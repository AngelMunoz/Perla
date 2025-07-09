namespace Perla.FileSystem

open System.Text.Json.Nodes

open Microsoft.Extensions.Logging

open IcedTasks
open FSharp.UMX
open FSharp.Data.Adaptive
open Perla
open Perla.Types
open Perla.Units
open Perla.Json.TemplateDecoders

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
  abstract OfflineTemplates: string<SystemPath> with get
  abstract OriginalCwd: string<SystemPath>
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

  abstract ResolveTsConfig: string option aval

  abstract ResolveOfflineTemplatesConfig:
    unit -> CancellableTask<DecodedTemplateConfiguration>

  abstract ResolveDescriptionsFile: unit -> CancellableTask<Map<string, string>>

  abstract ResolvePluginPaths: unit -> (string * string)[]

  abstract ResolveEsbuildPath: unit -> string<SystemPath>

  abstract ResolveLiveReloadScript: unit -> CancellableTask<string>
  abstract ResolveWorkerScript: unit -> CancellableTask<string>
  abstract ResolveTestingHelpersScript: unit -> CancellableTask<string>
  abstract ResolveMochaRunnerScript: unit -> CancellableTask<string>

  abstract SaveImportMap:
    map: Perla.PkgManager.ImportMap -> CancellableTask<unit>

  abstract SavePerlaConfig: config: PerlaConfig -> CancellableTask<unit>

  abstract SavePerlaConfig:
    updates: Perla.Json.PerlaConfig.PerlaWritableField seq ->
      CancellableTask<unit>

  abstract SetupEsbuild: string<Semver> -> CancellableTask<unit>

  abstract SetupFable: unit -> CancellableTask<unit>

  abstract SetupTemplate:
    user: string * repository: string<Repository> * branch: string<Branch> ->
      CancellableTask<(string<SystemPath> * DecodedTemplateConfiguration) option>

  abstract CopyGlobs:
    buildConfig: BuildConfig * tempDir: string<SystemPath> -> unit

  abstract EmitEnvFile:
    config: PerlaConfig * ?tmpPath: string<SystemPath> -> unit


[<RequireQualifiedAccess>]
module FileSystem =

  val GetDirectories: unit -> PerlaDirectories

  val GetManager:
    logger: ILogger * env: PlatformOps * dirs: PerlaDirectories ->
      PerlaFsManager
