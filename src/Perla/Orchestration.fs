namespace Perla


open Microsoft.Extensions.Logging
open IcedTasks
open FSharp.UMX

open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open Perla
open Perla.Types
open Perla.Database
open Perla.FileSystem
open Perla.Fable
open Perla.Esbuild
open Perla.Extensibility
open Perla.VirtualFs
open Perla.Scaffolding
open Perla.Configuration
open Perla.PkgManager


module Warmup =
  type RecoverableAssets =
    | Esbuild
    | Templates
    | Fable

  type MiddlewareResult =
    | Continue
    | Recover of RecoverableAssets Set
    | HardExit

  [<RequireQualifiedAccess>]
  module Check =

    let EsbuildPlugin(config: PerlaConfig aval, logger: ILogger) =
      let plugins = config |> AVal.map(fun c -> c.plugins) |> AVal.force

      if plugins |> Seq.contains Constants.PerlaEsbuildPluginName then
        Continue
      else
        logger.LogWarning
          "The Perla esbuild plugin is not installed, this may cause issues with your build."

        Recover(set [ Esbuild ])

    let Setup
      (
        logger: ILogger,
        db: PerlaDatabase,
        config: PerlaConfig aval,
        fable: FableService
      ) =
      cancellableTask {
        let templates = db.Checks.AreTemplatesPresent()

        let esbuild =
          db.Checks.IsEsbuildBinPresent(
            config |> AVal.force |> _.esbuild.version
          )

        let needsFable = config |> AVal.force |> _.fable |> Option.isSome

        let! fable =
          // if there's no fable in the config, we don't need to check for it
          if not needsFable then
            CancellableTask.singleton true
          else
            fable.IsPresent()

        let missingAssets = [
          if not templates then
            Templates

          if not esbuild then
            Esbuild

          if not fable then
            Fable
        ]

        if Seq.isEmpty missingAssets then
          return Continue
        else
          logger.LogWarning
            "Some required assets are missing: {missingAssets}. Attempting to recover."

          return Recover(set missingAssets)
      }

    let Templates(db: PerlaDatabase, logger: ILogger) =
      db.Checks.AreTemplatesPresent()
      |> function
        | true -> Continue
        | false ->
          logger.LogWarning
            "The Perla templates are not installed, this may cause issues with your build."

          Recover(set [ Templates ])

    let Fable(fable: FableService, logger: ILogger) = cancellableTask {
      let! isPresent = fable.IsPresent()

      if isPresent then
        return Continue
      else
        logger.LogWarning
          "The Fable compiler is not installed, this may cause issues with your build."

        return Recover(set [ Fable ])
    }


  module Recover =

    type SetupFailure =
      | EsbuildFailed of string
      | TemplatesFailed
      | FableFailed
      | HardExitRequested

    let esbuildSetup
      (
        config: PerlaConfig aval,
        db: PerlaDatabase,
        pfsm: PerlaFsManager,
        logger: ILogger
      ) =
      cancellableTaskResult {
        let! token = CancellableTaskResult.getCancellationToken()
        let version = config |> AVal.force |> _.esbuild.version

        logger.LogInformation("Installing esbuild: {version}...", version)

        try
          do!
            pfsm.SetupEsbuild (config |> AVal.force |> _.esbuild.version) token

          db.Checks.SaveEsbuildBinPresent(version) |> ignore
          logger.LogInformation("Successfully installed esbuild.")
          return! Ok()
        with ex ->
          logger.LogError("Failed to install esbuild, please try again.", ex)

          logger.LogError
            "If this keeps happening please report this issue on the Perla GitHub repository."
          // If we fail to install esbuild we can't continue
          return! Error(EsbuildFailed(UMX.untag version))
      }

    let templatesSetup
      (db: PerlaDatabase, pfsm: PerlaFsManager, logger: ILogger)
      =
      cancellableTaskResult {
        let! token = CancellableTaskResult.getCancellationToken()

        logger.LogInformation "Installing templates..."

        let user, repo, branch =
          (parseFullRepositoryName(Some Constants.Default_Templates_Repository))
            .Value

        let! values =
          pfsm.SetupTemplate (user, UMX.tag repo, UMX.tag branch) token

        match values with
        | None ->
          logger.LogError
            "Failed to install templates, please try again, if this keeps happening please report this issue."
          // If we fail to install templates we can't continue
          return! Error TemplatesFailed
        | Some(targetPath, decoded) ->
          logger.LogInformation "Successfully installed templates."

          db.Templates.Add(
            targetPath,
            decoded,
            user,
            UMX.tag repo,
            UMX.tag branch
          )
          |> ignore

          db.Checks.SaveTemplatesPresent() |> ignore
          logger.LogInformation "Templates saved to database."
          return! Ok()
      }

    let fableSetup(pfsm: PerlaFsManager, logger: ILogger) = cancellableTaskResult {
      let! token = CancellableTaskResult.getCancellationToken()

      logger.LogInformation "Installing fable..."

      try
        do! pfsm.SetupFable () token
        logger.LogInformation "Successfully installed fable."
        return! Ok()
      with ex ->
        logger.LogError("Failed to install fable, please try again.", ex)

        logger.LogError
          "If this keeps happening please report this issue on the Perla GitHub repository."
        // If we fail to install fable we can't continue
        return! Error FableFailed
    }

    let From
      (
        config: PerlaConfig aval,
        db: PerlaDatabase,
        pfsm: PerlaFsManager,
        logger: ILogger
      )
      (result: MiddlewareResult)
      =
      cancellableTaskResult {
        let! token = CancellableTaskResult.getCancellationToken()

        match result with
        | Continue -> return ()
        | HardExit ->
          logger.LogError "Setup failed, exiting."
          return! Error(HardExitRequested)
        | Recover recoverFrom ->
          logger.LogInformation "Recovering from missing assets: {recoverFrom}."

          let! result =
            Spectre.Console.AnsiConsole.ConfirmAsync(
              "Some required assets are missing. Do you want to install them?",
              true
            )

          if not result then
            logger.LogWarning
              "You chose not to recover from missing assets, this may cause issues with some of your commands."

            return ()
          else
            logger.LogInformation
              "Starting setup for missing assets: {recoverFrom}."

            let! _ =
              recoverFrom
              |> Seq.traverseTaskResultM(fun asset -> taskResult {
                match asset with
                | Esbuild ->
                  return! esbuildSetup (config, db, pfsm, logger) token
                | Templates -> return! templatesSetup (db, pfsm, logger) token
                | Fable -> return! fableSetup (pfsm, logger) token
              })

            return ()
      }

