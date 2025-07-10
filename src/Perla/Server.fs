namespace Perla.Server

open System
open System.IO
open System.Net
open System.Net.NetworkInformation
open System.Reactive.Subjects
open System.Runtime.InteropServices
open System.Text
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Collections.Generic

open AngleSharp
open AngleSharp.Html.Parser
open AngleSharp.Io
open AngleSharp.Dom

open Fake.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives
open Microsoft.AspNetCore.StaticFiles
open Microsoft.Net.Http.Headers
open Microsoft.Extensions.Logging

open Yarp.ReverseProxy
open Yarp.ReverseProxy.Configuration
open Yarp.ReverseProxy.Forwarder

open FSharp.Control
open FSharp.Control.Reactive

open FsToolkit.ErrorHandling

open Perla
open Perla.Types
open Perla.Units
open Perla.Json
open Perla.Logger
open Perla.Plugins
open Perla.FileSystem
open Perla.VirtualFs
open Perla.Build
open FSharp.UMX
open FSharp.Data.Adaptive
open IcedTasks

open Spectre.Console


module Types =

  [<RequireQualifiedAccess; Struct>]
  type ReloadKind =
    | FullReload
    | HMR

  [<RequireQualifiedAccess; Struct>]
  type PerlaScript =
    | LiveReload
    | Worker
    | Env
    | TestingHelpers
    | MochaTestRunner

  [<RequireQualifiedAccess>]
  type ReloadEvents =
    | FullReload of string
    | ReplaceCSS of string
    | CompileError of string

    member this.AsString =
      match this with
      | FullReload data -> $"event:reload\ndata:{data}\n\n"
      | ReplaceCSS data -> $"event:replace-css\ndata:{data}\n\n"
      | CompileError err -> $"event:compile-err\ndata:{err}\n\n"

open Types

// ============================================================================
// Extensions
// ============================================================================

[<AutoOpen>]
module Extensions =

  [<Extension>]
  type HttpContextExtensions() =

    [<Extension>]
    static member GetService<'T>(ctx: HttpContext) =
      let t = typeof<'T>

      match ctx.RequestServices.GetService t with
      | null -> raise(exn t.Name)
      | service -> service :?> 'T

    [<Extension>]
    static member GetLogger<'T>(ctx: HttpContext) =
      ctx.GetService<ILogger<'T>>()

    [<Extension>]
    static member GetLogger(ctx: HttpContext, categoryName: string) =
      let loggerFactory = ctx.GetService<ILoggerFactory>()
      loggerFactory.CreateLogger categoryName

    [<Extension>]
    static member SetStatusCode(ctx: HttpContext, httpStatusCode: int) =
      ctx.Response.StatusCode <- httpStatusCode

    [<Extension>]
    static member SetHttpHeader(ctx: HttpContext, key: string, value: obj) =
      ctx.Response.Headers[key] <- StringValues(value.ToString())

    [<Extension>]
    static member SetContentType(ctx: HttpContext, contentType: string) =
      ctx.SetHttpHeader(HeaderNames.ContentType, contentType)

    [<Extension>]
    static member WriteBytesAsync(ctx: HttpContext, bytes: byte[]) = task {
      ctx.SetHttpHeader(HeaderNames.ContentLength, bytes.Length)

      if ctx.Request.Method <> HttpMethods.Head then
        do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)

      return Some ctx
    }

    [<Extension>]
    static member WriteStringAsync(ctx: HttpContext, str: string) =
      ctx.WriteBytesAsync(Encoding.UTF8.GetBytes str)

    [<Extension>]
    static member WriteTextAsync(ctx: HttpContext, str: string) =
      ctx.SetContentType "text/plain; charset=utf-8"
      ctx.WriteStringAsync str

// ============================================================================
// LiveReload Module
// ============================================================================

