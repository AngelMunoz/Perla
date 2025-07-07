namespace Perla

open System
open System.Threading.Tasks
open LiteDB
open FSharp.UMX
open IcedTasks
open Perla.Types
open Perla.FileSystem
open Perla.Database

module Scaffolding =
  open Units

  [<Literal>]
  val ScaffoldConfiguration: string = "TemplateConfiguration"

  [<RequireQualifiedAccess; Struct>]
  type TemplateScriptKind =
    | Template of template: TemplateItem
    | Repository of repository: PerlaTemplateRepository

  /// <summary>
  /// Template Management Service that coordinates between PerlaFsManager and Database
  /// </summary>
  [<Interface>]
  type TemplateService =
    abstract ListRepositories: unit -> PerlaTemplateRepository list
    abstract ListTemplateItems: unit -> TemplateItem list

    abstract Add:
      user: string * repository: string<Repository> * branch: string<Branch> ->
        CancellableTask<ObjectId>

    abstract FindOne: name: TemplateSearchKind -> PerlaTemplateRepository option

    abstract FindTemplateItems:
      searchParams: QuickAccessSearch -> TemplateItem list

    abstract Update: template: PerlaTemplateRepository -> CancellableTask<bool>

    abstract Delete: searchKind: TemplateSearchKind -> bool

    abstract GetTemplateScriptContent:
      scriptKind: TemplateScriptKind -> CancellableTask<obj option>

  type TemplateServiceArgs = {
    PerlaFsManager: PerlaFsManager
    Database: PerlaDatabase
  }

  val Create: args: TemplateServiceArgs -> TemplateService
