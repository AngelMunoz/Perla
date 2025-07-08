namespace Perla

open System
open Perla.PkgManager

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

    member AsString: string
    static member FromString: string -> Browser

  [<Struct; RequireQualifiedAccess>]
  type BrowserMode =
    | Parallel
    | Sequential

    member AsString: string
    static member FromString: string -> BrowserMode

  type TestConfig = {
    browsers: Browser seq
    includes: string seq
    excludes: string seq
    watch: bool
    headless: bool
    browserMode: BrowserMode
    fable: FableConfig option
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

  type TestEvent =
    | SessionStart of runId: Guid * stats: TestStats * totalTests: int
    | SessionEnd of runId: Guid * stats: TestStats
    | SuiteStart of runId: Guid * stats: TestStats * suite: Suite
    | SuiteEnd of runId: Guid * stats: TestStats * suite: Suite
    | TestPass of runId: Guid * stats: TestStats * test: Test
    | TestFailed of
      runId: Guid *
      stats: TestStats *
      test: Test *
      message: string *
      stack: string
    | TestImportFailed of runId: Guid * message: string * stack: string
    | TestRunFinished of runId: Guid

  exception CommandNotParsedException of string
  exception HelpRequestedException
  exception MissingPackageNameException
  exception MissingImportMapPathException
  exception PackageNotFoundException
  exception HeaderNotFoundException of string
  exception FailedToParseNameException of string

module Defaults =
  open Types

  val FableConfig: FableConfig
  val DevServerConfig: DevServerConfig
  val EsbuildConfig: EsbuildConfig
  val BuildConfig: BuildConfig
  val TestConfig: TestConfig
  val PerlaConfig: PerlaConfig
