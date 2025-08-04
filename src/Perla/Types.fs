namespace Perla

open System

module Units =

  [<Measure>]
  type Semver

  [<Measure>]
  type SystemPath

  [<Measure>]
  type FileExtension

  [<Measure>]
  type ServerUrl

  [<Measure>]
  type UserPath

  [<Measure>]
  type RepositoryGroup

  [<Measure>]
  type TemplateGroup

  [<Measure>]
  type BareImport

  [<Measure>]
  type ResolutionUrl

  [<Measure>]
  type Repository

  [<Measure>]
  type Branch


module Types =
  open FSharp.UMX
  open Units

  type FableConfig = {
    project: string<SystemPath>
    extension: string<FileExtension>
    sourceMaps: bool
    outDir: string<SystemPath> option
  }

  type DevServerConfig = {
    port: int
    host: string
    liveReload: bool
    useSSL: bool
    proxy: Map<string, string>
  }

  type EsbuildConfig = {
    version: string<Semver>
    ecmaVersion: string
    minify: bool
    injects: string seq
    externals: string seq
    fileLoaders: Map<string, string>
    jsxAutomatic: bool
    jsxImportSource: string option
    aliases: Map<string<BareImport>, string<ResolutionUrl>>
  }

  type BuildConfig = {
    includes: string seq
    excludes: string seq
    outDir: string<SystemPath>
    emitEnvFile: bool
  }

  type PkgDependency = {
    package: string
    version: string<Semver>
  }

  [<Struct; RequireQualifiedAccess>]
  type Browser =
    | Webkit
    | Firefox
    | Chromium
    | Edge
    | Chrome

  [<Struct; RequireQualifiedAccess>]
  type BrowserMode =
    | Parallel
    | Sequential


  [<Struct>]
  type TestFramework =
    | Mocha
    | QUnit

  type TestConfig = {
    browsers: Browser seq
    includes: string seq
    excludes: string seq
    watch: bool
    headless: bool
    browserMode: BrowserMode
    fable: FableConfig option
    testFramework: TestFramework
    frameworkOptions: Map<string, obj>
  }

  type PerlaConfig = {
    index: string<SystemPath>
    provider: PkgManager.DownloadProvider
    useLocalPkgs: bool
    plugins: string list
    build: BuildConfig
    devServer: DevServerConfig
    fable: FableConfig option
    esbuild: EsbuildConfig
    testing: TestConfig
    mountDirectories: Map<string<ServerUrl>, string<UserPath>>
    enableEnv: bool
    envPath: string<ServerUrl>
    paths: Map<string<BareImport>, string<ResolutionUrl>>
    dependencies: PkgDependency Set
  }

  type Test = {
    body: string
    duration: float option
    fullTitle: string
    id: string
    pending: bool
    speed: string option
    state: string option
    title: string
    ``type``: string
  }

  type Suite = {
    id: string
    title: string
    fullTitle: string
    root: bool
    parent: string option
    pending: bool
    tests: Test list
  }

  type TestStats = {
    suites: int
    tests: int
    passes: int
    pending: int
    failures: int
    start: DateTime
    ``end``: DateTime option
  }

  type SessionStartEvent = {
    RunId: Guid
    Browser: Browser option
    Stats: TestStats
    TotalTests: int
  }

  type SessionEndEvent = {
    RunId: Guid
    Browser: Browser option
    Stats: TestStats
  }

  type SuiteEvent = {
    RunId: Guid
    Browser: Browser option
    Stats: TestStats
    Suite: Suite
  }

  type TestPassEvent = {
    RunId: Guid
    Browser: Browser option
    Stats: TestStats
    Test: Test
  }

  type TestFailedEvent = {
    RunId: Guid
    Browser: Browser option
    Stats: TestStats
    Test: Test
    Message: string
    Stack: string
  }

  type TestImportFailedEvent = {
    RunId: Guid
    Browser: Browser option
    Message: string
    Stack: string
  }

  type TestRunFinishedEvent = { RunId: Guid; Browser: Browser option }

  type TestEvent =
    | SessionStart of SessionStartEvent
    | SessionEnd of SessionEndEvent
    | SuiteStart of SuiteEvent
    | SuiteEnd of SuiteEvent
    | TestPass of TestPassEvent
    | TestFailed of TestFailedEvent
    | TestImportFailed of TestImportFailedEvent
    | TestRunFinished of TestRunFinishedEvent

  exception CommandNotParsedException of string
  exception HelpRequestedException
  exception MissingPackageNameException
  exception MissingImportMapPathException
  exception PackageNotFoundException
  exception HeaderNotFoundException of string
  exception FailedToParseNameException of string


  type Browser with

    member this.AsString =
      match this with
      | Chromium -> "chromium"
      | Chrome -> "chrome"
      | Edge -> "edge"
      | Webkit -> "webkit"
      | Firefox -> "firefox"

    static member FromString(value: string) =
      match value.ToLowerInvariant() with
      | "chromium" -> Chromium
      | "chrome" -> Chrome
      | "edge" -> Edge
      | "webkit" -> Webkit
      | "firefox" -> Firefox
      | _ -> Chromium

  type BrowserMode with

    member this.AsString =
      match this with
      | Parallel -> "parallel"
      | Sequential -> "sequential"

    static member FromString(value: string) =
      match value.ToLowerInvariant() with
      | "parallel" -> Parallel
      | "sequential" -> Sequential
      | _ -> Parallel

  type TestEvent with
    member this.RunId =
      match this with
      | SessionStart { RunId = runId } -> runId
      | SessionEnd { RunId = runId } -> runId
      | SuiteStart { RunId = runId } -> runId
      | SuiteEnd { RunId = runId } -> runId
      | TestPass { RunId = runId } -> runId
      | TestFailed { RunId = runId } -> runId
      | TestImportFailed { RunId = runId } -> runId
      | TestRunFinished { RunId = runId } -> runId

    member this.Browser =
      match this with
      | SessionStart { Browser = browser } -> browser
      | SessionEnd { Browser = browser } -> browser
      | SuiteStart { Browser = browser } -> browser
      | SuiteEnd { Browser = browser } -> browser
      | TestPass { Browser = browser } -> browser
      | TestFailed { Browser = browser } -> browser
      | TestImportFailed { Browser = browser } -> browser
      | TestRunFinished { Browser = browser } -> browser