module LiveReload =

  let WriteReloadChange
    (logger: ILogger)
    (event: FileChangedEvent, response: HttpResponse)
    =
    let data =
      Json.ToText(
        {|
          oldName = event.oldName
          name = event.name
        |},
        false
      )

    logger.LogInformation(
      "LiveReload: File Changed: {FileName}",
      UMX.untag event.name
    )

    response.WriteAsync $"event:reload\ndata:{data}\n\n"

  let WriteHmrChange
    (logger: ILogger)
    (event: FileChangedEvent, transform: FileTransform, response: HttpResponse)
    =
    let oldPath =
      event.oldPath
      |> Option.map(fun oldPath ->
        $"{oldPath}".Replace(Path.DirectorySeparatorChar, '/'))

    let replaced =
      if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
        (UMX.untag event.name).Replace(Path.DirectorySeparatorChar, '/')
      else
        (UMX.untag event.name)

    let userPath = $"{event.userPath}/{replaced}"

    let data =
      Json.ToText {|
        oldName = event.oldName
        oldPath = oldPath
        name = replaced
        url = $"{event.serverPath}/{replaced}"
        localPath = userPath
        content = transform.content
      |}

    logger.LogInformation("HMR: CSS File Changed: {UserPath}", userPath)
    response.WriteAsync $"event:replace-css\ndata:{data}\n\n"

  let WriteCompileError
    (logger: ILogger)
    (error: string option, response: HttpResponse)
    =
    let err = Json.ToText({| error = error |}, true)
    logger.LogWarning("Compilation Error: {Error}", err.Substring(0, 80))
    response.WriteAsync(ReloadEvents.CompileError(err).AsString)

// ============================================================================
// Middleware Module
// ============================================================================

