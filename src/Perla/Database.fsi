module Perla.Database

open System

open Microsoft.Extensions.Logging

open LiteDB

open FSharp.UMX

open Perla.Units
open Perla.Json.TemplateDecoders
open Perla.FileSystem


[<Class; Sealed>]
type PerlaCheck =

  [<BsonId>]
  member CheckId: ObjectId

  member Name: string

  member IsDone: bool

  member CreatedAt: DateTime

  member UpdatedAt: Nullable<DateTime>

[<Class; Sealed>]
type TemplateConfigurationItem =

  [<BsonId>]
  member ChildId: ObjectId with get, set

  member Name: string with get, set
  member ShortName: string with get, set
  member Description: string with get, set


[<Class; Sealed>]
type PerlaTemplateRepository =

  [<BsonId>]
  member _id: ObjectId with get, set

  member Username: string with get, set
  member Repository: string with get, set
  member Branch: string with get, set
  member Path: string with get, set
  member Name: string with get, set
  member Description: string with get, set
  member Author: string with get, set
  member License: string with get, set
  member RepositoryUrl: string with get, set
  member Group: string with get, set
  member Templates: TemplateConfigurationItem seq with get, set
  member CreatedAt: DateTime with get, set
  member UpdatedAt: Nullable<DateTime> with get, set

  member ToFullName: unit -> string
  member ToFullNameWithBranch: unit -> string

[<Class; Sealed>]
type TemplateItem =

  [<BsonId>]
  member _id: ObjectId with get, set

  member Id: string with get, set
  member Parent: ObjectId with get, set
  member Name: string with get, set
  member Group: string with get, set
  member ShortName: string with get, set
  member Description: string option with get, set
  member FullPath: string with get, set

[<RequireQualifiedAccess>]
type TemplateSearchKind =
  | Id of ObjectId
  | Username of name: string
  | Repository of repository: string
  | FullName of username: string * repository: string

[<RequireQualifiedAccess>]
type QuickAccessSearch =
  | Id of ObjectId
  | Name of string
  | Group of string<TemplateGroup>
  | ShortName of string
  | Parent of ObjectId



[<RequireQualifiedAccess; Struct>]
type TemplateScriptKind =
  | Template of template: TemplateItem
  | Repository of repository: PerlaTemplateRepository


[<Interface>]
type CheckRepository =
  abstract IsSetupPresent: unit -> bool
  abstract SaveSetup: unit -> ObjectId

  abstract IsEsbuildBinPresent: version: string<Semver> -> bool
  abstract SaveEsbuildBinPresent: version: string<Semver> -> ObjectId

  abstract AreTemplatesPresent: unit -> bool
  abstract SaveTemplatesPresent: unit -> ObjectId

[<Interface>]
type TemplateRepository =
  abstract ListRepositories: unit -> PerlaTemplateRepository list
  abstract ListTemplateItems: unit -> TemplateItem list

  abstract Add:
    path: string<SystemPath> *
    config: DecodedTemplateConfiguration *
    username: string *
    repository: string<Repository> *
    branch: string<Branch> ->
      ObjectId

  abstract FindOne: name: TemplateSearchKind -> PerlaTemplateRepository option

  abstract FindTemplateItems:
    searchParams: QuickAccessSearch -> TemplateItem list

  abstract Update: template: PerlaTemplateRepository -> bool

  abstract Delete: searchKind: TemplateSearchKind -> bool

[<Interface>]
type PerlaDatabase =
  abstract Checks: CheckRepository
  abstract Templates: TemplateRepository

type PerlaDatabaseArgs = {
  Logger: ILogger
  Directories: PerlaDirectories
  GetConnection: unit -> ILiteDatabase
}


module Database =
  val Create: PerlaDatabaseArgs -> PerlaDatabase