module Defaults =
  open Types
  open Units
  open FSharp.UMX

  let FableConfig: FableConfig = {
    project = UMX.tag "./src/App.fsproj"
    extension = UMX.tag ".fs.js"
    sourceMaps = true
    outDir = None
  }

  let DevServerConfig: DevServerConfig = {
    port = 7331
    host = "localhost"
    liveReload = true
    useSSL = false
    proxy = Map.empty
  }

  let EsbuildConfig: EsbuildConfig = {
    version = UMX.tag Constants.Esbuild_Version
    ecmaVersion = Constants.Esbuild_Target
    minify = true
    injects = Seq.empty
    externals = Seq.empty
    fileLoaders =
      [ ".png", "file"; ".woff", "file"; ".woff2", "file"; ".svg", "file" ]
      |> Map.ofList
    jsxAutomatic = false
    jsxImportSource = None
    aliases = Map.empty
  }

  let BuildConfig = {
    includes = Seq.empty
    excludes = seq {
      "./**/obj/**"
      "./**/bin/**"
      "./**/*.fs"
      "./**/*.fsi"
      "./**/*.fsproj"
    }
    outDir = UMX.tag "./dist"
    emitEnvFile = true
  }

  let TestConfig = {
    browsers = [ Browser.Chromium ]
    includes = [
      "**/*.test.js"
      "**/*.spec.js"
      "**/*.Test.fs.js"
      "**/*.Spec.fs.js"
    ]
    excludes = []
    watch = false
    headless = true
    browserMode = BrowserMode.Parallel
    fable = None
    testFramework = TestFramework.QUnit
    frameworkOptions = Map.empty
  }

  let PerlaConfig = {
    index = UMX.tag $"./{Constants.IndexFile}"
    provider = PkgManager.DownloadProvider.JspmIo
    useLocalPkgs = false
    plugins = []
    build = BuildConfig
    devServer = DevServerConfig
    esbuild = EsbuildConfig
    testing = TestConfig
    fable = None
    mountDirectories =
      Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]
    enableEnv = true
    envPath = UMX.tag Constants.EnvPath
    paths = Map.empty
    dependencies = Set.empty
  }