[<RequireQualifiedAccess>]
module Middleware =

  [<Struct>]
  type RequestedAs =
    | JS
    | Normal

  let processFile
    (logger: ILogger)
    (
      setContentAndWrite: string * byte array -> Task<_>,
      reqPath: string,
      mimeType: string,
      requestedAs: RequestedAs,
      content: byte array
    ) : Task =
    let processCssAsJs(content, url: string) =
      $"""const style=document.createElement('style');style.setAttribute("url", "{url}");
document.head.appendChild(style).innerHTML=String.raw`{content}`;"""

    let processJsonAsJs content = $"""export default {content};"""

    match mimeType, requestedAs with
    | "application/json", JS ->
      setContentAndWrite(
        MimeTypeNames.DefaultJavaScript,
        processJsonAsJs(Encoding.UTF8.GetString content)
        |> Encoding.UTF8.GetBytes
      )
    | "text/css", JS ->
      setContentAndWrite(
        MimeTypeNames.DefaultJavaScript,
        processCssAsJs(Encoding.UTF8.GetString content, reqPath)
        |> Encoding.UTF8.GetBytes
      )
    | mimeType, Normal -> setContentAndWrite(mimeType, content)
    | mimeType, JS ->
      logger.LogWarning(
        "Requested {RequestedAs} - {MimeType} - {RequestPath} as JS, this file type is not supported as JS, sending default content",
        JS,
        mimeType,
        reqPath
      )

      setContentAndWrite(mimeType, content)

  let ResolveFile(logger: ILogger) : HttpContext -> RequestDelegate -> Task =
    fun ctx next -> taskUnit {
      let vfs = ctx.GetService<VirtualFileSystem>()
      let processFile = processFile logger

      match vfs.Resolve(UMX.tag<ServerUrl> ctx.Request.Path) with
      | Some file ->
        let fileExtProvider = ctx.GetService<FileExtensionContentTypeProvider>()

        match fileExtProvider.TryGetContentType ctx.Request.Path with
        | true, mime ->
          let setContentTypeAndWrite(mimeType, content) =
            ctx.SetContentType mimeType
            ctx.WriteBytesAsync content

          let requestedAs =
            let query = ctx.Request.Query
            if query.ContainsKey("js") then JS else Normal

          match file with
          | TextFile content ->
            return!
              processFile(
                setContentTypeAndWrite,
                ctx.Request.Path.ToString(),
                mime,
                requestedAs,
                Encoding.UTF8.GetBytes(content.content)
              )
          | BinaryFile info ->
            let fileBytes = File.ReadAllBytes(UMX.untag info.source)

            return!
              processFile(
                setContentTypeAndWrite,
                ctx.Request.Path.ToString(),
                mime,
                requestedAs,
                fileBytes
              )
        | false, _ -> return! next.Invoke(ctx)
      | None -> return! next.Invoke(ctx)
    }

  let ProcessTestEvent
    (logger: ILogger)
    (testEvents: ISubject<TestEvent>)
    (ctx: HttpContext)
    =
    taskUnit {
      use content = new StreamReader(ctx.Request.Body, Encoding.UTF8)
      let! toDecode = content.ReadToEndAsync()

      Json.TestEventFromJson toDecode
      |> Result.teeError(fun err ->
        logger.LogError("Failed to parse test event: {Error}", err))
      |> Result.iter testEvents.OnNext

      return Results.Ok()
    }

  let SendScript (logger: ILogger) (script: PerlaScript) (ctx: HttpContext) = cancellableTask {
    let fsManager = ctx.GetService<PerlaFsManager>()

    logger.LogInformation("Sending Script {Script}", script)

    match script with
    | PerlaScript.LiveReload ->
      let! content = fsManager.ResolveLiveReloadScript()

      return Results.Text(content, "text/javascript")

    | PerlaScript.Worker ->
      let! content = fsManager.ResolveWorkerScript()

      return Results.Text(content, "text/javascript")

    | PerlaScript.TestingHelpers ->
      let! content = fsManager.ResolveTestingHelpersScript()

      return Results.Text(content, "text/javascript")

    | PerlaScript.MochaTestRunner ->
      let! content = fsManager.ResolveMochaRunnerScript()

      return Results.Text(content, "text/javascript")

    | PerlaScript.Env ->
      let fsManager = ctx.GetService<PerlaFsManager>()
      let envVars = fsManager.DotEnvContents |> AVal.force

      if Map.isEmpty envVars then
        logger.LogWarning(
          "An env file was requested but no env variables were found"
        )

        let message =
          """If you want to use env variables, remember to prefix them with 'PERLA_' e.g.
'PERLA_myApiKey' or 'PERLA_CLIENT_SECRET', then you will be able to import them via the env file"""

        logger.LogWarning("Env Content not found. {Message}", message)

        return Results.NotFound({| message = message |})
      else
        let content =
          envVars
          |> Map.fold
            (fun (sb: StringBuilder) key value ->
              sb.AppendLine $"export const {key} = \"{value}\"")
            (StringBuilder())
          |> _.ToString()

        return Results.Text(content, "text/javascript", Encoding.UTF8, 200)
  }

  let SseHandler
    (vfs: VirtualFileSystem)
    (fileChangedEvents: IObservable<FileChangedEvent>)
    (compileErrorEvents: IObservable<string option>)
    (ctx: HttpContext)
    =
    task {
      let logger = ctx.GetLogger("Perla:SSE")
      logger.LogInformation $"LiveReload Client Connected"
      ctx.SetHttpHeader("Content-Type", "text/event-stream")
      ctx.SetHttpHeader("Cache-Control", "no-cache")
      ctx.SetHttpHeader("Connection", "keep-alive")
      ctx.SetHttpHeader("Access-Control-Allow-Origin", "*")
      ctx.SetHttpHeader("Access-Control-Allow-Headers", "Cache-Control")
      ctx.SetStatusCode 200
      let res = ctx.Response
      let writeHmrChange = LiveReload.WriteHmrChange logger
      let writeReloadChange = LiveReload.WriteReloadChange logger
      let writeCompileError = LiveReload.WriteCompileError logger
      // Start Client communication
      do! res.WriteAsync $"id:{ctx.Connection.Id}\ndata:{DateTime.Now}\n\n"
      do! res.Body.FlushAsync()

      let onChangeSub =
        fileChangedEvents
        |> Observable.map(fun (event: FileChangedEvent) -> task {
          match event.changeType with
          | Changed ->
            match vfs.Resolve event.serverPath with
            | Some(FileKind.BinaryFile _) -> do! writeReloadChange(event, res)
            | Some(FileKind.TextFile file) ->
              if file.mimetype = MimeTypeNames.Css then
                do!
                  writeHmrChange(
                    event,
                    {
                      content = file.content
                      extension = ".css"
                    },
                    res
                  )
              else
                do! writeReloadChange(event, res)
            | None ->
              logger.LogWarning(
                "File Changed Event: {FileName} not found in VFS",
                UMX.untag event.name
              )

              do! writeReloadChange(event, res)
          | _ -> do! writeReloadChange(event, res)

          do! res.Body.FlushAsync()
        })
        |> Observable.switchTask
        |> Observable.subscribe(fun _ ->
          logger.LogInformation "File Changed Event processed")

      let onCompilerErrorSub =
        compileErrorEvents
        |> Observable.map(fun error -> task {
          do! writeCompileError(error, res)
          do! res.Body.FlushAsync()
        })
        |> Observable.switchTask
        |> Observable.subscribe(fun _ ->
          logger.LogWarning "Compile Error Event processed")

      ctx.RequestAborted.Register(fun _ ->
        onChangeSub.Dispose()
        onCompilerErrorSub.Dispose())
      |> ignore

      // Keep connection alive
      while not ctx.RequestAborted.IsCancellationRequested do
        do! Task.Delay(TimeSpan.FromSeconds(30.))
        do! res.WriteAsync(": keepalive\n\n")
        do! res.Body.FlushAsync()

      return Results.Ok()
    }

  let IndexHandler (config: PerlaConfig) (ctx: HttpContext) =
    let fsManager = ctx.GetService<PerlaFsManager>()
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
      liveReload.SetAttribute("type", MimeTypeNames.DefaultJavaScript)
      liveReload.SetAttribute("src", "/~perla~/livereload.js")
      body.AppendChild liveReload |> ignore

    Results.Text(doc.ToHtml(), MimeTypeNames.Html)

  let TestingIndex
    (config: PerlaConfig aval)
    (map: PkgManager.ImportMap aval)
    (ctx: HttpContext)
    =
    cancellableTask {

      let fsManager = ctx.GetService<PerlaFsManager>()
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
      mochaStyles.SetAttribute("type", MimeTypeNames.Css)
      head.AppendChild mochaStyles |> ignore

      let script: Dom.IElement = doc.CreateElement "script"
      script.SetAttribute("type", "importmap")
      script.TextContent <- Json.ToText(AVal.force map)
      head.AppendChild script |> ignore

      let mochaScript = doc.CreateElement "script"
      mochaScript.SetAttribute("type", MimeTypeNames.DefaultJavaScript)
      mochaScript.SetAttribute("src", "https://unpkg.com/mocha/mocha.js")
      body.AppendChild mochaScript |> ignore

      let mochaDiv = doc.CreateElement "div"
      mochaDiv.SetAttribute("id", "mocha")
      body.AppendChild mochaDiv |> ignore

      let runnerScript = doc.CreateElement "script"
      runnerScript.SetAttribute("type", "module")

      let! runnerContent = fsManager.ResolveMochaRunnerScript()

      runnerScript.TextContent <- runnerContent
      body.AppendChild runnerScript |> ignore

      if (AVal.force config).devServer.liveReload then
        let liveReload = doc.CreateElement "script"
        liveReload.SetAttribute("type", MimeTypeNames.DefaultJavaScript)
        liveReload.SetAttribute("src", "/~perla~/livereload.js")
        body.AppendChild liveReload |> ignore

      return Results.Text(doc.ToHtml(), MimeTypeNames.Html)
    }

