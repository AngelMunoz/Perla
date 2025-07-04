module Perla.Configuration

open Perla.Types
open Perla.Units
open FSharp.UMX
open Perla.PackageManager.Types
open System.Runtime.InteropServices
open System.Text.Json.Nodes

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Types =
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

  [<RequireQualifiedAccess>]
  type PerlaWritableField =
    | Provider of PkgManager.DownloadProvider
    | Dependencies of PkgDependency Set
    | Fable of FableField seq
    | Paths of Map<string<BareImport>, string<ResolutionUrl>>

open Types


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Defaults =
  val FableConfig: FableConfig
  val DevServerConfig: DevServerConfig
  val EsbuildConfig: EsbuildConfig
  val BuildConfig: BuildConfig
  val TestConfig: TestConfig
  val PerlaConfig: PerlaConfig

module internal ConfigExtraction =

  [<RequireQualifiedAccess>]
  module FromDecoders =
    open Json.ConfigDecoders

    val GetFable:
      FableConfig option * DecodedFableConfig option -> FableConfig option

    val GetDevServer:
      DevServerConfig * DecodedDevServer option -> DevServerConfig option

    val GetBuild: BuildConfig * DecodedBuild option -> BuildConfig option

    val GetEsbuild:
      EsbuildConfig * DecodedEsbuild option -> EsbuildConfig option

    val GetTesting: TestConfig * DecodedTesting option -> TestConfig option

  [<RequireQualifiedAccess>]
  module FromFields =
    val GetServerFields:
      DevServerConfig * DevServerField seq option -> DevServerField seq

    val GetMinify: DevServerField seq -> bool

    val GetDevServerOptions:
      DevServerConfig * DevServerField seq -> DevServerConfig

    val GetTesting: TestConfig * TestingField seq option -> TestConfig

  val FromEnv: config: PerlaConfig -> PerlaConfig

  val FromCli:
    provider: PkgManager.DownloadProvider option ->
    serverOptions: DevServerField seq option ->
    testingOptions: TestingField seq option ->
    config: PerlaConfig ->
      PerlaConfig

  val FromFile:
    fileContent: JsonObject option -> config: PerlaConfig -> PerlaConfig

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module internal Json =

  val getConfigDocument: perlaJsonText: string -> JsonObject

/// <summary>
/// Represents a store for the perla configuration for the current session
/// This class ensures there's a central way to update perla's configuration
/// either by CLI, or the perla.json file, this also ensures some of the
/// configuration fields are written correctly to perla.json
/// </summary>
[<Class>]
type ConfigurationManager =
  new:
    readPerlaJsonText: (unit -> string option) *
    writePerlaJsonText: (JsonObject option -> unit) ->
      ConfigurationManager

  member CurrentConfig: PerlaConfig

  member UpdateFromCliArgs:
    [<Optional>] ?provider: PkgManager.DownloadProvider *
    [<Optional>] ?serverOptions: seq<DevServerField> *
    [<Optional>] ?testingOptions: seq<TestingField> ->
      unit

  member UpdateFromFile: unit -> unit
  member WriteFieldsToFile: newValues: seq<PerlaWritableField> -> unit

val ConfigurationManager: ConfigurationManager
