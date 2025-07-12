module Perla.Database

#nowarn "3391"

open System
open System.Linq.Expressions
open LiteDB

open FSharp.UMX
open FsToolkit.ErrorHandling

open Perla
open Perla.Units
open Microsoft.Extensions.Logging
open Perla.Json

[<Class; Sealed>]
type PerlaCheck() =

  [<BsonId>]
  member val CheckId: ObjectId = ObjectId.NewObjectId() with get, set

  member val Name: string = String.Empty with get, set

  member val IsDone: bool = false with get, set

  member val CreatedAt: DateTime = DateTime.Now with get, set

  member val UpdatedAt: Nullable<DateTime> = Nullable() with get, set

[<RequireQualifiedAccess>]
module PerlaCheck =

  [<Literal>]
  let SetupCheckName = "SetupCheck"

  [<Literal>]
  let EsbuildCheckPrefix = "Esbuild:Version:"

  [<Literal>]
  let TemplatesCheckName = "TemplatesCheck"

[<Class; Sealed>]
type TemplateConfigurationItem() =

  [<BsonId>]
  member val ChildId: ObjectId = Unchecked.defaultof<_> with get, set

  member val Name: string = String.Empty with get, set
  member val ShortName: string = String.Empty with get, set
  member val Description: string = String.Empty with get, set


[<Class; Sealed>]
type PerlaTemplateRepository() =

  [<BsonId>]
  member val _id: ObjectId = Unchecked.defaultof<_> with get, set

  member val Username: string = String.Empty with get, set
  member val Repository: string = String.Empty with get, set
  member val Branch: string = String.Empty with get, set
  member val Path: string = String.Empty with get, set
  member val Name: string = String.Empty with get, set
  member val Description: string = String.Empty with get, set
  member val Author: string = String.Empty with get, set
  member val License: string = String.Empty with get, set
  member val RepositoryUrl: string = String.Empty with get, set
  member val Group: string = String.Empty with get, set

  member val Templates: TemplateConfigurationItem seq = Seq.empty with get, set

  member val CreatedAt: DateTime = DateTime.Now with get, set

  member val UpdatedAt: Nullable<DateTime> = Nullable() with get, set

  member this.ToFullName() : string = $"{this.Username}/{this.Repository}"

  member this.ToFullNameWithBranch() : string =
    $"{this.Username}/{this.Repository}/{this.Branch}"