// ============================================================================
// Custom SPA Middleware
// ============================================================================

module SpaMiddleware =
  let spaFallback
    (config: PerlaConfig)
    (next: RequestDelegate)
    (context: HttpContext)
    =
    task {
      let path = context.Request.Path.Value |> nonNull

      // Skip if it's an API call or static file
      if
        path.StartsWith("/api/")
        || path.StartsWith("/~perla~/")
        || Path.HasExtension(path)
      then
        return! next.Invoke(context)
      else
        // Rewrite to default page for SPA
        context.Request.Path <- PathString(UMX.untag config.index)

        return! next.Invoke(context)
    }


// ============================================================================
// YARP Proxy Configuration
// ============================================================================

module ProxyConfiguration =

  let createRoutesAndClusters(proxyConfig: Map<string, string>) =
    let routes = List<RouteConfig>()
    let clusters = List<ClusterConfig>()

    for KeyValue(from, target) in proxyConfig do
      let routeId = $"route_{Guid.NewGuid()}"
      let clusterId = $"cluster_{Guid.NewGuid()}"

      // Create cluster
      let cluster =
        ClusterConfig(
          ClusterId = clusterId,
          Destinations =
            Dictionary<string, DestinationConfig> [
              KeyValuePair("destination1", DestinationConfig(Address = target))
            ]
        )

      clusters.Add(cluster)

      // Create route
      let route =
        RouteConfig(
          RouteId = routeId,
          ClusterId = clusterId,
          Match = RouteMatch(Path = from)
        )

      routes.Add(route)

    routes, clusters

