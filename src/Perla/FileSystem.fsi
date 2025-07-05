namespace Perla.FileSystem

open System
open System.IO
open System.Runtime.InteropServices
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks

open IcedTasks
open FSharp.UMX
open FSharp.Data.Adaptive
open Perla.Units
open Perla.PackageManager.Types
open Perla.Json

[<RequireQualifiedAccess>]
type PerlaFileChange =
  | Index
  | PerlaConfig
  | ImportMap

[<RequireQualifiedAccess>]
module FileSystem =
  module Operators =
    val inline (/): a: string -> b: string -> string

  val AssemblyRoot: string<SystemPath>
  val PerlaArtifactsRoot: string<SystemPath>
  val Database: string<SystemPath>
  val Templates: string<SystemPath>
  val PerlaConfigPath: string<SystemPath>
  val LiveReloadScript: Lazy<string>
  val WorkerScript: Lazy<string>
  val TestingHelpersScript: Lazy<string>
  val MochaRunnerScript: Lazy<string>
  val DescriptionsFile: Lazy<Map<string, string>>
  val CurrentWorkingDirectory: unit -> string<SystemPath>

  val GetConfigPath:
    fileName: string ->
    fromDirectory: string<SystemPath> option ->
      string<SystemPath>

  val ExtractTemplateZip:
    username: string * repository: string * branch: string ->
      stream: Stream ->
        string<SystemPath> *
        Result<TemplateDecoders.DecodedTemplateConfiguration, string>

  val RemoveTemplateDirectory: path: string<SystemPath> -> unit
  val EsbuildBinaryPath: string<Semver> option -> string<SystemPath>
  val TryReadTsConfig: unit -> string option
  val GetTempDir: unit -> string

  val TplRepositoryChildTemplates:
    path: string<SystemPath> -> string<SystemPath> seq

  val DotNetToolRestore:
    cancellationToken: CancellationToken -> Task<Result<unit, string>>

  val CheckFableExists: cancellationToken: CancellationToken -> Task<bool>


type PerlaDirectories =
  abstract member AssemblyRoot: string<SystemPath> with get
  abstract member PerlaArtifactsRoot: string<SystemPath> with get
  abstract member Database: string<SystemPath> with get
  abstract member Templates: string<SystemPath> with get
  abstract member PerlaConfigPath: string<SystemPath> with get
  abstract member LiveReloadScript: string with get
  abstract member WorkerScript: string with get
  abstract member TestingHelpersScript: string with get
  abstract member MochaRunnerScript: string with get
  abstract member DescriptionsFile: Map<string, string> with get
  abstract member CurrentWorkingDirectory: string<SystemPath> with get


type PerlaFsManager =
  abstract SetCwdToPerlaRoot: ?fromPath: string<SystemPath> -> unit

  abstract ResolveConfig:
    unit ->
      CancellableTask<Perla.Types.PerlaConfig option>

  abstract ResolveImportMap:
    unit -> CancellableTask<Perla.PkgManager.ImportMap option>

  abstract ResolveIndexPath:
    unit -> CancellableTask<string<SystemPath>>

  abstract ResolvePluginPaths:
    unit -> (string * string)[]

  abstract ResolveDotEnvPaths:
    unit -> string<SystemPath>[]

  abstract ObservePerlaFiles: unit -> PerlaFileChange aval

[<Class>]
type FileSystem =
  static member PerlaConfigText:
    ?fromDirectory: string<SystemPath> -> string option

  static member SetCwdToPerlaRoot: ?fromPath: string<SystemPath> -> unit
  static member GetImportMap: ?fromDirectory: string<SystemPath> -> ImportMap

  static member SetupEsbuild:
    esbuildVersion: string<Semver> *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<unit>

  static member WriteImportMap:
    map: ImportMap * ?fromDirectory: string<SystemPath> -> ImportMap

  static member WritePerlaConfig:
    ?config: JsonObject * ?fromDirectory: string<SystemPath> -> unit

  static member WriteTplRepositoryToDisk:
    origin: string<SystemPath> * target: string<UserPath> * ?payload: obj ->
      unit

  static member IndexFile: fromConfig: string<SystemPath> -> string
  static member PluginFiles: unit -> (string * string) array

  static member ObservePerlaFiles:
    indexPath: string * [<Optional>] ?cancellationToken: CancellationToken ->
      IObservable<PerlaFileChange>

  static member GetDotEnvFilePaths:
    ?fromDirectory: string<SystemPath> -> string<SystemPath> seq
