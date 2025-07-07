namespace Perla.Warmup


open Microsoft.Extensions.Logging
open IcedTasks
open FSharp.UMX

open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open Perla
open Perla.Types
open Perla.Database
open Perla.FileSystem

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
      Ok()
    else
      logger.LogWarning
        "The Perla esbuild plugin is not installed, this may cause issues with your build."

      Error(Recover(set [ Esbuild ]))

  let Setup
    (db: PerlaDatabase, config: PerlaConfig aval, fable: Fable.FableService)
    =
    cancellableTaskResult {
      let templates = db.Checks.AreTemplatesPresent()

      let esbuild =
        db.Checks.IsEsbuildBinPresent(config |> AVal.force |> _.esbuild.version)

      let! fable = fable.IsPresent()

      let errors = [
        if not templates then
          Templates
        if not esbuild then
          Esbuild
        if not fable then
          Fable
      ]

      if Seq.isEmpty errors then
        return ()
      else
        return! Error(Recover(set errors))
    }

  let Templates(db: PerlaDatabase, logger: ILogger) =
    db.Checks.AreTemplatesPresent()
    |> function
      | true -> Ok()
      | false ->
        logger.LogWarning
          "The Perla templates are not installed, this may cause issues with your build."

        Error(Recover(set [ Templates ]))

  let Fable(fable: Fable.FableService, logger: ILogger) = cancellableTask {
    let! isPresent = fable.IsPresent()

    if isPresent then
      return Ok()
    else
      logger.LogWarning
        "The Fable compiler is not installed, this may cause issues with your build."

      return Error(Recover(set [ Fable ]))
  }


module Recover =
  let From
    (
      config: PerlaConfig aval,
      db: PerlaDatabase,
      pfsm: PerlaFsManager,
      logger: ILogger
    )
    (recoverFrom: RecoverableAssets seq)
    =
    cancellableTaskResult {
      let! token = CancellableTaskResult.getCancellationToken()

      let! _ =
        recoverFrom
        |> Seq.traverseTaskResultM(fun asset -> taskResult {
          match asset with
          | Esbuild ->
            let version = config |> AVal.force |> _.esbuild.version
            logger.LogInformation("Installing esbuild: {version}...", version)

            try
              do!
                pfsm.SetupEsbuild
                  (config |> AVal.force |> _.esbuild.version)
                  token

              db.Checks.SaveEsbuildBinPresent(version) |> ignore
              logger.LogInformation("Successfully installed esbuild.")
              return! Ok()
            with ex ->
              logger.LogError(
                "Failed to install esbuild, please try again.",
                ex
              )

              logger.LogError
                "If this keeps happening please report this issue on the Perla GitHub repository."
              // If we fail to install esbuild we can't continue
              return! Error HardExit
          | Templates ->
            logger.LogInformation "Installing templates..."

            let user, repo, branch =
              (parseFullRepositoryName(
                Some Constants.Default_Templates_Repository
              ))
                .Value

            let! values =
              pfsm.SetupTemplate (user, UMX.tag repo, UMX.tag branch) token

            match values with
            | None ->
              logger.LogError
                "Failed to install templates, please try again, if this keeps happening please report this issue."
              // If we fail to install templates we can't continue
              return! Error HardExit
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
          | Fable ->
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
              return! Error HardExit
        })

      return! Ok()
    }
