module Perla.Json

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Nodes

open Perla.PackageManager.Types
open Perla.Types

open Perla.Units
open Thoth.Json.Net
open FsToolkit.ErrorHandling
open FSharp.UMX

[<RequireQualifiedAccess; Struct>]
type PerlaConfigSection =
  | Index of index: string option
  | Fable of fable: FableConfig option
  | DevServer of devServer: DevServerConfig option
  | Build of build: BuildConfig option
  | Dependencies of dependencies: PkgDependency Set option

let DefaultJsonOptions() =
  JsonSerializerOptions(
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  )

let DefaultJsonNodeOptions() =
  JsonNodeOptions(PropertyNameCaseInsensitive = true)

let DefaultJsonDocumentOptions() =
  JsonDocumentOptions(
    AllowTrailingCommas = true,
    CommentHandling = JsonCommentHandling.Skip
  )

module TemplateDecoders =
  type DecodedTemplateConfigItem = {
    id: string
    name: string
    path: string<SystemPath>
    shortName: string
    description: string option
  }

  type DecodedTemplateConfiguration = {
    name: string
    group: string
    templates: DecodedTemplateConfigItem seq
    author: string option
    license: string option
    description: string option
    repositoryUrl: string option
  }

  let TemplateConfigItemDecoder: Decoder<DecodedTemplateConfigItem> =
    Decode.object(fun get -> {
      id = get.Required.Field "id" Decode.string
      name = get.Required.Field "name" Decode.string
      path = get.Required.Field "path" Decode.string |> UMX.tag<SystemPath>
      shortName = get.Required.Field "shortname" Decode.string
      description = get.Optional.Field "description" Decode.string
    })

  let TemplateConfigurationDecoder: Decoder<DecodedTemplateConfiguration> =
    Decode.object(fun get -> {
      name = get.Required.Field "name" Decode.string
      group = get.Required.Field "group" Decode.string
      templates =
        get.Required.Field "templates" (Decode.array TemplateConfigItemDecoder)
      author = get.Optional.Field "author" Decode.string
      license = get.Optional.Field "license" Decode.string
      description = get.Optional.Field "description" Decode.string
      repositoryUrl = get.Optional.Field "repositoryUrl" Decode.string
    })


