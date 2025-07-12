module Perla.Json

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks

open FsToolkit.ErrorHandling
open IcedTasks
open FSharp.UMX

open Perla.Types
open Perla.Units
open JDeck



[<RequireQualifiedAccess; Struct>]
type PerlaConfigSection =
  | Index of index: string option
  | Fable of fable: FableConfig option
  | DevServer of devServer: DevServerConfig option
  | Build of build: BuildConfig option
  | Dependencies of dependencies: PkgDependency Set option

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
  useLocalPkgs: bool option
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

[<AutoOpen>]
module internal Decoders =

  let BrowserDecoder: Decoder<Browser> =
    fun element -> decode {
      let! str = Required.string element
      return Browser.FromString str
    }

  let BrowserModeDecoder: Decoder<BrowserMode> =
    fun element -> decode {
      let! str = Required.string element
      return BrowserMode.FromString str
    }

  let DownloadProviderDecoder: Decoder<PkgManager.DownloadProvider> =
    fun element -> decode {
      let! str = Required.string element
      return PkgManager.DownloadProvider.fromString str
    }

  let PkgDependencyDecoder: Decoder<PkgDependency> =
    fun element -> decode {
      let! package = Required.Property.get ("package", Required.string) element
      let! version = Required.Property.get ("version", Required.string) element

      return {
        package = package
        version = UMX.tag version
      }
    }

  let PkgDependencySetDecoder: Decoder<PkgDependency Set> =
    fun element -> decode {
      // Dependencies come as object: { "package1": "version1", "package2": "version2" }
      let! dependencyMap =
        Optional.Property.map ("dependencies", Required.string) element

      let dependencyMap = defaultArg dependencyMap Map.empty

      return
        Set [
          for KeyValue(package, version) in dependencyMap ->
            {
              package = package
              version = UMX.tag<Semver> version
            }
        ]
    }

  let TestStatsDecoder: Decoder<TestStats> =
    fun element -> decode {
      let! suites = Required.Property.get ("suites", Required.int) element
      let! tests = Required.Property.get ("tests", Required.int) element
      let! passes = Required.Property.get ("passes", Required.int) element
      let! pending = Required.Property.get ("pending", Required.int) element
      let! failures = Required.Property.get ("failures", Required.int) element
      let! start = Required.Property.get ("start", Required.dateTime) element
      let! endTime = Optional.Property.get ("end", Required.dateTime) element

      return {
        suites = suites
        tests = tests
        passes = passes
        pending = pending
        failures = failures
        start = start
        ``end`` = endTime
      }
    }

  let TestDecoder: Decoder<Test> =
    fun element -> decode {
      let! body = Required.Property.get ("body", Required.string) element
      let! duration = Optional.Property.get ("duration", Required.float) element

      let! fullTitle =
        Required.Property.get ("fullTitle", Required.string) element

      let! id = Required.Property.get ("id", Required.string) element

      let! pending = Required.Property.get ("pending", Required.boolean) element

      let! speed = Optional.Property.get ("speed", Required.string) element
      let! state = Optional.Property.get ("state", Required.string) element
      let! title = Required.Property.get ("title", Required.string) element
      let! testType = Required.Property.get ("type", Required.string) element

      return {
        body = body
        duration = duration
        fullTitle = fullTitle
        id = id
        pending = pending
        speed = speed
        state = state
        title = title
        ``type`` = testType
      }
    }

  let SuiteDecoder: Decoder<Suite> =
    fun element -> decode {
      let! id = Required.Property.get ("id", Required.string) element
      let! title = Required.Property.get ("title", Required.string) element

      let! fullTitle =
        Required.Property.get ("fullTitle", Required.string) element

      let! root = Required.Property.get ("root", Required.boolean) element
      let! parent = Optional.Property.get ("parent", Optional.string) element
      let! pending = Required.Property.get ("pending", Required.boolean) element

      let! tests = Required.Property.list ("tests", TestDecoder) element

      return {
        id = id
        title = title
        fullTitle = fullTitle
        root = root
        parent = parent |> Option.flatten
        pending = pending
        tests = tests
      }
    }

  let SessionStart: Decoder<TestEvent> =
    fun element -> decode {
      let! runId = Required.Property.get ("runId", Required.guid) element
      let! stats = Required.Property.get ("stats", TestStatsDecoder) element

      let! totalTests =
        Required.Property.get ("totalTests", Required.int) element

      return SessionStart(runId, stats, totalTests)
    }


  let SessionEnd: Decoder<TestEvent> =
    fun element -> decode {
      let! runId = Required.Property.get ("runId", Required.guid) element
      let! stats = Required.Property.get ("stats", TestStatsDecoder) element
      return SessionEnd(runId, stats)
    }


  let TestPass: Decoder<TestEvent> =
    fun element -> decode {
      let! runId = Required.Property.get ("runId", Required.guid) element
      let! stats = Required.Property.get ("stats", TestStatsDecoder) element
      let! test = Required.Property.get ("test", TestDecoder) element
      return TestEvent.TestPass(runId, stats, test)
    }

  let TestFailed: Decoder<TestEvent> =
    fun element -> decode {
      let! runId = Required.Property.get ("runId", Required.guid) element
      let! stats = Required.Property.get ("stats", TestStatsDecoder) element
      let! test = Required.Property.get ("test", TestDecoder) element
      let! message = Required.Property.get ("message", Required.string) element
      let! stack = Required.Property.get ("stack", Required.string) element
      return TestFailed(runId, stats, test, message, stack)
    }

  let ImportFailed: Decoder<TestEvent> =
    fun element -> decode {
      let! runId = Required.Property.get ("runId", Required.guid) element
      let! message = Required.Property.get ("message", Required.string) element
      let! stack = Required.Property.get ("stack", Required.string) element
      return TestImportFailed(runId, message, stack)
    }

  let SuiteEventArgs: Decoder<Guid * TestStats * Suite> =
    fun element -> decode {
      let! runId = Required.Property.get ("runId", Required.guid) element
      let! stats = Required.Property.get ("stats", TestStatsDecoder) element
      let! suite = Required.Property.get ("suite", SuiteDecoder) element
      return runId, stats, suite
    }

  let TestEventDecoder: Decoder<TestEvent> =
    fun element -> decode {
      let! event = Required.Property.get ("event", Required.string) element

      match event with
      | "__perla-session-start" -> return! SessionStart element
      | "__perla-suite-start" ->
        return! (SuiteEventArgs >> Result.map SuiteStart) element
      | "__perla-suite-end" ->
        return! (SuiteEventArgs >> Result.map SuiteEnd) element
      | "__perla-test-pass" -> return! TestPass element
      | "__perla-test-failed" -> return! TestFailed element
      | "__perla-session-end" -> return! SessionEnd element
      | "__perla-test-import-failed" -> return! ImportFailed element
      | "__perla-test-run-finished" ->
        return!
          Required.Property.get
            ("runId", Required.guid >> Result.map TestRunFinished)
            element
      | value ->
        return!
          DecodeError.ofError(element.Clone(), $"{value} is not a known event")
          |> Error
    }