// ============================================================================
// Server Module
// ============================================================================

module Server =

  let isAddressPortOccupied (address: string) (port: int) =
    let didParse, address = IPEndPoint.TryParse($"{address}:{port}")

    if didParse then
      let props = IPGlobalProperties.GetIPGlobalProperties()
      let listeners = props.GetActiveTcpListeners()

      listeners
      |> Array.map(fun listener -> listener.Port)
      |> Array.contains (nonNull address).Port
    else
      false

  let GetServerURLs host port useSSL =
    match isAddressPortOccupied host port with
    | false ->
      if useSSL then
        $"http://{host}:{port - 1}", $"https://{host}:{port}"
      else
        $"http://{host}:{port}", $"https://{host}:{port + 1}"
    | true ->
      Logger.log(
        $"Address {host}:{port} is busy, selecting a dynamic port.",
        target = PrefixKind.Serve
      )

      $"http://{host}:{0}", $"https://{host}:{0}"

  let addServices
    (config: PerlaConfig aval)
    (vfs: VirtualFileSystem)
    (fsManager: PerlaFsManager)
    (builder: WebApplicationBuilder)
    =
    // Core services
    builder.Services.AddSingleton<PerlaFsManager>(fsManager) |> ignore
    builder.Services.AddSingleton<VirtualFileSystem>(vfs) |> ignore

    // File extension provider
    builder.Services.AddSingleton<FileExtensionContentTypeProvider>(fun _ ->
      FileExtensionContentTypeProvider())
    |> ignore

    // YARP Reverse Proxy
    if not(Map.isEmpty (AVal.force config).devServer.proxy) then
      let routes, clusters =
        ProxyConfiguration.createRoutesAndClusters
          (AVal.force config).devServer.proxy

      builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters)
      |> ignore

  // Logging is provided externally via ILogger

  let addCommonMiddleware host port useSSL (app: WebApplication) =
    let http, https = GetServerURLs host port useSSL
    app.Urls.Add(http)
    app.Urls.Add(https)

    // Custom SPA middleware
    let config =
      app.Services.GetRequiredService<PerlaFsManager>().PerlaConfiguration
      |> AVal.force

    app.Use(fun next ->
      RequestDelegate(fun ctx -> SpaMiddleware.spaFallback config next ctx))
    |> ignore

    if useSSL then
      app.UseHsts().UseHttpsRedirection() |> ignore

  let addVirtualFileSystemMiddleware (logger: ILogger) (app: WebApplication) =
    app.UseWhen(
      Func<HttpContext, bool>(fun ctx ->
        not(ctx.Request.Path.StartsWithSegments(PathString("/~perla~")))),
      (fun app ->
        app.Use(
          Func<HttpContext, RequestDelegate, Task>(
            Middleware.ResolveFile logger
          )
        )
        |> ignore)
    )
    |> ignore

    app

  let addLiveReload
    (logger: ILogger)
    (vfs: VirtualFileSystem)
    (fileChangedEvents: IObservable<FileChangedEvent>)
    (compileErrorEvents: IObservable<string option>)
    (app: WebApplication)
    =
    app.MapGet(
      "/~perla~/sse",
      Func<HttpContext, Task<IResult>>(fun ctx ->
        Middleware.SseHandler vfs fileChangedEvents compileErrorEvents ctx)
    )
    |> ignore

    app.MapGet(
      "/~perla~/livereload.js",
      Func<HttpContext, Task<IResult>>(fun ctx ->
        Middleware.SendScript
          logger
          PerlaScript.LiveReload
          ctx
          ctx.RequestAborted)
    )
    |> ignore

    app.MapGet(
      "/~perla~/worker.js",
      Func<HttpContext, Task<IResult>>(fun ctx ->
        Middleware.SendScript logger PerlaScript.Worker ctx ctx.RequestAborted)
    )
    |> ignore

    app

  let addEnv
    (logger: ILogger)
    (config: PerlaConfig aval)
    (app: WebApplication)
    =
    if (AVal.force config).enableEnv then
      app.MapGet(
        UMX.untag (AVal.force config).envPath,
        Func<HttpContext, Task<IResult>>(fun ctx ->
          Middleware.SendScript logger PerlaScript.Env ctx ctx.RequestAborted)
      )
      |> ignore

    app

  module DevApp =
    let addIndexHandler(app: WebApplication) =
      app.MapGet(
        "/",
        Func<HttpContext, IResult>(fun ctx ->
          let config =
            ctx.GetService<PerlaFsManager>().PerlaConfiguration |> AVal.force

          Middleware.IndexHandler config ctx)
      )
      |> ignore

      app.MapGet(
        "/index.html",
        Func<HttpContext, IResult>(fun ctx ->
          let config =
            ctx.GetService<PerlaFsManager>().PerlaConfiguration |> AVal.force

          Middleware.IndexHandler config ctx)
      )
      |> ignore

      app

  module TestApp =
    let addIndexHandler
      (dependencies: PkgManager.ImportMap aval)
      (app: WebApplication)
      =
      app.MapGet(
        "/",
        Func<HttpContext, Task<IResult>>(fun ctx ->
          let config = ctx.GetService<PerlaFsManager>().PerlaConfiguration

          Middleware.TestingIndex config dependencies ctx ctx.RequestAborted)
      )
      |> ignore

      app.MapGet(
        "/index.html",
        Func<HttpContext, Task<IResult>>(fun ctx ->
          let config = ctx.GetService<PerlaFsManager>().PerlaConfiguration

          Middleware.TestingIndex config dependencies ctx ctx.RequestAborted)
      )
      |> ignore

      app

    let addTestingHandlers
      (files: string seq option)
      (testConfig: TestConfig)
      (mochaConfig: Map<string, obj> option)
      testingEvents
      (app: WebApplication)
      =

      app.MapGet(
        "/~perla~/testing/helpers.js",
        Func<HttpContext, Task<IResult>>(fun ctx ->
          let logger = ctx.GetLogger("Perla:TestingHelpers")

          Middleware.SendScript
            logger
            PerlaScript.TestingHelpers
            ctx
            ctx.RequestAborted)
      )
      |> ignore

      app.MapPost(
        "/~perla~/testing/events",
        Func<HttpContext, Task>(fun ctx ->
          let logger = ctx.GetLogger("Perla:TestingEvents")
          Middleware.ProcessTestEvent logger testingEvents ctx)
      )
      |> ignore

      app.MapGet(
        "/~perla~/testing/files",
        Func<HttpContext, IResult>(fun _ ->
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
              match files with
              | Some files ->
                if files |> Seq.isEmpty then
                  [ "**/*.test.js"; "**/*.spec.js" ]
                else
                  files |> Seq.toList
              | None -> [ "**/*.test.js"; "**/*.spec.js" ]
          }

          Results.Ok(
            [|
              for file in glob do
                let systemPath =
                  (Path.GetFullPath file)
                    .Replace(Path.DirectorySeparatorChar, '/')

                let index = systemPath.IndexOf("/tests/")
                systemPath.Substring(index)
            |]
          ))
      )
      |> ignore

      app.MapGet(
        "/~perla~/testing/mocha-settings",
        Func<HttpContext, IResult>(fun _ ->
          Results.Ok(mochaConfig |> Option.defaultValue Map.empty))
      )
      |> ignore

      app.MapGet(
        "/~perla~/testing/environment",
        Func<HttpContext, IResult>(fun _ ->
          Results.Ok {|
            testConfig with
                browsers =
                  (testConfig.browsers |> Seq.map ConfigEncoders.Browser)
                    .ToString()
                browserMode =
                  (testConfig.browserMode |> ConfigEncoders.BrowserMode)
                    .ToString()
                runId = Guid.NewGuid()
          |})
      )
      |> ignore

      app