module ConfigDecoders =

  type DecodedFableConfig = {
    project: string<SystemPath> option
    extension: string<FileExtension> option
    sourceMaps: bool option
    outDir: string<SystemPath> option
  }

  type DecodedDevServer = {
    port: int option
    host: string option
    liveReload: bool option
    useSSL: bool option
    proxy: Map<string, string> option
  }

  type DecodedEsbuild = {
    esBuildPath: string<SystemPath> option
    version: string<Semver> option
    ecmaVersion: string option
    minify: bool option
    injects: string seq option
    externals: string seq option
    fileLoaders: Map<string, string> option
    jsxAutomatic: bool option
    jsxImportSource: string option
  }

  type DecodedBuild = {
    includes: string seq option
    excludes: string seq option
    outDir: string<SystemPath> option
    emitEnvFile: bool option
  }

  type DecodedTesting = {
    browsers: Browser seq option
    includes: string seq option
    excludes: string seq option
    watch: bool option
    headless: bool option
    browserMode: BrowserMode option
    fable: DecodedFableConfig option
  }

  type DecodedPerlaConfig = {
    index: string<SystemPath> option
    provider: PkgManager.DownloadProvider option
    plugins: string list option
    build: DecodedBuild option
    devServer: DecodedDevServer option
    fable: DecodedFableConfig option
    esbuild: DecodedEsbuild option
    testing: DecodedTesting option
    mountDirectories: Map<string<ServerUrl>, string<UserPath>> option
    enableEnv: bool option
    envPath: string<ServerUrl> option
    paths: Map<string<BareImport>, string<ResolutionUrl>> option
    dependencies: PkgDependency Set option
  }

  let FableFileDecoder: Decoder<DecodedFableConfig> =
    Decode.object(fun get -> {
      project =
        get.Optional.Field "project" Decode.string
        |> Option.map UMX.tag<SystemPath>
      extension =
        get.Optional.Field "extension" Decode.string
        |> Option.map UMX.tag<FileExtension>
      sourceMaps = get.Optional.Field "sourceMaps" Decode.bool
      outDir =
        get.Optional.Field "outDir" Decode.string
        |> Option.map UMX.tag<SystemPath>
    })

  let DevServerDecoder: Decoder<DecodedDevServer> =
    Decode.object(fun get -> {
      port = get.Optional.Field "port" Decode.int
      host = get.Optional.Field "host" Decode.string
      liveReload = get.Optional.Field "liveReload" Decode.bool
      useSSL = get.Optional.Field "useSSL" Decode.bool
      proxy = get.Optional.Field "proxy" (Decode.dict Decode.string)
    })

  let EsbuildDecoder: Decoder<DecodedEsbuild> =
    Decode.object(fun get -> {
      fileLoaders =
        get.Optional.Field "fileLoaders" (Decode.dict Decode.string)
      esBuildPath =
        get.Optional.Field "esBuildPath" Decode.string
        |> Option.map UMX.tag<SystemPath>
      version =
        get.Optional.Field "version" Decode.string
        |> Option.map UMX.tag<Semver>
      ecmaVersion = get.Optional.Field "ecmaVersion" Decode.string
      minify = get.Optional.Field "minify" Decode.bool
      injects =
        get.Optional.Field "injects" (Decode.list Decode.string)
        |> Option.map List.toSeq
      externals =
        get.Optional.Field "externals" (Decode.list Decode.string)
        |> Option.map List.toSeq
      jsxAutomatic = get.Optional.Field "jsxAutomatic" Decode.bool
      jsxImportSource = get.Optional.Field "jsxImportSource" Decode.string
    })

  let BuildDecoder: Decoder<DecodedBuild> =
    Decode.object(fun get -> {
      includes =
        get.Optional.Field "includes" (Decode.list Decode.string)
        |> Option.map List.toSeq
      excludes =
        get.Optional.Field "excludes" (Decode.list Decode.string)
        |> Option.map List.toSeq
      outDir =
        get.Optional.Field "outDir" Decode.string
        |> Option.map UMX.tag<SystemPath>
      emitEnvFile = get.Optional.Field "emitEnvFile" Decode.bool
    })

  let BrowserDecoder: Decoder<Browser> =
    Decode.string
    |> Decode.andThen(fun value -> Browser.FromString value |> Decode.succeed)

  let BrowserModeDecoder: Decoder<BrowserMode> =
    Decode.string
    |> Decode.andThen(fun value ->
      BrowserMode.FromString value |> Decode.succeed)

  let TestConfigDecoder: Decoder<DecodedTesting> =
    Decode.object(fun get -> {
      browsers =
        get.Optional.Field "browsers" (Decode.list BrowserDecoder)
        |> Option.map List.toSeq
      includes =
        get.Optional.Field "includes" (Decode.list Decode.string)
        |> Option.map List.toSeq
      excludes =
        get.Optional.Field "excludes" (Decode.list Decode.string)
        |> Option.map List.toSeq
      watch = get.Optional.Field "watch" Decode.bool
      headless = get.Optional.Field "headless" Decode.bool
      browserMode = get.Optional.Field "browserMode" BrowserModeDecoder
      fable = get.Optional.Field "fable" FableFileDecoder
    })

  let PerlaDecoder: Decoder<DecodedPerlaConfig> =
    Decode.object(fun get ->
      let providerDecoder =
        Decode.string
        |> Decode.andThen (function
          | "jspm" -> Decode.succeed PkgManager.DownloadProvider.JspmIo
          | "unpkg" -> Decode.succeed PkgManager.DownloadProvider.Unpkg
          | "jsdelivr" -> Decode.succeed PkgManager.DownloadProvider.JsDelivr
          | value -> Decode.fail $"{value} is not a valid run configuration")

      let directoriesDecoder =
        get.Optional.Field "mountDirectories" (Decode.dict Decode.string)
        |> Option.map(fun m ->
          m
          |> Map.toSeq
          |> Seq.map(fun (k, v) -> UMX.tag<ServerUrl> k, UMX.tag<UserPath> v)
          |> Map.ofSeq)

      let pathsDecoder =
        get.Optional.Field "paths" (Decode.dict Decode.string)
        |> Option.map(fun m ->
          m
          |> Map.toSeq
          |> Seq.map(fun (k, v) ->
            UMX.tag<BareImport> k, UMX.tag<ResolutionUrl> v)
          |> Map.ofSeq)

      let dependencyDecoder =
        Decode.dict Decode.string
        |> Decode.map(fun depMap ->
          depMap
          |> Map.toSeq
          |> Seq.map(fun (k, v) -> {
            package = k
            version = UMX.tag<Semver> v
          })
          |> Set.ofSeq)

      {
        index =
          get.Optional.Field "index" Decode.string
          |> Option.map UMX.tag<SystemPath>
        provider = get.Optional.Field "provider" providerDecoder
        plugins = get.Optional.Field "plugins" (Decode.list Decode.string)
        build = get.Optional.Field "build" BuildDecoder
        devServer = get.Optional.Field "devServer" DevServerDecoder
        fable = get.Optional.Field "fable" FableFileDecoder
        esbuild = get.Optional.Field "esbuild" EsbuildDecoder
        testing = get.Optional.Field "testing" TestConfigDecoder
        enableEnv = get.Optional.Field "enableEnv" Decode.bool
        envPath =
          get.Optional.Field "envPath" Decode.string
          |> Option.map UMX.tag<ServerUrl>
        dependencies = get.Optional.Field "dependencies" dependencyDecoder
        mountDirectories = directoriesDecoder
        paths = pathsDecoder
      })