[<RequireQualifiedAccess>]
module internal Encoders =

  let Browser: Encoder<Browser> = fun value -> Encode.string value.AsString

  let BrowserMode: Encoder<BrowserMode> =
    fun value -> Encode.string value.AsString

  let DownloadProviderEncoder: Encoder<PkgManager.DownloadProvider> =
    fun value -> Encode.string(PkgManager.DownloadProvider.asString value)

  let PkgDependencySetEncoder: Encoder<PkgDependency Set> =
    fun value ->
      Json.object [
        for dep in value ->
          dep.package, Encode.string(UMX.untag<Semver> dep.version)
      ]


let DefaultJsonOptions() =
  JsonSerializerOptions(
    WriteIndented = false,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  )
  |> Codec.useDecoder TestEventDecoder
  |> Codec.useDecoder PkgDependencyDecoder
  |> Codec.useDecoder PkgManager.DownloadResponse.Decoder
  |> Codec.useCodec(Encoders.Browser, BrowserDecoder)
  |> Codec.useCodec(Encoders.BrowserMode, BrowserModeDecoder)
  |> Codec.useCodec(Encoders.DownloadProviderEncoder, DownloadProviderDecoder)
  |> Codec.useCodec(Encoders.PkgDependencySetEncoder, PkgDependencySetDecoder)
  |> Codec.useCodec(PkgManager.ImportMap.Encoder, PkgManager.ImportMap.Decoder)


let DefaultJsonNodeOptions() =
  JsonNodeOptions(PropertyNameCaseInsensitive = true)

