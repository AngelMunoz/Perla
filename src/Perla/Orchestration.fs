namespace Perla


open Microsoft.Extensions.Logging
open IcedTasks
open FSharp.UMX

open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open Perla
open Perla.Types
open Perla.RequestHandler
open Perla.FileSystem
open Perla.Database
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
    | Playwright

  type MiddlewareResult =
    | Continue
    | Recover of RecoverableAssets Set
    | HardExit

  [<RequireQualifiedAccess>]
  module Check =

    let Setup
      (
        logger: ILogger,
        db: PerlaDatabase,
        config: PerlaConfig aval,
        fable: FableService,
        directories: PerlaDirectories,
        requiredAssets: RecoverableAssets seq
      ) =
      cancellableTask {
        let requiredAssetsSet = set requiredAssets

        let! missingAssets = cancellableTask {
          let missing = ResizeArray<RecoverableAssets>()

          if Set.contains Templates requiredAssetsSet then
            let templates = db.Checks.AreTemplatesPresent()

            if not templates then
              missing.Add(Templates)

          if Set.contains Esbuild requiredAssetsSet then
            let esbuild =
              db.Checks.IsEsbuildBinPresent(
                config |> AVal.force |> _.esbuild.version
              )

            if not esbuild then
              missing.Add(Esbuild)

          if Set.contains Fable requiredAssetsSet then
            let needsFable = config |> AVal.force |> _.fable |> Option.isSome

            let! fablePresent =
              // if there's no fable in the config, we don't need to check for it
              if not needsFable then
                CancellableTask.singleton true
              else
                fable.IsPresent()

            if not fablePresent then
              missing.Add(Fable)

          if Set.contains Playwright requiredAssetsSet then
            let playwrightArtifactsRoot =
              System.IO.DirectoryInfo(
                UMX.untag directories.PlaywrightArtifactsRoot
              )

            let playwrightPresent = db.Checks.IsPlaywrightPresent()
            let nextUpdate = db.Checks.NextPlaywrightUpdate()

            match nextUpdate with
            | None -> ()
            | Some nextUpdate ->
              if System.DateTime.UtcNow >= nextUpdate then
                logger.LogInformation
                  "Playwright browsers might be outdated, consider updating playwright."

            if not playwrightPresent || not playwrightArtifactsRoot.Exists then
              missing.Add Playwright

          return missing |> Seq.toList
        }

        if Seq.isEmpty missingAssets then
          return Continue
        else
          logger.LogWarning(
            "Some required assets are missing: {missingAssets}. Attempting to recover.",
            missingAssets
          )

          return Recover(set missingAssets)
      }

  module Recover =

    type SetupFailure =
      | EsbuildFailed of string
      | TemplatesFailed
      | FableFailed
      | PlaywrightFailed
      | HardExitRequested

    type RecoverArgs = {
      config: PerlaConfig aval
      db: PerlaDatabase
      pfsm: PerlaFsManager
      logger: ILogger
      skipPrompts: bool
      ci: bool
    }

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

        match Some Constants.Default_Templates_Repository with
        | FullRepositoryName(user, repo, branch) ->
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
        | _ ->
          logger.LogError "Failed to parse default templates repository."
          return! Error TemplatesFailed
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

    let From (args: RecoverArgs) (result: MiddlewareResult) = cancellableTaskResult {
      let! token = CancellableTaskResult.getCancellationToken()

      match result with
      | Continue -> return ()
      | HardExit ->
        args.logger.LogError "Setup failed, exiting."
        return! Error(HardExitRequested)
      | Recover recoverFrom ->
        args.logger.LogInformation(
          "Recovering from missing assets: {recoverFrom}.",
          recoverFrom
        )

        let! shouldProceed =
          if args.ci || args.skipPrompts then
            CancellableTask.singleton true
          else
            cancellableTask {
              let! result =
                Spectre.Console.AnsiConsole.ConfirmAsync(
                  "Some required assets are missing. Do you want to install them?",
                  true
                )

              return result
            }

        if not shouldProceed then
          args.logger.LogWarning
            "You chose not to recover from missing assets, this may cause issues with some of your commands."

          return ()
        else
          args.logger.LogInformation(
            "Starting setup for missing assets: {recoverFrom}.",
            recoverFrom
          )

          let! _ =
            recoverFrom
            |> Seq.traverseTaskResultM(fun asset -> taskResult {
              match asset with
              | Esbuild ->
                return!
                  esbuildSetup
                    (args.config, args.db, args.pfsm, args.logger)
                    token
              | Templates ->
                return! templatesSetup (args.db, args.pfsm, args.logger) token
              | Fable -> return! fableSetup (args.pfsm, args.logger) token
              | Playwright ->
                return! task {
                  try
                    do! Testing.Testing.SetupPlaywright args.logger
                    args.db.Checks.SavePlaywrightPresent() |> ignore

                    args.db.Checks.SaveNextPlaywrightUpdate(
                      System.DateTime.UtcNow.AddDays 30.0
                    )
                    |> ignore

                    args.logger.LogInformation
                      "Successfully installed Playwright."

                    return Ok()
                  with ex ->
                    args.logger.LogError(
                      "Failed to install Playwright, please try again.",
                      ex
                    )

                    args.logger.LogError
                      "If this keeps happening please report this issue on the Perla GitHub repository."

                    return Error PlaywrightFailed
                }
            })

          return ()
    }

type HasLogger =
  abstract member Logger: ILogger

type HasPlatformOps =
  abstract member PlatformOps: PlatformOps

type HasDirectories =
  abstract member Directories: PerlaDirectories

type HasRequestHandler =
  abstract member RequestHandler: RequestHandler

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

type HasBuildService =
  abstract member BuildService: Perla.Build.BuildService

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
  inherit HasRequestHandler
  inherit HasBuildService

type AppContainerArgs = {
  Logger: ILogger
  Directories: PerlaDirectories
  FsManager: PerlaFsManager
  Platform: PlatformOps
  RequestHandler: RequestHandler
}

module AppContainer =


  let Create(args: AppContainerArgs) : AppContainer =

    let {
          Logger = logger
          Directories = directories
          FsManager = fsManager
          Platform = platformOps
          RequestHandler = requestHandler
        } =
      args

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
        PlatformOps = platformOps
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
      Create {
        PerlaFsManager = fsManager
        Database = database
      }

    let configurationManager =
      ConfigurationManager(fsManager.PerlaConfiguration)


    let pkgManager =
      let jspmService =
        RequestHandler.JspmService.create(Json.DefaultJsonOptions())

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
        member _.RequestHandler = requestHandler

        member _.BuildService =
          Perla.Build.BuildService.Create {
            Logger = logger
            FsManager = fsManager
            EsbuildService = esbuildService
            ExtensibilityService = extensibilityService
            VirtualFileSystem = virtualFileSystem
            FableService = fableService
            Directories = directories
          }
    }

[<AutoOpen>]
module Patterns =

  let inline (|Logger|)(container: #HasLogger) = container.Logger
  let inline (|PlatformOps|)(container: #HasPlatformOps) = container.PlatformOps
  let inline (|Directories|)(container: #HasDirectories) = container.Directories
  let inline (|FsManager|)(container: #HasFsManager) = container.FsManager
  let inline (|Database|)(container: #HasDatabase) = container.Db

  let inline (|RequestHandler|)(container: #HasRequestHandler) =
    container.RequestHandler

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
