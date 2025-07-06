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

[<RequireQualifiedAccess; Obsolete>]
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
      CancellableTask<
        (string<SystemPath> *
        DecodedTemplateConfiguration *
        string *
        string<Repository> *
        string<Branch>) option
       >


[<RequireQualifiedAccess>]
module FileSystem =

  val GetDirectories: unit -> PerlaDirectories

  val GetManager:
    logger: ILogger * env: Perla.Env.PlatformOps * dirs: PerlaDirectories ->
      PerlaFsManager

  [<Obsolete>]
  val AssemblyRoot: string<SystemPath>

  [<Obsolete>]
  val PerlaArtifactsRoot: string<SystemPath>

  [<Obsolete>]
  val Database: string<SystemPath>

  [<Obsolete>]
  val Templates: string<SystemPath>

  [<Obsolete>]
  val PerlaConfigPath: string<SystemPath>

  [<Obsolete>]
  val LiveReloadScript: Lazy<string>

  [<Obsolete>]
  val WorkerScript: Lazy<string>

  [<Obsolete>]
  val TestingHelpersScript: Lazy<string>

  [<Obsolete>]
  val MochaRunnerScript: Lazy<string>

  [<Obsolete>]
  val DescriptionsFile: Lazy<Map<string, string>>

  [<Obsolete>]
  val CurrentWorkingDirectory: unit -> string<SystemPath>

  [<Obsolete>]
  val GetConfigPath:
    fileName: string ->
    fromDirectory: string<SystemPath> option ->
      string<SystemPath>

  [<Obsolete>]
  val ExtractTemplateZip:
    username: string * repository: string * branch: string ->
      stream: Stream ->
        string<SystemPath> *
        Result<TemplateDecoders.DecodedTemplateConfiguration, string>

  [<Obsolete>]
  val RemoveTemplateDirectory: path: string<SystemPath> -> unit

  [<Obsolete>]
  val EsbuildBinaryPath: string<Semver> option -> string<SystemPath>

  [<Obsolete>]
  val TryReadTsConfig: unit -> string option

  [<Obsolete>]
  val GetTempDir: unit -> string

  [<Obsolete>]
  val TplRepositoryChildTemplates:
    path: string<SystemPath> -> string<SystemPath> seq

  [<Obsolete>]
  val DotNetToolRestore:
    cancellationToken: CancellationToken -> Task<Result<unit, string>>

  [<Obsolete>]
  val CheckFableExists: cancellationToken: CancellationToken -> Task<bool>

[<Class; Obsolete>]
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
