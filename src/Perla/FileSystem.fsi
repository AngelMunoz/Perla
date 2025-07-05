namespace Perla.FileSystem

open System
open System.IO
open System.Runtime.InteropServices
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open IcedTasks
open FSharp.UMX
open FSharp.Data.Adaptive
open Perla.Types
open Perla.Units
open Perla.PackageManager.Types
open Perla.Json
open Perla.Json.TemplateDecoders

[<RequireQualifiedAccess>]
type PerlaFileChange =
  | Index
  | PerlaConfig
  | ImportMap

[<Measure>]
type Repository

[<Measure>]
type Branch

[<Interface>]
type PerlaDirectories =
  abstract AssemblyRoot: string<SystemPath> with get
  abstract PerlaArtifactsRoot: string<SystemPath> with get
  abstract Database: string<SystemPath> with get
  abstract Templates: string<SystemPath> with get
  abstract PerlaConfigPath: string<SystemPath> with get
  abstract CurrentWorkingDirectory: string<SystemPath> with get
  abstract SetCwdToProject: ?fromPath: string<SystemPath> -> unit

[<Interface>]
type PerlaFsManager =

  abstract PerlaConfiguration: PerlaConfig aval

  abstract ResolveIndexPath: string<SystemPath> aval

  abstract ResolveIndex: string aval

  abstract DotEnvContents: Map<string, string> aval

  abstract ResolveImportMap: Perla.PkgManager.ImportMap aval

  abstract ResolveDescriptionsFile: unit -> CancellableTask<Map<string, string>>

  abstract ResolvePluginPaths: unit -> (string * string)[]

  abstract ResolveEsbuildPath: unit -> string<SystemPath>

  abstract ResolveLiveReloadScript: unit -> CancellableTask<string>
  abstract ResolveWorkerScript: unit -> CancellableTask<string>
  abstract ResolveTestingHelpersScript: unit -> CancellableTask<string>
  abstract ResolveMochaRunnerScript: unit -> CancellableTask<string>

  abstract SetupEsbuild: string<Semver> -> CancellableTask<unit>

  abstract SetupFable: unit -> CancellableTask<unit>

  abstract SetupTemplate:
    user: string * repository: string<Repository> * branch: string<Branch> ->
      CancellableTask<DecodedTemplateConfiguration option>


[<RequireQualifiedAccess>]
module FileSystem =

  val GetDirectories: unit -> PerlaDirectories

  val GetManager:
    logger: ILogger * env: Perla.Env.PlatformOps * dirs: PerlaDirectories ->
      PerlaFsManager

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