type HasLogger =
  abstract member Logger: ILogger

type HasPlatformOps =
  abstract member PlatformOps: PlatformOps

type HasDirectories =
  abstract member Directories: PerlaDirectories

type HasFsManager =
  abstract member FsManager: PerlaFsManager

type HasDatabase =
  abstract member Db: PerlaDatabase

type HasEsbuildService =
  abstract member EsbuildService: EsbuildService

type HasExtensibilityService =
  abstract member ExtensibilityService: ExtensibilityService

type HasVirtualFileSystem =
  abstract member VirtualFileSystem: VirtualFileSystem

type HasFableService =
  abstract member FableService: FableService

type HasTemplateService =
  abstract member TemplateService: TemplateService

type HasConfiguration =
  abstract member Configuration: ConfigurationManager

type HasPkgManager =
  abstract member PkgManager: PkgManager

[<Interface>]
type AppContainer =
  inherit HasLogger
  inherit HasPlatformOps
  inherit HasDirectories
  inherit HasFsManager
  inherit HasDatabase
  inherit HasEsbuildService
  inherit HasExtensibilityService
  inherit HasVirtualFileSystem
  inherit HasFableService
  inherit HasTemplateService
  inherit HasConfiguration
  inherit HasPkgManager

type AppContainerArgs = {
  Logger: ILogger
  Directories: PerlaDirectories
  FsManager: PerlaFsManager
  Platform: PlatformOps
}

module AppContainer =


  let Create(args: AppContainerArgs) : AppContainer =

    let {
          Logger = logger
          Directories = directories
          FsManager = fsManager
          Platform = platformOps
        } =
      args

    let fsManager = FileSystem.GetManager(logger, platformOps, directories)

    let database =
      let getConnection() : LiteDB.ILiteDatabase =
        let db = directories.Database
        new LiteDB.LiteDatabase $"Filename={UMX.untag db}; Connection=direct"

      Database.Create {
        Logger = logger
        Directories = directories
        GetConnection = getConnection
      }

    let esbuildService =
      Esbuild.Create {
        Cwd = directories.CurrentWorkingDirectory
        PerlaFsManager = fsManager
        Logger = logger
      }

    let extensibilityService = ExtensibilityService.Create logger

    let virtualFileSystem =
      VirtualFs.Create {
        Extensibility = extensibilityService
        Logger = logger
      }

    let fableService =
      Fable.Create {
        Platform = platformOps
        Logger = logger
      }

    let templateService =
      Scaffolding.Create {
        PerlaFsManager = fsManager
        Database = database
      }

    let configurationManager =
      ConfigurationManager(fsManager.PerlaConfiguration)


    let pkgManager =
      let jspmService = RequestHandler.JspmService.create()

      let pkgManagerConfig =
        let appData =
          System.Environment.GetFolderPath
            System.Environment.SpecialFolder.ApplicationData

        {
          GlobalCachePath = System.IO.Path.Combine(appData, "perla", "packages")
          cwd = UMX.untag directories.CurrentWorkingDirectory
        }

      PkgManager.create {
        logger = logger
        reqHandler = jspmService
        config = pkgManagerConfig
      }

    { new AppContainer with
        member _.Logger = logger
        member _.PlatformOps = platformOps
        member _.Directories = directories
        member _.FsManager = fsManager
        member _.Db = database
        member _.EsbuildService = esbuildService
        member _.ExtensibilityService = extensibilityService
        member _.VirtualFileSystem = virtualFileSystem
        member _.FableService = fableService
        member _.TemplateService = templateService
        member _.Configuration = configurationManager
        member _.PkgManager = pkgManager
    }

[<AutoOpen>]
module Patterns =

  let inline (|Logger|)(container: #HasLogger) = container.Logger
  let inline (|PlatformOps|)(container: #HasPlatformOps) = container.PlatformOps
  let inline (|Directories|)(container: #HasDirectories) = container.Directories
  let inline (|FsManager|)(container: #HasFsManager) = container.FsManager
  let inline (|Database|)(container: #HasDatabase) = container.Db

  let inline (|EsbuildService|)(container: #HasEsbuildService) =
    container.EsbuildService

  let inline (|ExtensibilityService|)(container: #HasExtensibilityService) =
    container.ExtensibilityService

  let inline (|VirtualFileSystem|)(container: #HasVirtualFileSystem) =
    container.VirtualFileSystem

  let inline (|FableService|)(container: #HasFableService) =
    container.FableService

  let inline (|TemplateService|)(container: #HasTemplateService) =
    container.TemplateService

  let inline (|Configuration|)(container: #HasConfiguration) =
    container.Configuration

  let inline (|PkgManager|)(container: #HasPkgManager) = container.PkgManager