// ============================================================================
// Main Server Class
// ============================================================================

type Server =
  static member GetServerApp
    (
      config: PerlaConfig aval,
      vfs: VirtualFileSystem,
      fileChangedEvents: IObservable<FileChangedEvent>,
      compileErrorEvents: IObservable<string option>,
      fsManager: PerlaFsManager
    ) =

    let builder = WebApplication.CreateBuilder()
    builder.Logging.AddPerlaLogger() |> ignore

    Server.addServices config vfs fsManager builder

    let app = builder.Build()

    Server.addCommonMiddleware
      (AVal.force config).devServer.host
      (AVal.force config).devServer.port
      (AVal.force config).devServer.useSSL
      app

    Server.DevApp.addIndexHandler app
    |> Server.addLiveReload app.Logger vfs fileChangedEvents compileErrorEvents
    |> Server.addEnv app.Logger config
    |> Server.addVirtualFileSystemMiddleware app.Logger

  static member GetTestingApp
    (
      config: PerlaConfig aval,
      vfs: VirtualFileSystem,
      dependencies: PkgManager.ImportMap aval,
      testEvents: ISubject<TestEvent>,
      fileChangedEvents: IObservable<FileChangedEvent>,
      compileErrorEvents: IObservable<string option>,
      fsManager: PerlaFsManager,
      [<Optional>] ?fileGlobs: string seq,
      [<Optional>] ?mochaOptions: Map<string, obj>
    ) =

    let builder = WebApplication.CreateBuilder()

    Server.addServices config vfs fsManager builder

    let app = builder.Build()

    Server.addCommonMiddleware
      (AVal.force config).devServer.host
      (AVal.force config).devServer.port
      (AVal.force config).devServer.useSSL
      app

    Server.TestApp.addIndexHandler dependencies app
    |> Server.addLiveReload app.Logger vfs fileChangedEvents compileErrorEvents
    |> Server.addEnv app.Logger config
    |> Server.TestApp.addTestingHandlers
      fileGlobs
      (AVal.force config).testing
      mochaOptions
      testEvents
    |> Server.addVirtualFileSystemMiddleware app.Logger

  static member GetStaticServer(config: PerlaConfig aval) =
    let webroot =
      Path.Combine(".", $"{(AVal.force config).build.outDir}")
      |> Path.GetFullPath

    let builder =
      WebApplication.CreateBuilder(WebApplicationOptions(WebRootPath = webroot))

    builder.Logging.AddPerlaLogger() |> ignore

    // YARP Reverse Proxy if needed
    if not(Map.isEmpty (AVal.force config).devServer.proxy) then
      let routes, clusters =
        ProxyConfiguration.createRoutesAndClusters
          (AVal.force config).devServer.proxy

      builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters)
      |> ignore

    let app = builder.Build()

    Server.addCommonMiddleware
      (AVal.force config).devServer.host
      (AVal.force config).devServer.port
      (AVal.force config).devServer.useSSL
      app

    app.UseDefaultFiles().UseStaticFiles() |> ignore

    app