let DefaultJsonDocumentOptions() =
  JsonDocumentOptions(
    AllowTrailingCommas = true,
    CommentHandling = JsonCommentHandling.Skip
  )

type Json =
  static member ToBytes value =
    JsonSerializer.SerializeToUtf8Bytes(value, DefaultJsonOptions())

  static member FromBytes<'T when 'T: not struct and 'T: not null>
    (value: byte array)
    =
    let value: 'T | null =
      JsonSerializer.Deserialize<'T>(ReadOnlySpan value, DefaultJsonOptions())

    nonNull value

  static member FromStream<'T when 'T: not struct and 'T: not null>
    (stream: IO.Stream)
    =
    cancellableTask {
      let! cancellationToken = CancellableTask.getCancellationToken()

      let! value =
        JsonSerializer.DeserializeAsync<'T>(
          stream,
          DefaultJsonOptions(),
          cancellationToken
        )

      return nonNull value
    }

  static member ToText(value, ?minify: bool) =
    let options = DefaultJsonOptions()
    let shouldMinify = minify |> Option.defaultValue false

    options.WriteIndented <- not shouldMinify

    JsonSerializer.Serialize(value, options)

  static member ToNode value =
    match JsonSerializer.SerializeToNode(value, DefaultJsonOptions()) with
    | null -> failwith "Serialization to JsonNode failed"
    | result -> result

  static member FromConfigFile(content: string) =
    Decoding.auto<DecodedPerlaConfig>(content, DefaultJsonOptions())

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
    try
      use jsonDocument = System.Text.Json.JsonDocument.Parse value
      let jsonElement = jsonDocument.RootElement
      TestEventDecoder jsonElement
    with ex ->
      let dummyElement = System.Text.Json.JsonDocument.Parse("{}").RootElement

      Error {
        value = dummyElement
        kind = JsonValueKind.String
        rawValue = value
        targetType = typeof<TestEvent>
        message = ex.Message
        exn = Some ex
        index = None
        property = None
      }



