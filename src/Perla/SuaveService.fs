namespace Perla.SuaveService

open System
open System.IO
open System.Net
open System.Text
open System.Threading.Tasks
open Microsoft.Extensions.Logging

open IcedTasks
open FSharp.Data.Adaptive

open AngleSharp
open AngleSharp.Html.Parser
open AngleSharp.Html.Dom

open Perla
open Perla.Types
open Perla.Units
open Perla.VirtualFs
open Perla.FileSystem
open Perla.Build
open Perla.Json
open FSharp.UMX

module Observable =
  open System.Collections.Generic
  open System.Threading

  type ObservableAsyncEnumerable<'T>(source: IObservable<'T>) =
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

    let waitForSignal(cancellationToken: CancellationToken) =
      lock lockObj (fun () ->
        match signal with
        | Some existingTcs -> existingTcs.Task
        | None ->
          let tcs = TaskCompletionSource<bool>()
          signal <- Some tcs

          // Handle cancellation
          if cancellationToken.IsCancellationRequested then
            tcs.TrySetCanceled(cancellationToken) |> ignore
          else
            cancellationToken.Register(fun () ->
              tcs.TrySetCanceled(cancellationToken) |> ignore)
            |> ignore

          tcs.Task)

    interface IAsyncEnumerable<'T> with
      member _.GetAsyncEnumerator(cancellationToken) =
        // Subscribe on first enumeration
        if subscription.IsNone then
          subscription <- Some(source.SubscribeSafe(observer))

        let mutable current = Unchecked.defaultof<'T>

        { new IAsyncEnumerator<'T> with
            member _.Current = current

            member _.MoveNextAsync() = valueTask {
              cancellationToken.ThrowIfCancellationRequested()

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
                      let! _ = waitForSignal(cancellationToken)
                      // Continue the loop to try dequeuing again
                      ()
                    with :? OperationCanceledException ->
                      cancellationToken.ThrowIfCancellationRequested()
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
    ObservableAsyncEnumerable(source) :> IAsyncEnumerable<'T>


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

// ============================================================================
// Core Types
// ============================================================================

type SuaveContext = {
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  Config: PerlaConfig aval
  FsManager: PerlaFsManager
  FileChangedEvents: IObservable<FileChangedEvent>
  CompileErrorEvents: IObservable<string option>
}

// ============================================================================
// MIME Type Detection
// ============================================================================

module MimeTypes =

  let private defaultMimeType = "application/octet-stream"

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
    tryGetContentType filePath |> Option.defaultValue defaultMimeType

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

  let private processFile
    (logger: ILogger)
    (vfs: VirtualFileSystem)
    (requestPath: string)
    : WebPart =
    fun ctx -> async {

      match vfs.Resolve(UMX.tag<ServerUrl> requestPath) with
      | Some file ->
        match file with
        | TextFile fileContent ->
          let mimeType = fileContent.mimetype
          return! (setMimeType mimeType >=> OK fileContent.content) ctx

        | BinaryFile binaryInfo ->
          let mimeType = binaryInfo.mimetype
          // For binary files, we need to read from the source
          let bytes = File.ReadAllBytes(UMX.untag binaryInfo.source)

          return!
            (setMimeType mimeType >=> Suave.Response.response HTTP_200 bytes)
              ctx

      | None -> return! NOT_FOUND "File not found in virtual file system" ctx
    }

  let resolveFile(suaveCtx: SuaveContext) : WebPart =
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
  open System.Threading
  open FSharp.Control
  open FSharp.Control.Reactive
  open System.Collections.Generic

  type SseBodyArgs = {
    token: CancellationToken
    fileChangedEvents: IAsyncEnumerable<FileChangedEvent>
    compileErrorEvents: IAsyncEnumerable<string option>
  }

  let sseBody
    ({
       token = token
       fileChangedEvents = fileChangedEvents
     }: SseBodyArgs)
    (out: Sockets.Connection)
    : Async<unit> =
    asyncEx {

      for event in fileChangedEvents do
        let data =
          sprintf
            """{"type": "file-changed", "path": "%s"}"""
            (UMX.untag event.path)

        let msg =
          Message.createType
            (string(DateTimeOffset.Now.ToUnixTimeSeconds()))
            data
            "file-changed"

        // Fire and forget for subscription events
        do! EventSource.send out msg :> Task
    }

  let sseHandler
    (fileChangedEvents: IObservable<FileChangedEvent>)
    (compileErrorEvents: IObservable<string option>)
    : WebPart =

    fun ctx -> async {
      let! token = Async.CancellationToken
      let fileChangedEvents = fileChangedEvents |> Observable.toAsyncEnumerable

      let compileErrorEvents =
        compileErrorEvents |> Observable.toAsyncEnumerable

      return!
        handShake
          (sseBody {
            token = token
            fileChangedEvents = fileChangedEvents
            compileErrorEvents = compileErrorEvents
           }
           >> Sockets.SocketOp.ofAsync)
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
        // Rewrite to index file
        let indexFile = UMX.untag config.index

        let newPath =
          if indexFile.StartsWith('.') then
            indexFile.[1..]
          else
            indexFile

        // Redirect to index
        return! (Redirection.FOUND newPath) ctx
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

  let indexHandler(config: PerlaConfig, fsManager: PerlaFsManager) =
    path "/"
    >=> setMimeType "text/html"
    >=> fun ctx -> async {
      let content = fsManager.ResolveIndex |> AVal.force
      let map = fsManager.ResolveImportMap |> AVal.force

      use context = BrowsingContext.New(Configuration.Default)
      let parser = context.GetService<IHtmlParser>() |> nonNull
      use doc = parser.ParseDocument(content)
      let body = Build.EnsureBody doc
      let head = Build.EnsureHead doc

      let script = doc.CreateElement "script"
      script.SetAttribute("type", "importmap")
      script.TextContent <- Json.ToText map
      head.AppendChild script |> ignore

      // remove standalone entry points, we don't need them in the browser
      doc.QuerySelectorAll("[data-entry-point=standalone][type=module]")
      |> Seq.iter(fun f -> f.Remove())

      if config.devServer.liveReload then
        let liveReload = doc.CreateElement "script"
        liveReload.SetAttribute("type", "application/javascript")
        liveReload.SetAttribute("src", "/~perla~/livereload.js")
        body.AppendChild liveReload |> ignore

      return! OK (doc.ToHtml()) ctx
    }

// ============================================================================
// Main Server Configuration
// ============================================================================

module SuaveServer =
  open System.Threading

  let toLoggary(logger: ILogger) =
    { new Suave.Logging.Logger with
        member _.log
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

          // Extract the message text from Suave's Message type
          let messageText =
            match message.value with
            | Logging.Event template -> template
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

            let messageText =
              match message.value with
              | Logging.Event template -> template
              | Logging.Gauge(value, units) -> $"Gauge: {value} {units}"

            logger.Log(logLevel, messageText)
            return ()
          }

        member _.name = [| "PerlaLogger" |]
    }

  let createApp(suaveCtx: SuaveContext) =
    let config = AVal.force suaveCtx.Config
    let proxyWebparts = ProxyService.createProxyWebparts config.devServer.proxy

    choose [
      // Perla internal endpoints
      path "/~perla~/sse"
      >=> LiveReload.sseHandler
        suaveCtx.FileChangedEvents
        suaveCtx.CompileErrorEvents

      PerlaHandlers.liveReloadScript suaveCtx.FsManager
      PerlaHandlers.workerScript suaveCtx.FsManager
      PerlaHandlers.testingHelpers suaveCtx.FsManager
      PerlaHandlers.indexHandler(config, suaveCtx.FsManager)

      // Proxy endpoints (if configured)
      proxyWebparts

      // Virtual file system (before SPA fallback)
      VirtualFiles.resolveFile suaveCtx

      // SPA fallback
      SpaFallback.spaFallback config

      // Final fallback
      NOT_FOUND "Resource not found"
    ]

  let startServer
    (suaveCtx: SuaveContext)
    (cancellationToken: CancellationToken)
    =
    let config = AVal.force suaveCtx.Config
    let host = config.devServer.host
    let port = config.devServer.port

    let serverConfig =
      defaultConfig
        .withBindings([ HttpBinding.createSimple HTTP host port ])
        .withCancellationToken(cancellationToken)
        .withLogger(suaveCtx.Logger |> toLoggary)


    let app = createApp suaveCtx
    suaveCtx.Logger.LogInformation($"Starting Suave server on {host}:{port}")
    startWebServer serverConfig app
