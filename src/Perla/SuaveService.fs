namespace Perla.SuaveService

open System
open System.IO
open System.Net
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging

open AngleSharp
open AngleSharp.Html.Parser

open FSharp.Control
open FSharp.Data.Adaptive
open IcedTasks

open System.Collections.Generic
open Perla
open Perla.Types
open Perla.Units
open Perla.VirtualFs
open Perla.FileSystem
open Perla.Build
open Perla.Json
open Perla.Plugins
open FSharp.UMX

module Observable =

  type ObservableAsyncEnumerable<'T>
    (source: IObservable<'T>, cancellationToken: CancellationToken) =
    let queue = Collections.Concurrent.ConcurrentQueue<'T>()
    let mutable completed = false
    let mutable error: exn option = None
    let mutable signal: TaskCompletionSource<bool> option = None
    let mutable subscription: IDisposable option = None
    let lockObj = obj()

    let onNotification() =
      lock lockObj (fun () ->
        match signal with
        | Some tcs ->
          signal <- None
          tcs.TrySetResult(true) |> ignore
        | None -> ())

    let observer =
      { new IObserver<'T> with
          member _.OnNext(value) =
            queue.Enqueue(value)
            onNotification()

          member _.OnError(ex) =
            error <- Some ex
            completed <- true
            onNotification()

          member _.OnCompleted() =
            completed <- true
            onNotification()
      }

    let waitForSignal(enumeratorToken: CancellationToken) =
      lock lockObj (fun () ->
        match signal with
        | Some existingTcs -> existingTcs.Task
        | None ->
          let tcs = TaskCompletionSource<bool>()
          signal <- Some tcs

          // Create combined cancellation token
          use combinedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
              cancellationToken,
              enumeratorToken
            )

          let combinedToken = combinedCts.Token

          // Handle cancellation
          if combinedToken.IsCancellationRequested then
            tcs.TrySetCanceled(combinedToken) |> ignore
          else
            combinedToken.Register(fun () ->
              tcs.TrySetCanceled(combinedToken) |> ignore)
            |> ignore

          tcs.Task)

    interface IAsyncEnumerable<'T> with
      member _.GetAsyncEnumerator(enumeratorToken) =
        // Subscribe on first enumeration
        if subscription.IsNone then
          subscription <- Some(source.SubscribeSafe(observer))

        let mutable current = Unchecked.defaultof<'T>

        { new IAsyncEnumerator<'T> with
            member _.Current = current

            member _.MoveNextAsync() = valueTask {
              // Create combined cancellation token
              use combinedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                  cancellationToken,
                  enumeratorToken
                )

              let combinedToken = combinedCts.Token

              combinedToken.ThrowIfCancellationRequested()

              let mutable keepLooping = true
              let mutable result = false

              while keepLooping do
                // Try to dequeue an item
                match queue.TryDequeue() with
                | true, item ->
                  current <- item
                  result <- true
                  keepLooping <- false
                | false, _ ->
                  // Check if completed
                  if completed then
                    match error with
                    | Some ex -> raise ex
                    | None ->
                      result <- false
                      keepLooping <- false
                  else
                    // Wait for notification
                    try
                      let! _ = waitForSignal(enumeratorToken)
                      // Continue the loop to try dequeuing again
                      ()
                    with :? OperationCanceledException ->
                      combinedToken.ThrowIfCancellationRequested()
                      result <- false
                      keepLooping <- false

              return result
            }

            member _.DisposeAsync() =
              lock lockObj (fun () ->
                subscription |> Option.iter(fun s -> s.Dispose())
                subscription <- None
                signal <- None)

              ValueTask.CompletedTask
        }

  let toAsyncEnumerable(source: IObservable<'T>) =
    ObservableAsyncEnumerable(source, CancellationToken.None)
    :> IAsyncEnumerable<'T>

  let toCancellableAsyncEnumerable
    (token: CancellationToken)
    (source: IObservable<'T>)
    =
    ObservableAsyncEnumerable(source, token) :> IAsyncEnumerable<'T>

// ============================================================================
// Suave imports
// ============================================================================

open Suave
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.RequestErrors
open Suave.Writers
open Suave.Proxy
open System.Reactive.Subjects

// ============================================================================
// Core Types
// ============================================================================

[<AutoOpen>]
module TestableTypes =

  type HttpResponse = {
    Headers: Map<string, string>
    Body: string
    StatusCode: int
    ContentType: string option
  }

  type HttpRequest = {
    Path: string
    Method: string
    Query: Map<string, string>
    Headers: Map<string, string>
  }

  type ResponseWriter = {
    WriteText: string -> Task<unit>
    WriteBytes: byte[] -> Task<unit>
    SetHeader: string -> string -> unit
    SetStatusCode: int -> unit
    SetContentType: string -> unit
    Flush: unit -> Task<unit>
  }

type SuaveContext = {
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  Config: PerlaConfig aval
  FsManager: PerlaFsManager
  FileChangedEvents: IObservable<FileChangedEvent>
}

type SuaveTestingContext = {
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  Config: PerlaConfig aval
  FsManager: PerlaFsManager
  FileChangedEvents: IObservable<FileChangedEvent>
  TestEvents: ISubject<TestEvent>
}

type SuaveServerContext =
  | SuaveContext of SuaveContext
  | SuaveTestingContext of SuaveTestingContext

  member this.Logger =
    match this with
    | SuaveContext ctx -> ctx.Logger
    | SuaveTestingContext ctx -> ctx.Logger

  member this.VirtualFileSystem =
    match this with
    | SuaveContext ctx -> ctx.VirtualFileSystem
    | SuaveTestingContext ctx -> ctx.VirtualFileSystem

  member this.Config =
    match this with
    | SuaveContext ctx -> ctx.Config
    | SuaveTestingContext ctx -> ctx.Config

  member this.FsManager =
    match this with
    | SuaveContext ctx -> ctx.FsManager
    | SuaveTestingContext ctx -> ctx.FsManager

  member this.FileChangedEvents =
    match this with
    | SuaveContext ctx -> ctx.FileChangedEvents
    | SuaveTestingContext ctx -> ctx.FileChangedEvents

  member this.TestEvents =
    match this with
    | SuaveContext _ -> None
    | SuaveTestingContext ctx -> Some ctx.TestEvents

// ============================================================================
// MIME Type Detection
// ============================================================================

module MimeTypes =
  [<Literal>]
  let DefaultMimeType = "application/octet-stream"

  let tryGetContentType(filePath: string) =
    let extension =
      match Path.GetExtension(filePath) with
      | null -> ""
      | ext -> ext.ToLowerInvariant()

    match extension with
    | ".html"
    | ".htm" -> Some "text/html"
    | ".css" -> Some "text/css"
    | ".js"
    | ".mjs" -> Some "application/javascript"
    | ".json" -> Some "application/json"
    | ".png" -> Some "image/png"
    | ".jpg"
    | ".jpeg" -> Some "image/jpeg"
    | ".gif" -> Some "image/gif"
    | ".svg" -> Some "image/svg+xml"
    | ".ico" -> Some "image/x-icon"
    | ".woff" -> Some "font/woff"
    | ".woff2" -> Some "font/woff2"
    | ".ttf" -> Some "font/ttf"
    | ".otf" -> Some "font/otf"
    | ".txt" -> Some "text/plain"
    | ".xml" -> Some "application/xml"
    | ".pdf" -> Some "application/pdf"
    | ".zip" -> Some "application/zip"
    | ".map" -> Some "application/json"
    | _ -> None

  let getContentType(filePath: string) =
    tryGetContentType filePath |> Option.defaultValue DefaultMimeType

// ============================================================================
// Proxy using Suave.Proxy
// ============================================================================

module ProxyService =

  let createProxyWebparts(proxyConfig: Map<string, string>) =
    proxyConfig
    |> Map.toList
    |> List.map(fun (pathPattern, targetUrl) ->
      let targetUri = Uri(targetUrl)
      pathStarts pathPattern >=> proxy targetUri)
    |> function
      | [] -> never
      | [ single ] -> single
      | multiple -> choose multiple

// ============================================================================
// Virtual File System Integration
// ============================================================================

module VirtualFiles =

  // File processing types matching ASP.NET Core implementation
  [<Struct>]
  type RequestedAs =
    | JS
    | Normal

  type FileProcessingResult = {
    ContentType: string
    Content: byte[]
    ShouldProcess: bool
  }

  // Pure function to transform CSS content to JS (matching ASP.NET Core)
  let processCssAsJs (content: string) (url: string) =
    $"""const style=document.createElement('style');style.setAttribute("url", "{url}");
document.head.appendChild(style).innerHTML=String.raw`{content}`;"""

  // Pure function to transform JSON content to JS (matching ASP.NET Core)
  let processJsonAsJs(content: string) = $"""export default {content};"""

  // Pure function to determine how to process a file
  let determineFileProcessing
    (mimeType: string)
    (requestedAs: RequestedAs)
    (content: byte[])
    (reqPath: string)
    : FileProcessingResult =

    match mimeType, requestedAs with
    | "application/json", JS -> {
        ContentType = "application/javascript"
        Content =
          processJsonAsJs(System.Text.Encoding.UTF8.GetString content)
          |> System.Text.Encoding.UTF8.GetBytes
        ShouldProcess = true
      }
    | "text/css", JS -> {
        ContentType = "application/javascript"
        Content =
          processCssAsJs (System.Text.Encoding.UTF8.GetString content) reqPath
          |> System.Text.Encoding.UTF8.GetBytes
        ShouldProcess = true
      }
    | _, Normal -> {
        ContentType = mimeType
        Content = content
        ShouldProcess = false
      }
    | _, JS ->
        {
          ContentType = mimeType
          Content = content
          ShouldProcess = false
        }

  // Log function for unsupported JS transformations
  let logUnsupportedJsTransformation
    (logger: ILogger)
    (mimeType: string)
    (reqPath: string)
    =
    logger.LogWarning(
      "Requested JS - {MimeType} - {RequestPath} as JS, this file type is not supported as JS, sending default content",
      mimeType,
      reqPath
    )

  // Determine request type from query string
  let determineRequestedAs(ctx: HttpContext) : RequestedAs =
    if ctx.request.query |> List.exists(fun (key, _) -> key = "js") then
      JS
    else
      Normal

  let processFile
    (logger: ILogger)
    (vfs: VirtualFileSystem)
    (requestPath: string)
    : WebPart =
    fun ctx -> async {
      let requestedAs = determineRequestedAs ctx

      match vfs.Resolve(UMX.tag<ServerUrl> requestPath) with
      | Some file ->
        match file with
        | TextFile fileContent ->
          let mimeType = fileContent.mimetype
          let content = System.Text.Encoding.UTF8.GetBytes fileContent.content

          // Process the file based on request type
          let processingResult =
            determineFileProcessing mimeType requestedAs content requestPath

          // Log unsupported JS transformations if needed
          if requestedAs = JS && not processingResult.ShouldProcess then
            logUnsupportedJsTransformation logger mimeType requestPath

          return!
            (setMimeType processingResult.ContentType
             >=> Successful.ok processingResult.Content)
              ctx
        | BinaryFile binaryInfo ->
          return!
            (setMimeType binaryInfo.mimetype
             >=> Stream.okStream(
               async { return File.OpenRead(UMX.untag binaryInfo.source) }
             ))
              ctx

      | None -> return! NOT_FOUND "File not found in virtual file system" ctx
    }

  let resolveFile(suaveCtx: SuaveServerContext) : WebPart =
    fun ctx -> async {
      let requestPath = ctx.request.url.AbsolutePath

      // Skip Perla internal paths
      if requestPath.StartsWith("/~perla~/") then
        return None
      else
        return!
          processFile suaveCtx.Logger suaveCtx.VirtualFileSystem requestPath ctx
    }

// ============================================================================
// Server-Sent Events for Live Reload
// ============================================================================

module LiveReload =

  open Suave.EventSource
  // Pure functions matching Server.fs implementation
  let createReloadEventData(event: FileChangedEvent) =
    Json.ToText(
      {|
        oldName = event.oldName
        name = event.name
      |},
      true
    )

  let createHmrEventData (event: FileChangedEvent) (transform: FileTransform) =
    let oldPath =
      event.oldPath
      |> Option.map(fun oldPath -> $"{oldPath}".Replace('\\', '/'))

    let replaced = (UMX.untag event.name).Replace('\\', '/')

    Json.ToText(
      {|
        oldName = event.oldName
        oldPath = oldPath
        name = replaced
        url = event.serverPath
        localPath = replaced
        content = transform.content
      |},
      true
    )
  // Pure functions for creating SSE messages
  let createReloadMessage(event: FileChangedEvent) : Message =
    let data = createReloadEventData event
    let id = string(DateTimeOffset.Now.ToUnixTimeSeconds())
    Message.createType id data "reload"

  let createHmrMessage (event: FileChangedEvent) (file: FileContent) : Message =
    let data =
      createHmrEventData event {
        content = file.content
        extension = ".css"
      }

    let id = string(DateTimeOffset.Now.ToUnixTimeSeconds())
    Message.createType id data "replace-css"

  let createLiveReloadMessage
    (vfs: VirtualFileSystem)
    (event: FileChangedEvent)
    : Message =
    match event.changeType with
    | Changed ->
      // Check if it's a CSS file for HMR
      match vfs.Resolve event.serverPath with
      | Some(TextFile file) when file.mimetype = "text/css" ->
        createHmrMessage event file
      | _ -> createReloadMessage event
    | Created
    | Deleted
    | Renamed -> createReloadMessage event

  let sseBody
    vfs
    (fileChangedEvents: IAsyncEnumerable<FileChangedEvent>)
    (out: Sockets.Connection)
    : Async<unit> =
    asyncEx {
      // Handle file change events
      for event in fileChangedEvents do
        let msg = createLiveReloadMessage vfs event
        do! EventSource.send out msg :> Task
    }

  let sseHandler
    (vfs: VirtualFileSystem)
    (fileChangedEvents: IObservable<FileChangedEvent>)
    : WebPart =

    fun ctx -> async {
      let! token = Async.CancellationToken

      let fileChangedEvents =
        fileChangedEvents |> Observable.toCancellableAsyncEnumerable token

      return!
        handShake
          (sseBody vfs fileChangedEvents >> Sockets.SocketOp.ofAsync)
          ctx
    }

// ============================================================================
// SPA Fallback
// ============================================================================

module SpaFallback =

  let spaFallback(config: PerlaConfig) : WebPart =
    fun ctx -> async {
      let path = ctx.request.url.AbsolutePath

      // Skip if it's an API call, Perla internal path, or has file extension
      if
        path.StartsWith("/api/")
        || path.StartsWith("/~perla~/")
        || Path.HasExtension(path)
      then
        return None
      else
        // Redirect to index
        return! Redirection.FOUND "/" ctx
    }

// ============================================================================
// Perla-specific Handlers
// ============================================================================

module PerlaHandlers =

  let liveReloadScript(fsManager: PerlaFsManager) =
    path "/~perla~/livereload.js"
    >=> setMimeType "application/javascript"
    >=> fun ctx -> async {
      let! content =
        fsManager.ResolveLiveReloadScript() |> Async.AwaitCancellableTask

      return! OK content ctx
    }

  let workerScript(fsManager: PerlaFsManager) =
    path "/~perla~/worker.js"
    >=> setMimeType "application/javascript"
    >=> fun ctx -> async {
      let! content =
        fsManager.ResolveWorkerScript() |> Async.AwaitCancellableTask

      return! OK content ctx
    }

  let testingHelpers(fsManager: PerlaFsManager) =
    path "/~perla~/testing/helpers.js"
    >=> setMimeType "application/javascript"
    >=> fun ctx -> async {
      let! content =
        fsManager.ResolveTestingHelpersScript() |> Async.AwaitCancellableTask

      return! OK content ctx
    }

  let mochaRunner(fsManager: PerlaFsManager) =
    path "/~perla~/testing/mocha-runner.js"
    >=> setMimeType "application/javascript"
    >=> fun ctx -> async {
      let! content =
        fsManager.ResolveMochaRunnerScript() |> Async.AwaitCancellableTask

      return! OK content ctx
    }

  let indexHandler(config: PerlaConfig, fsManager: PerlaFsManager) =
    path "/"
    >=> setMimeType "text/html"
    >=> fun ctx -> async {
      let content = fsManager.ResolveIndex |> AVal.force
      let map = fsManager.ResolveImportMap |> AVal.force

      use context = BrowsingContext.New Configuration.Default
      let parser = context.GetService<IHtmlParser>() |> nonNull
      use doc = parser.ParseDocument content
      let body = Build.EnsureBody doc
      let head = Build.EnsureHead doc

      let script = doc.CreateElement "script"
      script.SetAttribute("type", "importmap")
      script.TextContent <- Json.ToText map
      head.AppendChild script |> ignore

      // remove standalone entry points, we don't need them in the browser
      doc.QuerySelectorAll "[data-entry-point=standalone][type=module]"
      |> Seq.iter(fun f -> f.Remove())

      if config.devServer.liveReload then
        let liveReload = doc.CreateElement "script"
        liveReload.SetAttribute("type", "application/javascript")
        liveReload.SetAttribute("src", "/~perla~/livereload.js")
        body.AppendChild liveReload |> ignore

      return! OK (doc.ToHtml()) ctx
    }

  let testingIndexHandler
    (
      config: PerlaConfig,
      fsManager: PerlaFsManager,
      importMap: Perla.PkgManager.ImportMap
    ) =
    path "/"
    >=> setMimeType "text/html"
    >=> fun ctx -> async {
      let content = fsManager.ResolveIndex |> AVal.force

      use context = BrowsingContext.New(Configuration.Default)
      let parser = context.GetService<IHtmlParser>() |> nonNull
      use doc = parser.ParseDocument(content)

      // remove any existing entry points, we don't need them in the tests
      doc.QuerySelectorAll("[data-entry-point][type=module]")
      |> Seq.iter(fun f -> f.Remove())

      doc.QuerySelectorAll("[data-entry-point][rel=stylesheet]")
      |> Seq.iter(fun f -> f.Remove())

      doc.QuerySelectorAll("[data-entry-point=standalone][type=module]")
      |> Seq.iter(fun f -> f.Remove())

      let body = Build.EnsureBody doc
      let head = Build.EnsureHead doc
      let mochaStyles: Dom.IElement = doc.CreateElement "link"
      mochaStyles.SetAttribute("href", "https://unpkg.com/mocha/mocha.css")
      mochaStyles.SetAttribute("rel", "stylesheet")
      mochaStyles.SetAttribute("type", "text/css")
      head.AppendChild mochaStyles |> ignore

      let script: Dom.IElement = doc.CreateElement "script"
      script.SetAttribute("type", "importmap")
      script.TextContent <- Json.ToText(importMap)
      head.AppendChild script |> ignore

      let mochaScript = doc.CreateElement "script"
      mochaScript.SetAttribute("type", "application/javascript")
      mochaScript.SetAttribute("src", "https://unpkg.com/mocha/mocha.js")
      body.AppendChild mochaScript |> ignore

      let mochaDiv = doc.CreateElement "div"
      mochaDiv.SetAttribute("id", "mocha")
      body.AppendChild mochaDiv |> ignore

      let runnerScript = doc.CreateElement "script"
      runnerScript.SetAttribute("type", "module")

      let! runnerContent =
        fsManager.ResolveMochaRunnerScript() |> Async.AwaitCancellableTask

      runnerScript.TextContent <- runnerContent
      body.AppendChild runnerScript |> ignore

      if config.devServer.liveReload then
        let liveReload = doc.CreateElement "script"
        liveReload.SetAttribute("type", "application/javascript")
        liveReload.SetAttribute("src", "/~perla~/livereload.js")
        body.AppendChild liveReload |> ignore

      return! OK (doc.ToHtml()) ctx
    }

  // Environment variables endpoint handler
  let envHandler (fsManager: PerlaFsManager) (logger: ILogger) : WebPart =
    fun ctx -> async {
      let envVars = fsManager.DotEnvContents |> AVal.force

      if Map.isEmpty envVars then
        logger.LogWarning(
          "An env file was requested but no env variables were found"
        )

        let message =
          """If you want to use env variables, remember to prefix them with 'PERLA_' e.g.
'PERLA_myApiKey' or 'PERLA_CLIENT_SECRET', then you will be able to import them via the env file"""

        logger.LogWarning("Env Content not found. {Message}", message)

        return!
          (setMimeType "application/json"
           >=> OK(Json.ToText({| message = message |}))
           >=> Writers.setStatus HTTP_404)
            ctx
      else
        let content =
          envVars
          |> Map.fold
            (fun (sb: StringBuilder) key value ->
              sb.AppendLine $"export const {key} = \"{value}\";")
            (StringBuilder())
          |> _.ToString()

        return! (setMimeType "text/javascript" >=> OK content) ctx
    }

// ============================================================================
// Testing Handlers
// ============================================================================

module TestingHandlers =
  open Fake.IO

  let testingFiles
    (fileGlobs: string seq option, testConfig: TestConfig)
    : WebPart =
    fun ctx -> async {
      let glob: Globbing.LazyGlobbingPattern = {
        BaseDirectory = "./tests"
        Excludes = [
          "**/bin/**"
          "**/obj/**"
          "**/*.fs"
          "**/*.fsproj"
          yield! testConfig.excludes
        ]
        Includes =
          match fileGlobs with
          | Some files ->
            if files |> Seq.isEmpty then
              [ "**/*.test.js"; "**/*.spec.js" ]
            else
              files |> Seq.toList
          | None -> [ "**/*.test.js"; "**/*.spec.js" ]
      }

      let files = [|
        for file in glob do
          let systemPath =
            (Path.GetFullPath file).Replace(Path.DirectorySeparatorChar, '/')

          let index = systemPath.IndexOf("/tests/")
          systemPath.Substring(index)
      |]

      return! (setMimeType "application/json" >=> OK(Json.ToText files)) ctx
    }

  let testingEnvironment(testConfig: TestConfig) : WebPart =
    fun ctx -> async {
      let result = {|
        testConfig with
            browsers =
              (testConfig.browsers |> Seq.map Encoders.Browser).ToString()
            browserMode =
              (testConfig.browserMode |> Encoders.BrowserMode).ToString()
            runId = Guid.NewGuid()
      |}

      return! (setMimeType "application/json" >=> OK(Json.ToText result)) ctx
    }

  let mochaSettings(mochaConfig: Map<string, obj> option) : WebPart =
    fun ctx -> async {
      let config = mochaConfig |> Option.defaultValue Map.empty
      return! (setMimeType "application/json" >=> OK(Json.ToText config)) ctx
    }

  let testingEvents
    (logger: ILogger, testEvents: ISubject<TestEvent>)
    : WebPart =
    fun ctx -> async {

      let content = Encoding.UTF8.GetString ctx.request.rawForm

      match Json.TestEventFromJson content with
      | Result.Ok testEvent ->
        testEvents.OnNext testEvent
        return! OK "Event processed" ctx
      | Result.Error err ->
        logger.LogError("Failed to parse test event: {Error}", err)
        return! BAD_REQUEST "Invalid test event format" ctx
    }

// ============================================================================
// Port Occupied Detection and Handling
// ============================================================================

module PortUtils =
  open System.Net.NetworkInformation

  let isAddressPortOccupied (address: string) (port: int) =
    try
      let props = IPGlobalProperties.GetIPGlobalProperties()
      let listeners = props.GetActiveTcpListeners()

      listeners
      |> Array.exists(fun listener ->
        listener.Port = port
        && (listener.Address = IPAddress.Any
            || listener.Address.ToString() = address
            || (address = "localhost" && listener.Address = IPAddress.Loopback)))
    with _ ->
      false

// ============================================================================
// Main Server Configuration
// ============================================================================

module SuaveServer =

  let toLoggary(logger: ILogger) =
    { new Logging.Logger with
        member this.log
          (level: Logging.LogLevel)
          (messageThunk: Logging.LogLevel -> Logging.Message)
          =
          let message = messageThunk level

          let logLevel =
            match level with
            | Logging.LogLevel.Verbose -> LogLevel.Trace
            | Logging.LogLevel.Debug -> LogLevel.Debug
            | Logging.LogLevel.Info -> LogLevel.Information
            | Logging.LogLevel.Warn -> LogLevel.Warning
            | Logging.LogLevel.Error -> LogLevel.Error
            | Logging.LogLevel.Fatal -> LogLevel.Critical

          // Simpler implementation: replace {key[:format]} with value, supporting format specifiers
          let formatMessage (template: string) (fields: Map<string, obj>) =
            let regex =
              System.Text.RegularExpressions.Regex(@"\{(\w+)(?::([^}]+))?\}")

            regex.Replace(
              template,
              fun (m: System.Text.RegularExpressions.Match) ->
                let key = m.Groups.[1].Value

                let fmt =
                  if m.Groups.Count > 2 && m.Groups.[2].Success then
                    m.Groups.[2].Value
                  else
                    ""

                match fields.TryFind key with
                | Some(:? IFormattable as f) when
                  not(System.String.IsNullOrEmpty(fmt))
                  ->
                  f.ToString(
                    fmt,
                    System.Globalization.CultureInfo.InvariantCulture
                  )
                | Some value -> string value
                | None -> m.Value
            )

          let messageText =
            match message.value with
            | Logging.Event template ->
              if message.fields.Count > 0 then
                formatMessage template message.fields
              else
                template
            | Logging.Gauge(value, units) -> $"Gauge: {value} {units}"

          logger.Log(logLevel, messageText)

        member _.logWithAck
          (level: Logging.LogLevel)
          (messageThunk: Logging.LogLevel -> Logging.Message)
          =
          async {
            let message = messageThunk level

            let logLevel =
              match level with
              | Logging.LogLevel.Verbose -> LogLevel.Trace
              | Logging.LogLevel.Debug -> LogLevel.Debug
              | Logging.LogLevel.Info -> LogLevel.Information
              | Logging.LogLevel.Warn -> LogLevel.Warning
              | Logging.LogLevel.Error -> LogLevel.Error
              | Logging.LogLevel.Fatal -> LogLevel.Critical

            let formatMessage (template: string) (fields: Map<string, obj>) =
              let regex = RegularExpressions.Regex(@"\{(\w+)(?::([^}]+))?\}")

              regex.Replace(
                template,
                fun (m: RegularExpressions.Match) ->
                  let key = m.Groups.[1].Value

                  let fmt =
                    if m.Groups.Count > 2 && m.Groups.[2].Success then
                      m.Groups.[2].Value
                    else
                      ""

                  match fields.TryFind key with
                  | Some(:? IFormattable as f) when
                    not(String.IsNullOrEmpty fmt)
                    ->
                    f.ToString(fmt, Globalization.CultureInfo.InvariantCulture)
                  | Some value -> string value
                  | None -> m.Value
              )

            let messageText =
              match message.value with
              | Logging.Event template ->
                if message.fields.Count > 0 then
                  formatMessage template message.fields
                else
                  template
              | Logging.Gauge(value, units) -> $"Gauge: {value} {units}"

            logger.Log(logLevel, messageText)
            return ()
          }

        member _.name = [| "Perla:Server:" |]
    }

  let createTestingApp(suaveCtx: SuaveServerContext) =
    let config = AVal.force suaveCtx.Config
    let proxyWebparts = ProxyService.createProxyWebparts config.devServer.proxy

    choose [
      // Perla internal endpoints
      path "/~perla~/sse"
      >=> LiveReload.sseHandler
        suaveCtx.VirtualFileSystem
        suaveCtx.FileChangedEvents

      PerlaHandlers.liveReloadScript suaveCtx.FsManager
      PerlaHandlers.workerScript suaveCtx.FsManager
      PerlaHandlers.testingHelpers suaveCtx.FsManager
      PerlaHandlers.mochaRunner suaveCtx.FsManager
      PerlaHandlers.indexHandler(config, suaveCtx.FsManager)

      // Testing endpoints
      pathStarts "/~perla~/testing/"
      >=> choose [
        path "/~perla~/testing/files"
        >=> TestingHandlers.testingFiles(None, config.testing)

        path "/~perla~/testing/environment"
        >=> TestingHandlers.testingEnvironment config.testing

        path "/~perla~/testing/mocha-settings"
        >=> TestingHandlers.mochaSettings None

        POST
        >=> path "/~perla~/testing/events"
        >=> TestingHandlers.testingEvents(
          suaveCtx.Logger,
          suaveCtx.TestEvents.Value
        )
      ]

      // Environment variables endpoint (if enabled)
      if config.enableEnv then
        path(UMX.untag config.envPath)
        >=> PerlaHandlers.envHandler suaveCtx.FsManager suaveCtx.Logger
      else
        never

      // Proxy endpoints (if configured)
      proxyWebparts

      // Virtual file system (before SPA fallback)
      VirtualFiles.resolveFile suaveCtx

      // SPA fallback
      SpaFallback.spaFallback config

      // Final fallback
      NOT_FOUND "Resource not found"
    ]

  let createApp(suaveCtx: SuaveServerContext) =
    let config = AVal.force suaveCtx.Config
    let proxyWebparts = ProxyService.createProxyWebparts config.devServer.proxy

    choose [
      // Perla internal endpoints
      path "/~perla~/sse"
      >=> LiveReload.sseHandler
        suaveCtx.VirtualFileSystem
        suaveCtx.FileChangedEvents

      PerlaHandlers.liveReloadScript suaveCtx.FsManager
      PerlaHandlers.workerScript suaveCtx.FsManager
      PerlaHandlers.testingHelpers suaveCtx.FsManager
      PerlaHandlers.mochaRunner suaveCtx.FsManager
      PerlaHandlers.indexHandler(config, suaveCtx.FsManager)

      // Environment variables endpoint (if enabled)
      if config.enableEnv then
        path(UMX.untag config.envPath)
        >=> PerlaHandlers.envHandler suaveCtx.FsManager suaveCtx.Logger
      else
        never

      // Proxy endpoints (if configured)
      proxyWebparts

      // Virtual file system (before SPA fallback)
      VirtualFiles.resolveFile suaveCtx

      // SPA fallback
      SpaFallback.spaFallback config

      // Final fallback
      NOT_FOUND "Resource not found"
    ]

  let createStaticServerApp(suaveCtx: SuaveServerContext) =
    let config = AVal.force suaveCtx.Config
    let proxyWebparts = ProxyService.createProxyWebparts config.devServer.proxy

    let outDir =
      Path.Combine(".", UMX.untag config.build.outDir) |> Path.GetFullPath

    choose [
      // Proxy endpoints (if configured)
      proxyWebparts

      // serve static files from the output directory
      Files.browseHome

      // SPA fallback for static files
      fun ctx -> async {
        let path = ctx.request.url.AbsolutePath

        // Skip if it's an API call or has file extension
        if path.StartsWith("/api/") || Path.HasExtension(path) then
          return None
        else
          // Serve index.html for SPA routes
          let indexPath = Path.Combine(outDir, "index.html")

          return! Files.sendFile indexPath false ctx
      }

      // Final fallback
      NOT_FOUND "Resource not found"
    ]

  let startServer
    (suaveCtx: SuaveServerContext)
    (cancellationToken: CancellationToken)
    =
    let config = AVal.force suaveCtx.Config

    let host, port =
      let host = config.devServer.host
      let port = config.devServer.port
      // Check if port is occupied and log if needed
      if PortUtils.isAddressPortOccupied host port then
        suaveCtx.Logger.LogWarning(
          "Address {Host}:{Port} is busy, Perla will bind to {Port}",
          host,
          port,
          port + 1
        )

        host, port + 1
      else
        host, port

    let serverConfig =
      let host = if host = "localhost" then "127.0.0.1" else host

      defaultConfig
        .withBindings([ HttpBinding.createSimple HTTP host port ])
        .withCancellationToken(cancellationToken)
        .withLogger(suaveCtx.Logger |> toLoggary)

    let app =
      match suaveCtx with
      | SuaveContext _ -> createApp suaveCtx
      | SuaveTestingContext _ -> createTestingApp suaveCtx

    suaveCtx.Logger.LogInformation
      $"Starting the Perla DevServer at: http://{host}:{port}/"

    startWebServer serverConfig app

  let startStaticServer
    (suaveCtx: SuaveContext)
    (cancellationToken: CancellationToken)
    =
    let config = AVal.force suaveCtx.Config

    let host, port =
      let host = config.devServer.host
      let port = config.devServer.port
      // Check if port is occupied and log if needed
      if PortUtils.isAddressPortOccupied host port then
        suaveCtx.Logger.LogWarning(
          "Address {Host}:{Port} is busy, Perla will bind to {Port}",
          host,
          port,
          port + 1
        )

        host, port + 1
      else
        host, port

    let serverConfig =
      let host = if host = "localhost" then "127.0.0.1" else host

      defaultConfig
        .withBindings([ HttpBinding.createSimple HTTP host port ])
        .withCancellationToken(cancellationToken)
        .withLogger(suaveCtx.Logger |> toLoggary)
        .withHomeFolder(
          UMX.untag config.build.outDir |> Path.GetFullPath |> Some
        )

    let app = createStaticServerApp(SuaveContext suaveCtx)

    suaveCtx.Logger.LogInformation
      $"Starting the Perla DevServer at: http://{host}:{port}/"

    startWebServer serverConfig app