[<Class; Sealed>]
type TemplateItem() =

  [<BsonId>]
  member val _id: ObjectId = Unchecked.defaultof<_> with get, set

  member val Id: string = String.Empty with get, set
  member val Parent: ObjectId = Unchecked.defaultof<_> with get, set
  member val Name: string = String.Empty with get, set
  member val Group: string = String.Empty with get, set
  member val ShortName: string = String.Empty with get, set
  member val Description: string option = None with get, set
  member val FullPath: string = String.Empty with get, set

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

  let inline getCollection<'Type>(database: ILiteDatabase) =
    let collection = database.GetCollection<'Type>()

    // Ensure indexes based on type
    match typeof<'Type> with
    | t when t = typeof<PerlaCheck> ->
      let checks = collection :?> ILiteCollection<PerlaCheck>
      checks.EnsureIndex(fun check -> check.Name, true) |> ignore
      checks.EnsureIndex(fun check -> check.IsDone) |> ignore
    | t when t = typeof<PerlaTemplateRepository> ->
      let repositories = collection :?> ILiteCollection<PerlaTemplateRepository>
      repositories.EnsureIndex(fun template -> template.Username) |> ignore
      repositories.EnsureIndex(fun template -> template.Repository) |> ignore
      repositories.EnsureIndex(fun template -> template.Group) |> ignore
    | t when t = typeof<TemplateItem> ->
      let templates = collection :?> ILiteCollection<TemplateItem>
      templates.EnsureIndex(fun template -> template.Parent) |> ignore
      templates.EnsureIndex(fun template -> template.Group) |> ignore
      templates.EnsureIndex(fun template -> template.Name) |> ignore
      templates.EnsureIndex(fun template -> template.ShortName) |> ignore
    | _ -> ()

    collection

  let buildTemplateItems
    (config: DecodedTemplateConfiguration)
    (repositoryId: ObjectId)
    (basePath: string)
    =
    config.templates
    |> Seq.map(fun templateItem ->
      TemplateItem(
        _id = ObjectId.NewObjectId(),
        Id = templateItem.id,
        Parent = repositoryId,
        Name = templateItem.name,
        Group = $"{config.group}.{templateItem.id}",
        ShortName = templateItem.shortName,
        Description = templateItem.description,
        FullPath =
          System.IO.Path.Combine(basePath, UMX.untag templateItem.path)
      ))

  let buildTemplateConfigurationItems(templateItems: TemplateItem seq) =
    templateItems
    |> Seq.map(fun item ->
      let description =
        item.Description |> Option.defaultValue "No Description Provided"

      TemplateConfigurationItem(
        ChildId = item._id,
        Name = item.Name,
        ShortName = item.ShortName,
        Description = description
      ))

  let findRepositoryBySearchKind
    (repositories: ILiteCollection<PerlaTemplateRepository>)
    (searchKind: TemplateSearchKind)
    =
    match searchKind with
    | TemplateSearchKind.Id id -> repositories.FindById(id) |> Option.ofNull
    | TemplateSearchKind.Username username ->
      repositories
        .Query()
        .Where(fun repo ->
          repo.Username.Equals(
            username,
            StringComparison.InvariantCultureIgnoreCase
          ))
        .SingleOrDefault()
      |> Option.ofNull
    | TemplateSearchKind.Repository repository ->
      repositories
        .Query()
        .Where(fun repo ->
          repo.Repository.Equals(
            repository,
            StringComparison.InvariantCultureIgnoreCase
          ))
        .SingleOrDefault()
      |> Option.ofNull
    | TemplateSearchKind.FullName(username, repository) ->
      repositories
        .Query()
        .Where(fun repo ->
          repo.Username.Equals(
            username,
            StringComparison.InvariantCultureIgnoreCase
          )
          && repo.Repository.Equals(
            repository,
            StringComparison.InvariantCultureIgnoreCase
          ))
        .SingleOrDefault()
      |> Option.ofNull

  let getChecks(args: PerlaDatabaseArgs) =
    { new CheckRepository with
        member _.AreTemplatesPresent() : bool =
          use db = args.GetConnection()
          let checks = getCollection<PerlaCheck> db
          let checkName = PerlaCheck.TemplatesCheckName
          checks.Exists(fun check -> check.Name = checkName && check.IsDone)

        member _.IsEsbuildBinPresent(version: string<Semver>) : bool =
          use db = args.GetConnection()
          let checks = getCollection<PerlaCheck> db
          let checkName = $"{PerlaCheck.EsbuildCheckPrefix}{version}"
          checks.Exists(fun check -> check.Name = checkName && check.IsDone)

        member _.IsSetupPresent() : bool =
          use db = args.GetConnection()
          let checks = getCollection<PerlaCheck> db
          let checkName = PerlaCheck.SetupCheckName
          checks.Exists(fun check -> check.Name = checkName && check.IsDone)

        member _.SaveEsbuildBinPresent(version: string<Semver>) : ObjectId =
          use db = args.GetConnection()
          let checks = getCollection<PerlaCheck> db
          let checkName = $"{PerlaCheck.EsbuildCheckPrefix}{version}"

          match
            checks.FindOne(fun check -> check.Name = checkName && check.IsDone)
            |> Option.ofNull
          with
          | Some found -> found.CheckId
          | None ->
            let check = PerlaCheck(Name = checkName, IsDone = true)
            checks.Insert(check)

        member _.SaveSetup() : ObjectId =
          use db = args.GetConnection()
          let checks = getCollection<PerlaCheck> db

          match
            checks.FindOne(fun check -> check.Name = PerlaCheck.SetupCheckName)
            |> Option.ofNull
          with
          | Some found -> found.CheckId
          | None ->
            let check =
              PerlaCheck(Name = PerlaCheck.SetupCheckName, IsDone = true)

            checks.Insert(check)

        member _.SaveTemplatesPresent() : ObjectId =
          use db = args.GetConnection()
          let checks = getCollection<PerlaCheck> db
          let checkName = PerlaCheck.TemplatesCheckName

          match
            checks.FindOne(fun check -> check.Name = checkName) |> Option.ofNull
          with
          | Some found -> found.CheckId
          | None ->
            let check = PerlaCheck(Name = checkName, IsDone = true)
            checks.Insert(check)
    }

  let getTemplates(args: PerlaDatabaseArgs) =
    { new TemplateRepository with
        member _.Add(path, config, username, repository, branch) =
          use db = args.GetConnection()
          let repositories = getCollection<PerlaTemplateRepository> db
          let templates = getCollection<TemplateItem> db

          let repositoryId = ObjectId.NewObjectId()

          // Build template items from config
          let templateItems =
            buildTemplateItems config repositoryId (UMX.untag path)

          // Build template configuration items
          let templateConfigItems =
            buildTemplateConfigurationItems templateItems

          // Create the repository
          let repoUrl =
            config.repositoryUrl
            |> Option.defaultValue
              $"https://github.com/{username}/{repository}/tree/{branch}"

          let description =
            config.description |> Option.defaultValue "No description provided"

          let author = config.author |> Option.defaultValue "No author provided"

          let license =
            config.license |> Option.defaultValue "No license provided"

          let repository =
            PerlaTemplateRepository(
              _id = repositoryId,
              Username = username,
              Repository = UMX.untag repository,
              Branch = UMX.untag branch,
              Path = UMX.untag path,
              Name = config.name,
              RepositoryUrl = repoUrl,
              Group = config.group,
              Templates = templateConfigItems,
              CreatedAt = DateTime.Now,
              UpdatedAt = Nullable(),
              Description = description,
              Author = author,
              License = license
            )

          // Insert repository and templates
          repositories.Insert(repository: PerlaTemplateRepository) |> ignore
          templates.InsertBulk templateItems |> ignore

          repositoryId

        member _.Delete(searchKind: TemplateSearchKind) : bool =
          use db = args.GetConnection()
          let repositories = getCollection<PerlaTemplateRepository> db
          let templates = getCollection<TemplateItem> db

          let repositoryToDelete =
            findRepositoryBySearchKind repositories searchKind

          match repositoryToDelete with
          | Some repo ->
            // Delete associated templates
            templates.DeleteMany(fun template -> template.Parent = repo._id)
            |> ignore
            // Delete repository
            repositories.Delete repo._id
          | None -> false

        member _.FindOne
          (name: TemplateSearchKind)
          : PerlaTemplateRepository option =
          use db = args.GetConnection()
          let repositories = getCollection<PerlaTemplateRepository> db
          findRepositoryBySearchKind repositories name

        member _.FindTemplateItems
          (searchParams: QuickAccessSearch)
          : TemplateItem list =
          use db = args.GetConnection()
          let templates = getCollection<TemplateItem> db

          let results =
            match searchParams with
            | QuickAccessSearch.Id id ->
              templates.FindById(id)
              |> Option.ofNull
              |> Option.map List.singleton
              |> Option.defaultValue []
            | QuickAccessSearch.Name name ->
              templates
                .Query()
                .Where(fun template ->
                  template.Name.Equals(
                    name,
                    StringComparison.InvariantCultureIgnoreCase
                  ))
                .ToArray()
              |> Array.toList
            | QuickAccessSearch.Group group ->
              templates
                .Query()
                .Where(fun template ->
                  template.Group.Equals(
                    UMX.untag group,
                    StringComparison.InvariantCultureIgnoreCase
                  ))
                .ToArray()
              |> Array.toList
            | QuickAccessSearch.ShortName shortName ->
              templates
                .Query()
                .Where(fun template ->
                  template.ShortName.Equals(
                    shortName,
                    StringComparison.InvariantCultureIgnoreCase
                  ))
                .ToArray()
              |> Array.toList
            | QuickAccessSearch.Parent id ->
              templates
                .Query()
                .Where(fun template -> template.Parent = id)
                .ToArray()
              |> Array.toList

          results

        member _.ListRepositories() : PerlaTemplateRepository list =
          use db = args.GetConnection()
          let repositories = getCollection<PerlaTemplateRepository> db

          repositories.FindAll() |> Seq.toList

        member _.ListTemplateItems() : TemplateItem list =
          use db = args.GetConnection()
          let templates = getCollection<TemplateItem> db

          templates.FindAll() |> Seq.toList

        member _.Update(template: PerlaTemplateRepository) : bool =
          use db = args.GetConnection()
          let repositories = getCollection<PerlaTemplateRepository> db
          let templates = getCollection<TemplateItem> db

          // Update the UpdatedAt timestamp
          template.UpdatedAt <- Nullable(DateTime.Now)

          // Update the repository in the database
          let updateResult = repositories.Update(template)

          // If repository update was successful, update associated templates
          if updateResult then
            // Delete existing templates for this repository
            templates.DeleteMany(fun t -> t.Parent = template._id) |> ignore

            // Insert updated templates if any are provided
            if template.Templates |> Seq.isEmpty |> not then
              // Build template items from the configuration items
              let updatedTemplates =
                template.Templates
                |> Seq.map(fun configItem ->
                  TemplateItem(
                    _id = ObjectId.NewObjectId(),
                    Parent = template._id,
                    Name = configItem.Name,
                    Group = $"{template.Group}.{configItem.ShortName}",
                    ShortName = configItem.ShortName,
                    Description = Some configItem.Description,
                    FullPath =
                      System.IO.Path.Combine(template.Path, configItem.Name)
                  ))

              templates.InsertBulk(updatedTemplates) |> ignore

          updateResult
    }

  let Create(args: PerlaDatabaseArgs) =
    { new PerlaDatabase with
        member _.Checks = getChecks args
        member _.Templates = getTemplates args
    }
