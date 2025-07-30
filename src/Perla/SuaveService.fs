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
                subscription |> Option.iter(_.Dispose())
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
}

type SuaveTestingContext = {
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  Config: PerlaConfig aval
  FsManager: PerlaFsManager
  NotifyTestEvent: Result<TestEvent, JDeck.DecodeError> -> unit
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

  member this.NotifyTestEvent =
    match this with
    | SuaveContext _ -> None
    | SuaveTestingContext ctx -> Some ctx.NotifyTestEvent

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
  open FsToolkit.ErrorHandling

  module Proxy =
    open Suave.Utils
    open Suave.Sockets
    open System.Net.Http

    let private client =
      lazy
        (let handler = new HttpClientHandler()
         handler.AutomaticDecompression <- DecompressionMethods.All
         let client = new HttpClient(handler)
         client.DefaultRequestHeaders.ExpectContinue <- false
         client)

    let private (?) headers (name: string) =
      headers
      |> Seq.tryFind(fun (k, _) ->
        String.Equals(k, name, StringComparison.InvariantCultureIgnoreCase))
      |> Option.map snd

    let private httpWebResponseToHttpContext
      (ctx: HttpContext)
      (response: HttpResponseMessage)
      =
      let status =
        match HttpCode.tryParse(int response.StatusCode) with
        | Choice1Of2 x -> x.status
        | _ -> HTTP_502.status

      let headers =
        response.Headers
        |> Seq.map(fun (KeyValue(k, v)) -> k, v |> String.concat ";")
        |> Seq.toList

      // Check if response is chunked
      let isChunked =
        response.Headers.TransferEncodingChunked.HasValue
        && response.Headers.TransferEncodingChunked.Value
        || response.Headers.Contains("Transfer-Encoding")
           && response.Headers.GetValues("Transfer-Encoding")
              |> Seq.exists(fun v -> v.Contains("chunked"))

      let writeHeaders(conn: Connection) : SocketOp<_> = taskResult {
        // Write all response headers
        for (key, value) in headers do
          // Skip Content-Length for chunked responses
          if
            not(
              isChunked
              && String.Equals(
                key,
                "Content-Length",
                StringComparison.InvariantCultureIgnoreCase
              )
            )
          then
            do! conn.asyncWriteLn(sprintf "%s: %s" key value)

        // Write content headers if they exist
        for KeyValue(key, value) in response.Content.Headers do
          // Skip Content-Length for chunked responses
          if
            not(
              isChunked
              && String.Equals(
                key,
                "Content-Length",
                StringComparison.InvariantCultureIgnoreCase
              )
            )
          then
            do!
              conn.asyncWriteLn(sprintf "%s: %s" key (String.concat ";" value))


        // Write empty line to end headers
        do! conn.asyncWriteLn ""
        do! conn.flush()
      }

      {
        ctx with
            response = {
              ctx.response with
                  status = status
                  headers = headers
                  content =
                    SocketTask(fun (conn, _) -> taskResult {
                      do! writeHeaders conn
                      use! stream = response.Content.ReadAsStreamAsync()

                      if isChunked then
                        // For chunked responses, transfer the stream directly
                        do! transferStreamChunked conn stream
                      else
                        do! transferStream conn stream
                    })
            }
      }

    let proxy(newHost: Uri) : WebPart =
      fun ctx -> asyncEx {
        let remappedAddress =
          if [ 80; 443 ] |> Seq.contains newHost.Port then
            sprintf
              "%s://%s%s%s"
              newHost.Scheme
              newHost.Host
              ctx.request.path
              ctx.request.rawQuery
          else
            sprintf
              "%s://%s:%i%s%s"
              newHost.Scheme
              newHost.Host
              newHost.Port
              ctx.request.path
              ctx.request.rawQuery

        // Configure client to not buffer responses for proper chunked handling
        // Enable automatic decompression while preserving chunked responses
        let client = client.Value
        use request = new HttpRequestMessage()
        request.RequestUri <- Uri remappedAddress
        request.Method <- HttpMethod(ctx.request.rawMethod)

        // Check if request is chunked
        let isChunkedRequest =
          ctx.request.headers
          |> List.tryFind(fun (key, _) ->
            String.Equals(
              key,
              "Transfer-Encoding",
              StringComparison.InvariantCultureIgnoreCase
            ))
          |> Option.map snd
          |> Option.map(fun v -> v.Contains("chunked"))
          |> Option.defaultValue false

        match ctx.request.headers?("User-Agent") with
        | Some x -> request.Headers.UserAgent.ParseAdd x
        | None -> ()

        match ctx.request.headers?("Accept") with
        | Some x -> request.Headers.Accept.ParseAdd x
        | None -> ()

        match
          ctx.request.headers?("Date")
          |> Option.bind(Parse.dateTime >> Choice.toOption)
        with
        | Some x -> request.Headers.Date <- x
        | None -> ()

        match ctx.request.headers?("Host") with
        | Some x -> request.Headers.Host <- x
        | None -> ()

        // Prepare content if needed
        let hasBody =
          [ HttpMethod.POST; HttpMethod.PUT; HttpMethod.PATCH ]
          |> Seq.contains ctx.request.method

        if hasBody then
          request.Content <- new ByteArrayContent(ctx.request.rawForm)

        // Set Content-Type on content headers if present
        match ctx.request.headers?("Content-Type"), request.Content with
        | Some x, NonNull c ->
          c.Headers.ContentType <-
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse(x)
        | _ -> ()

        // Only add Content-Length if not chunked and content exists
        if not isChunkedRequest then
          match ctx.request.headers?("Content-Length"), request.Content with
          | Some x, NonNull c ->
            match Parse.int64 x with
            | Choice1Of2 v -> c.Headers.ContentLength <- Nullable v
            | _ -> ()
          | _ -> ()

        // Forward Transfer-Encoding header if present
        match ctx.request.headers?("Transfer-Encoding") with
        | Some x ->
          request.Headers.TransferEncodingChunked <- x.Contains("chunked")
        | None -> ()

        // Forward additional headers that might be important for chunked responses
        match ctx.request.headers?("Connection") with
        | Some x -> request.Headers.Connection.ParseAdd x
        | None -> ()

        match ctx.request.headers?("Keep-Alive") with
        | Some x -> request.Headers.Add("Keep-Alive", x)
        | None -> ()

        request.Headers.Add("X-Forwarded-For", ctx.request.host)

        try
          let! response = client.SendAsync request

          return httpWebResponseToHttpContext ctx response |> Some
        with exn ->
          return!
            (OK $"Unable to proxy the request: {exn.Message}"
             >=> setStatus HTTP_502)
              ctx
      }

  let createProxyWebparts (logger: ILogger) (proxyConfig: Map<string, string>) =
    proxyConfig
    |> Map.toList
    |> List.map(fun (pathPattern, targetUrl) ->
      let targetUri = Uri(targetUrl)

      logger.LogInformation(
        "Creating proxy for path {PathPattern} to target {TargetUrl}",
        pathPattern,
        targetUrl
      )

      pathStarts pathPattern
      >=> (fun ctx -> async {
        logger.LogInformation(
          "Proxying {Path} to {TargetUrl}",
          ctx.request.url,
          targetUrl + ctx.request.url.PathAndQuery
        )

        let! result = Proxy.proxy targetUri ctx

        match result with
        | None ->
          logger.LogWarning("Proxy failed for {Path}", ctx.request.url)
        | Some response ->
          // Log if we detect chunked responses for debugging
          if
            response.response.headers
            |> List.exists(fun (key, _) ->
              String.Equals(
                key,
                "Transfer-Encoding",
                StringComparison.InvariantCultureIgnoreCase
              ))
          then
            logger.LogDebug(
              "Proxied chunked response for {Path}",
              ctx.request.url
            )

        return result
      }))
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
          processJsonAsJs(Encoding.UTF8.GetString content)
          |> Encoding.UTF8.GetBytes
        ShouldProcess = true
      }
    | "text/css", JS -> {
        ContentType = "application/javascript"
        Content =
          processCssAsJs (Encoding.UTF8.GetString content) reqPath
          |> Encoding.UTF8.GetBytes
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
          let content = Encoding.UTF8.GetBytes fileContent.content

          // Process the file based on request type
          let processingResult =
            determineFileProcessing mimeType requestedAs content requestPath

          // Log unsupported JS transformations if needed
          if requestedAs = JS && not processingResult.ShouldProcess then
            logUnsupportedJsTransformation logger mimeType requestPath

          return!
            (setMimeType processingResult.ContentType
             >=> ok processingResult.Content)
              ctx
        | BinaryFile binaryInfo ->
          return!
            (setMimeType binaryInfo.mimetype
             >=> Stream.okStream(
               async { return File.OpenRead(UMX.untag binaryInfo.source) }
             ))
              ctx

      | None -> return None
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
  // For reload events (non-HMR)
  let createReloadEventDataSimple(event: FileChangedEvent) =
    Json.ToText(
      {|
        oldName = event.oldName
        name = event.name
      |},
      true
    )

  let createReloadMessage(event: FileChangedEvent) : Message =
    let data = createReloadEventDataSimple event
    let id = string(DateTimeOffset.Now.ToUnixTimeSeconds())
    Message.createType id data "reload"

  let createHmrEventData
    (event: FileChangedEvent)
    (transform: FileTransform)
    (target: string)
    =
    let oldPath =
      event.oldPath
      |> Option.map(fun oldPath -> $"{oldPath}".Replace('\\', '/'))

    let replaced = (UMX.untag event.name).Replace('\\', '/')

    match target with
    | "style" ->
      Json.ToText(
        {|
          target = "style"
          url = (UMX.untag event.serverPath)
          content = transform.content
        |},
        true
      )
    | "link" ->
      Json.ToText(
        {|
          target = "link"
          href = (UMX.untag event.serverPath)
        |},
        true
      )
    | _ -> failwith "Unknown target for HMR event"

  let createHmrMessages
    (event: FileChangedEvent)
    (file: FileContent)
    : Message list =
    let styleMsg =
      let data =
        createHmrEventData
          event
          {
            content = file.content
            extension = ".css"
            fileLocation = UMX.untag file.source
          }
          "style"

      let id = string(DateTimeOffset.Now.ToUnixTimeSeconds())
      Message.createType id data "replace-css"

    let linkMsg =
      let data =
        createHmrEventData
          event
          {
            content = file.content
            extension = ".css"
            fileLocation = UMX.untag file.source
          }
          "link"

      let id = string(DateTimeOffset.Now.ToUnixTimeSeconds())
      Message.createType id data "replace-css"

    [ styleMsg; linkMsg ]

  let createLiveReloadMessages
    (vfs: VirtualFileSystem)
    (event: FileChangedEvent)
    : Message list =
    match event.changeType with
    | Changed ->
      // Check if it's a CSS file for HMR
      match vfs.Resolve event.serverPath with
      | Some(TextFile file) when file.mimetype = "text/css" ->
        createHmrMessages event file
      | _ -> [ createReloadMessage event ]
    | Created
    | Deleted
    | Renamed -> [ createReloadMessage event ]

  let sseBody
    vfs
    (fileChangedEvents: IAsyncEnumerable<FileChangedEvent>)
    (out: Sockets.Connection)
    : Async<unit> =
    asyncEx {
      // Handle file change events
      for event in fileChangedEvents do
        let msgs = createLiveReloadMessages vfs event

        for msg in msgs do
          do! send out msg :> Task
    }

  let sseHandler(vfs: VirtualFileSystem) : WebPart =

    fun ctx -> async {
      let! token = Async.CancellationToken

      let fileChangedEvents =
        vfs.FileChanges |> Observable.toCancellableAsyncEnumerable token

      return!
        handShake
          (sseBody vfs fileChangedEvents >> Sockets.SocketOp.ofAsync)
          ctx
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

  let indexHandler(configA: PerlaConfig aval, fsManager: PerlaFsManager) =
    fun ctx -> async {
      let content = fsManager.ResolveIndex |> AVal.force

      let map =
        fsManager.ResolveImportMap
        |> ImportMaps.withPathsA configA
        |> AVal.force

      let config = configA |> AVal.force

      use context = BrowsingContext.New Configuration.Default
      let parser = context.GetService<IHtmlParser>() |> nonNull
      use doc = parser.ParseDocument content
      let body = Build.EnsureBody doc
      let head = Build.EnsureHead doc

      let script = doc.CreateElement "script"
      script.SetAttribute("type", "importmap")
      script.TextContent <- Json.ToText map
      head.AppendChild script |> ignore

      if config.devServer.liveReload then
        let liveReload = doc.CreateElement "script"
        liveReload.SetAttribute("type", "application/javascript")
        liveReload.SetAttribute("src", "/~perla~/livereload.js")
        head.AppendChild liveReload |> ignore

      return! (setMimeType "text/html" >=> OK(doc.ToHtml())) ctx
    }

  let testingIndexHandler
    (configA: PerlaConfig aval, fsManager: PerlaFsManager)
    =
    fun ctx -> async {
      let content = fsManager.ResolveIndex |> AVal.force

      use context = BrowsingContext.New(Configuration.Default)
      let parser = context.GetService<IHtmlParser>() |> nonNull
      use doc = parser.ParseDocument(content)

      // remove any existing entry points, we don't need them in the tests
      doc.QuerySelectorAll("[data-entry-point][type=module]")
      |> Seq.iter(_.Remove())

      doc.QuerySelectorAll("[data-entry-point][rel=stylesheet]")
      |> Seq.iter(_.Remove())

      doc.QuerySelectorAll("[data-entry-point=standalone][type=module]")
      |> Seq.iter(_.Remove())

      let body = Build.EnsureBody doc
      let head = Build.EnsureHead doc
      let mochaStyles: Dom.IElement = doc.CreateElement "link"
      mochaStyles.SetAttribute("href", "https://unpkg.com/mocha/mocha.css")
      mochaStyles.SetAttribute("rel", "stylesheet")
      mochaStyles.SetAttribute("type", "text/css")
      head.AppendChild mochaStyles |> ignore

      let script: Dom.IElement = doc.CreateElement "script"
      script.SetAttribute("type", "importmap")

      let mergedImportMap =
        fsManager.ResolveImportMap
        |> ImportMaps.withPathsA configA
        |> AVal.force

      let config = configA |> AVal.force

      script.TextContent <- Json.ToText(mergedImportMap)
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
        head.AppendChild liveReload |> ignore

      return! (setMimeType "text/html" >=> OK(doc.ToHtml())) ctx
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
           >=> setStatus HTTP_404)
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
// SPA Fallback
// ============================================================================

module SpaFallback =

  let spaFallback
    (configA: PerlaConfig aval)
    (fsManager: PerlaFsManager)
    : WebPart =
    fun ctx -> async {
      let path = ctx.request.url.AbsolutePath

      // Skip if it's an API call, Perla internal path, or has file extension
      if
        path.StartsWith "/api/"
        || path.StartsWith "/~perla~/"
        || Path.HasExtension path
      then
        return None
      else
        // Serve index.html directly (do not redirect)
        return! PerlaHandlers.indexHandler (configA, fsManager) ctx
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
    (notifyTestEvent: Result<TestEvent, JDeck.DecodeError> -> unit)
    : WebPart =
    fun ctx -> async {

      let content = Encoding.UTF8.GetString ctx.request.rawForm

      match Json.TestEventFromJson content with
      | Result.Ok testEvent ->
        notifyTestEvent(Ok testEvent)
        return! OK "Event processed" ctx
      | Result.Error err ->
        notifyTestEvent(Result.Error err)
        return! BAD_REQUEST "Invalid test event format" ctx
    }

// ============================================================================
// Client Log Forwarding Endpoint
// ============================================================================

module ClientLog =

  type ClientLogLevel =
    | Debug
    | Info
    | Warn
    | Error

  let tryParseLogLevel(level: string) =
    match level.ToLowerInvariant() with
    | "debug" -> Debug
    | "info"
    | "log" -> Info
    | "warn" -> Warn
    | "error" -> Error
    | _ -> Info

  let tryExtractModuleResolutionError(msg: string) : string option =
    let pattern =
      System.Text.RegularExpressions.Regex
        @"Failed to resolve module specifier ""([^\""]+)"""

    let m = pattern.Match(msg)

    if m.Success && m.Groups.Count > 1 then
      Some m.Groups.[1].Value
    else
      None

  let clientLogHandler(logger: ILogger) : WebPart =
    path "/~perla~/log-client-error"
    >=> POST
    >=> fun ctx -> async {
      let decoded =
        JDeck.Decoding.fromBytes(
          ctx.request.rawForm,
          JDeck.Decode.Decode.sequence(fun _ -> ClientLogMessageDecoder)
        )

      let log logMsg =
        match tryParseLogLevel logMsg.level with
        | Debug -> logger.LogDebug(logMsg.message)
        | Info -> logger.LogInformation(logMsg.message)
        | Warn -> logger.LogWarning(logMsg.message)
        | Error ->
          logger.LogError
            $"{logMsg.timestamp}:{logMsg.message} - {logMsg.stack}"

      use _ =
        Serilog.Context.LogContext.PushProperty(
          "PlBrowser",
          Logger.Constants.BrowserPrefix
        )

      match decoded with
      | Ok logs ->
        for logMsg in logs do
          log logMsg

          match tryExtractModuleResolutionError logMsg.message with
          | Some specifier ->
            logger.LogInformation
              $"It seems you or your dependencies tried to call a deep import for this package, please add it as a dependency calling perla add {specifier}"
          | None -> ()

        return! OK "Logged" ctx
      | _ ->
        let decoded =
          JDeck.Decoding.fromBytes(ctx.request.rawForm, ClientLogMessageDecoder)

        match decoded with
        | Ok logMsg ->
          log logMsg

          match tryExtractModuleResolutionError logMsg.message with
          | Some specifier ->
            logger.LogInformation
              $"It seems you or your dependencies tried to call a deep import for this package, please add it as a dependency calling perla add {specifier}"
          | None -> ()

          return! OK "Logged" ctx
        | _ ->
          logger.LogError "Failed to parse client log"
          return! BAD_REQUEST "Invalid log format" ctx
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
                let key = m.Groups[1].Value

                let fmt =
                  if m.Groups.Count > 2 && m.Groups[2].Success then
                    m.Groups[2].Value
                  else
                    ""

                match fields.TryFind key with
                | Some(:? IFormattable as f) when not(String.IsNullOrEmpty(fmt)) ->
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
                  let key = m.Groups[1].Value

                  let fmt =
                    if m.Groups.Count > 2 && m.Groups[2].Success then
                      m.Groups[2].Value
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

    let proxyWebparts =
      ProxyService.createProxyWebparts suaveCtx.Logger config.devServer.proxy

    choose [
      // Perla internal endpoints
      path "/~perla~/sse" >=> LiveReload.sseHandler suaveCtx.VirtualFileSystem

      ClientLog.clientLogHandler suaveCtx.Logger
      PerlaHandlers.liveReloadScript suaveCtx.FsManager
      PerlaHandlers.workerScript suaveCtx.FsManager
      PerlaHandlers.testingHelpers suaveCtx.FsManager
      PerlaHandlers.mochaRunner suaveCtx.FsManager

      // Virtual file system (before SPA fallback)
      VirtualFiles.resolveFile suaveCtx

      // Serve index.html for root
      path "/"
      >=> PerlaHandlers.indexHandler(suaveCtx.Config, suaveCtx.FsManager)

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
        >=> TestingHandlers.testingEvents suaveCtx.NotifyTestEvent.Value
      ]

      // Environment variables endpoint (if enabled)
      if config.enableEnv then
        path(UMX.untag config.envPath)
        >=> PerlaHandlers.envHandler suaveCtx.FsManager suaveCtx.Logger
      else
        never

      // Proxy endpoints (if configured)
      proxyWebparts

      // SPA fallback for extensionless paths
      SpaFallback.spaFallback suaveCtx.Config suaveCtx.FsManager

      // Final fallback
      NOT_FOUND "Resource not found"
    ]

  let createApp(suaveCtx: SuaveServerContext) =
    let config = AVal.force suaveCtx.Config

    let proxyWebparts =
      ProxyService.createProxyWebparts suaveCtx.Logger config.devServer.proxy

    choose [
      // Perla internal endpoints
      path "/~perla~/sse" >=> LiveReload.sseHandler suaveCtx.VirtualFileSystem

      ClientLog.clientLogHandler suaveCtx.Logger
      PerlaHandlers.liveReloadScript suaveCtx.FsManager
      PerlaHandlers.workerScript suaveCtx.FsManager
      PerlaHandlers.testingHelpers suaveCtx.FsManager
      PerlaHandlers.mochaRunner suaveCtx.FsManager

      // Virtual file system (before SPA fallback)
      VirtualFiles.resolveFile suaveCtx

      // Serve index.html for root
      path "/"
      >=> PerlaHandlers.indexHandler(suaveCtx.Config, suaveCtx.FsManager)

      // Environment variables endpoint (if enabled)
      if config.enableEnv then
        path(UMX.untag config.envPath)
        >=> PerlaHandlers.envHandler suaveCtx.FsManager suaveCtx.Logger
      else
        never

      // Proxy endpoints (if configured)
      proxyWebparts

      // SPA fallback for extensionless paths
      SpaFallback.spaFallback suaveCtx.Config suaveCtx.FsManager

      // Final fallback
      NOT_FOUND "Resource not found"
    ]

  let createStaticServerApp(suaveCtx: SuaveServerContext) =
    let config = AVal.force suaveCtx.Config

    let proxyWebparts =
      ProxyService.createProxyWebparts suaveCtx.Logger config.devServer.proxy

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