[<RequireQualifiedAccess>]
module internal TestDecoders =

  let TestStats: Decoder<TestStats> =
    Decode.object(fun get -> {
      suites = get.Required.Field "suites" Decode.int
      tests = get.Required.Field "tests" Decode.int
      passes = get.Required.Field "passes" Decode.int
      pending = get.Required.Field "pending" Decode.int
      failures = get.Required.Field "failures" Decode.int
      start = get.Required.Field "start" Decode.datetimeUtc
      ``end`` = get.Optional.Field "end" Decode.datetimeUtc
    })

  let Test: Decoder<Test> =
    Decode.object(fun get -> {
      body = get.Required.Field "body" Decode.string
      duration = get.Optional.Field "duration" Decode.float
      fullTitle = get.Required.Field "fullTitle" Decode.string
      id = get.Required.Field "id" Decode.string
      pending = get.Required.Field "pending" Decode.bool
      speed = get.Optional.Field "speed" Decode.string
      state = get.Optional.Field "state" Decode.string
      title = get.Required.Field "title" Decode.string
      ``type`` = get.Required.Field "type" Decode.string
    })

  let Suite: Decoder<Suite> =
    Decode.object(fun get -> {
      id = get.Required.Field "id" Decode.string
      title = get.Required.Field "title" Decode.string
      fullTitle = get.Required.Field "fullTitle" Decode.string
      root = get.Required.Field "root" Decode.bool
      parent = get.Optional.Field "parent" Decode.string
      pending = get.Required.Field "pending" Decode.bool
      tests = get.Required.Field "tests" (Decode.list Test)
    })