module PerlaConfig =

  [<RequireQualifiedAccess>]
  module FromDecoders =

    let GetFable(config: FableConfig option, fable: DecodedFableConfig option) = option {
      let! decoded = fable
      let fable = defaultArg config Defaults.FableConfig

      let outDir = decoded.outDir |> Option.orElseWith(fun () -> fable.outDir)

      return {
        fable with
            project = defaultArg decoded.project fable.project
            extension = defaultArg decoded.extension fable.extension
            sourceMaps = defaultArg decoded.sourceMaps fable.sourceMaps
            outDir = outDir
      }
    }

    let GetDevServer
      (config: DevServerConfig, devServer: DecodedDevServer option)
      =
      option {
        let! decoded = devServer

        return {
          config with
              port = defaultArg decoded.port config.port
              host = defaultArg decoded.host config.host
              liveReload = defaultArg decoded.liveReload config.liveReload
              useSSL = defaultArg decoded.useSSL config.useSSL
              proxy = defaultArg decoded.proxy config.proxy
        }
      }

    let GetBuild(config: BuildConfig, build: DecodedBuild option) = option {
      let! decoded = build

      return {
        config with
            includes = defaultArg decoded.includes config.includes
            excludes = defaultArg decoded.excludes config.excludes
            outDir = defaultArg decoded.outDir config.outDir
            emitEnvFile = defaultArg decoded.emitEnvFile config.emitEnvFile
      }
    }

    let GetEsbuild(config: EsbuildConfig, esbuild: DecodedEsbuild option) = option {
      let! decoded = esbuild

      return {
        config with
            version = defaultArg decoded.version config.version
            ecmaVersion = defaultArg decoded.ecmaVersion config.ecmaVersion
            minify = defaultArg decoded.minify config.minify
            injects = defaultArg decoded.injects config.injects
            externals = defaultArg decoded.externals config.externals
            fileLoaders = defaultArg decoded.fileLoaders config.fileLoaders
            jsxAutomatic = defaultArg decoded.jsxAutomatic config.jsxAutomatic
            jsxImportSource = decoded.jsxImportSource
      }
    }

    let GetTesting(config: TestConfig, testing: DecodedTesting option) = option {
      let! testing = testing

      return {
        config with
            browsers = defaultArg testing.browsers config.browsers
            includes = defaultArg testing.includes config.includes
            excludes = defaultArg testing.excludes config.excludes
            watch = defaultArg testing.watch config.watch
            headless = defaultArg testing.headless config.headless
            browserMode = defaultArg testing.browserMode config.browserMode
            fable = GetFable(config.fable, testing.fable)
      }
    }

  [<RequireQualifiedAccess>]
  module FromFields =
    type DevServerField =
      | Port of int
      | Host of string
      | LiveReload of bool
      | UseSSL of bool
      | MinifySources of bool

    [<RequireQualifiedAccess>]
    type TestingField =
      | Browsers of Browser seq
      | Includes of string seq
      | Excludes of string seq
      | Watch of bool
      | Headless of bool
      | BrowserMode of BrowserMode

    type FableField =
      | Project of string
      | Extension of string
      | SourceMaps of bool
      | OutDir of bool

    let GetServerFields
      (config: DevServerConfig, serverOptions: DevServerField seq option)
      =
      let getDefaults() = seq {
        DevServerField.Port config.port
        DevServerField.Host config.host
        DevServerField.LiveReload config.liveReload
        DevServerField.UseSSL config.useSSL
      }

      let options = serverOptions |> Option.defaultWith getDefaults

      if Seq.isEmpty options then getDefaults() else options

    let GetMinify(serverOptions: DevServerField seq) =
      serverOptions
      |> Seq.tryPick(fun opt ->
        match opt with
        | MinifySources minify -> Some minify
        | _ -> None)
      |> Option.defaultWith(fun _ -> true)

    let GetDevServerOptions
      (config: DevServerConfig, serverOptions: DevServerField seq)
      =
      serverOptions
      |> Seq.fold
        (fun (current: DevServerConfig) next ->
          match next with
          | Port port -> { current with port = port }
          | Host host -> { current with host = host }
          | LiveReload liveReload -> { current with liveReload = liveReload }
          | UseSSL useSSL -> { current with useSSL = useSSL }
          | _ -> current)
        config

    let GetTesting
      (testing: TestConfig, testingOptions: TestingField seq option)
      =
      defaultArg testingOptions Seq.empty
      |> Seq.fold
        (fun (current: TestConfig) next ->
          match next with
          | TestingField.Browsers value -> { current with browsers = value }
          | TestingField.Includes value -> { current with includes = value }
          | TestingField.Excludes value -> { current with excludes = value }
          | TestingField.Watch value -> { current with watch = value }
          | TestingField.Headless value -> { current with headless = value }
          | TestingField.BrowserMode value ->
              { current with browserMode = value })
        testing

  let FromString content =
    let config = Defaults.PerlaConfig
    let userConfig = Json.FromConfigFile content |> Result.toOption
    let userFable = userConfig |> Option.map _.fable |> Option.flatten
    let userDevServer = userConfig |> Option.map _.devServer |> Option.flatten
    let userBuild = userConfig |> Option.map _.build |> Option.flatten
    let userEsbuild = userConfig |> Option.map _.esbuild |> Option.flatten
    let userTesting = userConfig |> Option.map _.testing |> Option.flatten
    let userPlugins = userConfig |> Option.map _.plugins |> Option.flatten
    let userIndex = userConfig |> Option.map _.index |> Option.flatten
    let userProvider = userConfig |> Option.map _.provider |> Option.flatten
    let userEnvPath = userConfig |> Option.map _.envPath |> Option.flatten
    let useLocalPkgs = userConfig |> Option.map _.useLocalPkgs |> Option.flatten

    let userDependencies =
      userConfig |> Option.map _.dependencies |> Option.flatten

    let userMountDirectories =
      userConfig |> Option.map _.mountDirectories |> Option.flatten

    let userPaths = userConfig |> Option.map _.paths |> Option.flatten
    let userEnableEnv = userConfig |> Option.map _.enableEnv |> Option.flatten

    let fable = FromDecoders.GetFable(config.fable, userFable)

    let devServer =
      FromDecoders.GetDevServer(config.devServer, userDevServer)
      |> Option.defaultValue Defaults.DevServerConfig

    let build =
      FromDecoders.GetBuild(config.build, userBuild)
      |> Option.defaultValue Defaults.BuildConfig

    let esbuild =
      FromDecoders.GetEsbuild(config.esbuild, userEsbuild)
      |> Option.defaultValue Defaults.EsbuildConfig

    let testing =
      FromDecoders.GetTesting(config.testing, userTesting)
      |> Option.defaultValue Defaults.TestConfig

    let plugins =
      userPlugins |> Option.defaultValue Defaults.PerlaConfig.plugins

    {
      config with
          index = defaultArg userIndex config.index
          provider = defaultArg userProvider config.provider
          useLocalPkgs = defaultArg useLocalPkgs config.useLocalPkgs
          mountDirectories =
            defaultArg userMountDirectories config.mountDirectories
          enableEnv = defaultArg userEnableEnv config.enableEnv
          envPath = defaultArg userEnvPath config.envPath
          paths = defaultArg userPaths config.paths
          dependencies = defaultArg userDependencies config.dependencies
          plugins = plugins
          build = build
          devServer = devServer
          fable = fable
          esbuild = esbuild
          testing = testing
    }

  type FableField =
    | Project of string
    | Extension of string
    | SourceMaps of bool
    | OutDir of bool

  type PerlaWritableField =
    | Provider of PkgManager.DownloadProvider
    | Dependencies of PkgDependency Set
    | UseLocalPkgs of bool
    | Fable of FableField seq
    | Paths of Map<string<BareImport>, string<ResolutionUrl>>

  let UpdateFileFields
    (jsonContents: JsonObject option)
    (fields: PerlaWritableField seq)
    =

    let provider =
      fields
      |> Seq.tryPick(fun f ->
        match f with
        | Provider config -> Some config
        | _ -> None)
      |> Option.map PkgManager.DownloadProvider.asString

    let useLocalPkgs =
      fields
      |> Seq.tryPick(fun f ->
        match f with
        | UseLocalPkgs useLocalPkgs -> Some useLocalPkgs
        | _ -> None)

    let dependencies =
      fields
      |> Seq.tryPick(fun f ->
        match f with
        | Dependencies deps -> Some deps
        | _ -> None)

    let paths =
      fields
      |> Seq.tryPick(fun f ->
        match f with
        | Paths paths -> Some paths
        | _ -> None)

    let fable =
      fields
      |> Seq.tryPick(fun f ->
        match f with
        | Fable fields ->
          let mutable f = {|
            project = None
            extension = None
            sourceMaps = None
            outDir = None
          |}

          for field in fields do
            match field with
            | Project path -> f <- {| f with project = Some path |}
            | Extension ext -> f <- {| f with extension = Some ext |}
            | SourceMaps sourceMaps ->
              f <- {|
                f with
                    sourceMaps = Some sourceMaps
              |}
            | OutDir outDir -> f <- {| f with outDir = Some outDir |}

          Some f
        | _ -> None)

    let addProvider(content: JsonObject) =
      match provider with
      | Some config ->
        content["provider"] <- JsonSerializer.SerializeToNode(config)
      | None -> ()

      content

    let addUseLocalPkgs(content: JsonObject) =
      match useLocalPkgs with
      | Some useLocalPkgs ->
        content["useLocalPkgs"] <- JsonSerializer.SerializeToNode(useLocalPkgs)
      | None -> ()

      content

    let addDeps(content: JsonObject) =
      match dependencies with
      | Some deps ->
        let dependencies =
          deps |> Seq.map(fun dep -> dep.package, dep.version) |> Map.ofSeq

        content["dependencies"] <- Json.ToNode(dependencies)
      | None -> ()

      content

    let addFable(content: JsonObject) =
      match fable with
      | Some fable -> content["fable"] <- Json.ToNode(fable)
      | None -> ()

      content

    let addPaths(content: JsonObject) =
      match paths with
      | Some paths -> content["paths"] <- Json.ToNode(paths)
      | None -> ()

      content

    let content =
      match jsonContents with
      | Some content -> content
      | None ->
        (JsonObject.Parse($"""{{ "$schema": "{Constants.JsonSchemaUrl}" }}""")
         |> nonNull)
          .AsObject()

    match
      content["$schema"]
      |> Option.ofObj
      |> Option.map(fun schema -> schema.GetValue<string>() |> Option.ofNull)
      |> Option.flatten
    with
    | Some _ -> ()
    | None -> content["$schema"] <- Json.ToNode(Constants.JsonSchemaUrl)

    content |> addProvider |> addUseLocalPkgs |> addDeps |> addFable |> addPaths
