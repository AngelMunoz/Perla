module Perla.Json

open System
open System.Text.Json
open System.Text.Json.Nodes
open Perla.Types
open Perla.Units
open JDeck

open FSharp.UMX

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
  val DownloadProviderDecoder: Decoder<PkgManager.DownloadProvider>
  val PkgDependencyDecoder: Decoder<PkgDependency>
  val PkgDependencySetDecoder: Decoder<PkgDependency Set>
  val TestStatsDecoder: Decoder<TestStats>
  val TestDecoder: Decoder<Test>
  val SuiteDecoder: Decoder<Suite>
  val SessionStart: Decoder<TestEvent>
  val SessionEnd: Decoder<TestEvent>
  val TestPass: Decoder<TestEvent>
  val TestFailed: Decoder<TestEvent>
  val ImportFailed: Decoder<TestEvent>
  val SuiteEventArgs: Decoder<Guid * TestStats * Suite>
  val TestEventDecoder: Decoder<TestEvent>

[<RequireQualifiedAccess>]
module internal Encoders =
  val Browser: Encoder<Browser>
  val BrowserMode: Encoder<BrowserMode>
  val DownloadProviderEncoder: Encoder<PkgManager.DownloadProvider>
  val PkgDependencySetEncoder: Encoder<PkgDependency Set>

[<RequireQualifiedAccess; Struct>]
type PerlaConfigSection =
  | Index of index: string option
  | Fable of fable: FableConfig option
  | DevServer of devServer: DevServerConfig option
  | Build of build: BuildConfig option
  | Dependencies of dependencies: PkgDependency Set option

val DefaultJsonOptions: unit -> JsonSerializerOptions
val DefaultJsonNodeOptions: unit -> JsonNodeOptions
val DefaultJsonDocumentOptions: unit -> JsonDocumentOptions

[<Class>]
type Json =

  static member FromBytes<'T when 'T: not struct and 'T: not null> :
    value: byte array -> 'T

  static member FromStream<'T when 'T: not struct and 'T: not null> :
    stream: IO.Stream -> IcedTasks.CancellableTasks.CancellableTask<'T>

  static member ToBytes: value: 'a -> byte array
  static member ToText: value: 'a * ?minify: bool -> string
  static member ToNode: value: 'a -> JsonNode

  static member FromConfigFile:
    string -> Result<DecodedPerlaConfig, DecodeError>

  static member TestEventFromJson: string -> Result<TestEvent, DecodeError>


module PerlaConfig =
  val FromString: content: string -> PerlaConfig

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

  val UpdateFileFields:
    jsonContents: JsonObject option ->
    fields: PerlaWritableField seq ->
      JsonObject
