namespace Perla

open System
open System.Threading.Tasks
open Perla.Types
open Perla.PackageManager.Types

[<Obsolete>]
module Dependencies =
  val Search: name: string * page: int -> Task<unit>
  val Show: name: string -> Task<unit>

[<Class>]
type Dependencies =
  static member Add:
    package: string * map: ImportMap -> Task<Result<ImportMap, string>>

  static member Restore: package: string -> Task<Result<ImportMap, string>>

  static member Restore:
    packages: seq<string> -> Task<Result<ImportMap, string>>

  static member GetMapAndDependencies:
    packages: seq<string> -> Task<Result<string seq * ImportMap, string>>

  static member GetMapAndDependencies:
    map: ImportMap -> Task<Result<string seq * ImportMap, string>>

  static member Remove:
    package: string * map: ImportMap -> Task<Result<ImportMap, string>>

  static member SwitchProvider:
    map: ImportMap -> Task<Result<ImportMap, string>>

  static member LocateDependenciesFromMapAndConfig:
    importMap: ImportMap * config: PerlaConfig ->
      (PkgDependency Set * PkgDependency Set)
