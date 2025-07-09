namespace Perla

open System
open System.IO
open FSharp.UMX
open FSharp.Compiler.Interactive.Shell
open IcedTasks
open LiteDB
open Perla.FileSystem
open Perla.Database
open Perla.Units

// Internal FSI session management - not exposed in signature
module FsiSession =
  type Fsi =
    static member GetSession(?argv: string seq, ?stdout, ?stderr) =
      let defaultArgv = [|
        "fsi.exe"
        "--optimize+"
        "--nologo"
        "--gui-"
        "--readline-"
      |]

      let argv =
        match argv with
        | Some argv -> [| yield! defaultArgv; yield! argv |]
        | None -> defaultArgv

      let stdout = defaultArg stdout Console.Out
      let stderr = defaultArg stderr Console.Error

      let config = FsiEvaluationSession.GetDefaultConfiguration()

      FsiEvaluationSession.Create(
        config,
        argv,
        new StringReader(""),
        stdout,
        stderr,
        true
      )

  [<Literal>]
  let ScaffoldConfiguration = "TemplateConfiguration"

  let getConfigurationFromScript content =
    use session = Fsi.GetSession()

    session.EvalInteractionNonThrowing(content) |> ignore

    match session.TryFindBoundValue ScaffoldConfiguration with
    | Some bound ->
      match bound.Value.ReflectionValue with
      | null -> None
      | bound -> Some bound
    | None -> None

module Scaffolding =
  open Units

  [<Literal>]
  let ScaffoldConfiguration = "TemplateConfiguration"

  [<RequireQualifiedAccess; Struct>]
  type TemplateScriptKind =
    | Template of template: TemplateItem
    | Repository of repository: PerlaTemplateRepository

  let readTemplateScriptContents(path: string) = cancellableTask {
    try
      let! token = CancellableTask.getCancellationToken()

      let! content =
        File.ReadAllTextAsync(
          Path.Combine(path, Constants.TemplatingScriptName),
          token
        )

      return Some content
    with _ ->
      return None
  }

  /// <summary>
  /// Template Management Service that coordinates between PerlaFsManager and Database
  /// </summary>
  [<Interface>]
  type TemplateService =
    abstract ListRepositories: unit -> PerlaTemplateRepository list
    abstract ListTemplateItems: unit -> TemplateItem list

    abstract Add:
      user: string * repository: string<Repository> * branch: string<Branch> ->
        CancellableTask<LiteDB.ObjectId>

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

  let Create
    ({
       Database = database
       PerlaFsManager = fsManager
     }: TemplateServiceArgs)
    =
    { new TemplateService with
        member _.ListRepositories() = database.Templates.ListRepositories()

        member _.ListTemplateItems() = database.Templates.ListTemplateItems()

        member _.Add(user, repository, branch) = cancellableTask {
          match! fsManager.SetupTemplate(user, repository, branch) with
          | Some(path, config) ->
            return
              database.Templates.Add(path, config, user, repository, branch)
          | None ->
            return
              failwith
                $"Failed to setup template {user}/{UMX.untag repository}@{UMX.untag branch}"
        }

        member _.FindOne(name) = database.Templates.FindOne(name)

        member _.FindTemplateItems(searchParams) =
          database.Templates.FindTemplateItems(searchParams)

        member _.Update(template) = cancellableTask {
          let user = template.Username
          let repository = UMX.tag<Repository> template.Repository
          let branch = UMX.tag<Branch> template.Branch

          match! fsManager.SetupTemplate(user, repository, branch) with
          | Some(path, config) ->
            // Update template properties
            template.Path <- UMX.untag path
            template.Name <- config.name

            template.Description <-
              config.description
              |> Option.defaultValue "No description provided"

            template.Author <-
              config.author |> Option.defaultValue "No author provided"

            template.License <-
              config.license |> Option.defaultValue "No license provided"

            template.RepositoryUrl <-
              config.repositoryUrl
              |> Option.defaultValue
                $"https://github.com/{user}/{template.Repository}/tree/{template.Branch}"

            template.Group <- config.group
            template.UpdatedAt <- Nullable(DateTime.Now)

            return database.Templates.Update(template)
          | None -> return false
        }

        member _.Delete(searchKind) = database.Templates.Delete(searchKind)

        member _.GetTemplateScriptContent(scriptKind) = cancellableTask {
          let! content =
            match scriptKind with
            | TemplateScriptKind.Template template ->
              readTemplateScriptContents(UMX.untag template.FullPath)
            | TemplateScriptKind.Repository repository ->
              readTemplateScriptContents(UMX.untag repository.Path)

          return
            content
            |> Option.map FsiSession.getConfigurationFromScript
            |> Option.flatten
        }
    }