[<RequireQualifiedAccess>]
module internal EventDecoders =

  let SessionStart: Decoder<Guid * TestStats * int> =
    Decode.object(fun get ->
      get.Required.Field "runId" Decode.guid,
      get.Required.Field "stats" TestDecoders.TestStats,
      get.Required.Field "totalTests" Decode.int)

  let SessionEnd: Decoder<Guid * TestStats> =
    Decode.object(fun get ->
      get.Required.Field "runId" Decode.guid,
      get.Required.Field "stats" TestDecoders.TestStats)

  let SuiteEvent: Decoder<Guid * TestStats * Suite> =
    Decode.object(fun get ->
      get.Required.Field "runId" Decode.guid,
      get.Required.Field "stats" TestDecoders.TestStats,
      get.Required.Field "suite" TestDecoders.Suite)

  let TestPass: Decoder<Guid * TestStats * Test> =
    Decode.object(fun get ->
      get.Required.Field "runId" Decode.guid,
      get.Required.Field "stats" TestDecoders.TestStats,
      get.Required.Field "test" TestDecoders.Test)

  let TestFailed: Decoder<Guid * TestStats * Test * string * string> =
    Decode.object(fun get ->
      get.Required.Field "runId" Decode.guid,
      get.Required.Field "stats" TestDecoders.TestStats,
      get.Required.Field "test" TestDecoders.Test,
      get.Required.Field "message" Decode.string,
      get.Required.Field "stack" Decode.string)

  let ImportFailed: Decoder<Guid * string * string> =
    Decode.object(fun get ->
      get.Required.Field "runId" Decode.guid,
      get.Required.Field "message" Decode.string,
      get.Required.Field "stack" Decode.string)

[<RequireQualifiedAccess>]
module internal ConfigEncoders =

  let Browser: Encoder<Browser> = fun value -> Encode.string value.AsString

  let BrowserMode: Encoder<BrowserMode> =
    fun value -> Encode.string value.AsString

  let TestConfig: Encoder<TestConfig> =
    fun value ->
      Encode.object [
        "browsers", value.browsers |> Seq.map Browser |> Encode.seq
        "includes", value.includes |> Seq.map Encode.string |> Encode.seq
        "excludes", value.excludes |> Seq.map Encode.string |> Encode.seq
        "watch", Encode.bool value.watch
        "headless", Encode.bool value.headless
        "browserMode", BrowserMode value.browserMode
      ]

type Json =
  static member ToBytes value =
    JsonSerializer.SerializeToUtf8Bytes(value, DefaultJsonOptions())

  static member FromBytes<'T>(value: byte array) =
    JsonSerializer.Deserialize<'T>(ReadOnlySpan value, DefaultJsonOptions())

  static member ToText(value, ?minify) =
    let opts = DefaultJsonOptions()
    let minify = defaultArg minify false
    opts.WriteIndented <- minify
    JsonSerializer.Serialize(value, opts)

  static member ToNode value =
    JsonSerializer.SerializeToNode(value, DefaultJsonOptions())

  static member FromConfigFile(content: string) =
    Decode.fromString ConfigDecoders.PerlaDecoder content

  static member TestEventFromJson(value: string) =
    // test events
    // { event: string
    //   runId: Guid
    //   stats: TestingStats
    //   suite?: Suite
    //   test?: Test
    //   // message and stack are the error
    //   message?: string
    //   stack?: string }
    result {
      match! Decode.fromString (Decode.field "event" Decode.string) value with
      | "__perla-session-start" ->
        return!
          Decode.fromString EventDecoders.SessionStart value
          |> Result.map SessionStart
      | "__perla-suite-start"
      | "__perla-suite-end" ->
        return!
          Decode.fromString EventDecoders.SuiteEvent value
          |> Result.map SuiteStart
      | "__perla-test-pass" ->
        return!
          Decode.fromString EventDecoders.TestPass value |> Result.map TestPass
      | "__perla-test-failed" ->
        return!
          Decode.fromString EventDecoders.TestFailed value
          |> Result.map TestFailed
      | "__perla-session-end" ->
        return!
          Decode.fromString EventDecoders.SessionEnd value
          |> Result.map SessionEnd
      | "__perla-test-import-failed" ->
        return!
          Decode.fromString EventDecoders.ImportFailed value
          |> Result.map TestImportFailed
      | "__perla-test-run-finished" ->
        let decoder =
          Decode.object(fun get -> get.Required.Field "runId" Decode.guid)

        return! Decode.fromString decoder value |> Result.map TestRunFinished
      | unknown -> return! Error($"'{unknown}' is not a known event")
    }
