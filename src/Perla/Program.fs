// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open Microsoft.Extensions.Logging
open FSharp.SystemCommandLine
open Perla
open Perla.RequestHandler
open Perla.FileSystem
open Perla.Commands
open Perla.Logger


module Env =

  let SetupAppContainer logLevel =
    let lf =
      let logLevel = defaultArg logLevel LogLevel.Information

      LoggerFactory.Create(fun builder ->
        builder.AddPerlaLogger(logLevel = logLevel).SetMinimumLevel(logLevel)
        |> ignore)

    let AppLogger = lf.CreateLogger("Perla")

    let platform = PlatformOps.Create AppLogger
    let directories = PerlaDirectories.Create platform

    try
      System.IO.DirectoryInfo($"{directories.PerlaArtifactsRoot}").Create()
    with _ ->
      ()

    directories.SetCwdToProject()


    let requestHandler =
      RequestHandler.Create {
        Logger = AppLogger
        PlatformOps = platform
        PerlaDirectories = directories
      }

    let pfsm =
      FileSystem.GetManager {
        Logger = AppLogger
        PlatformOps = platform
        PerlaDirectories = directories
        RequestHandler = requestHandler
      }
    // add it to the path
    pfsm.ResolveEsbuildPath() |> ignore

    AppContainer.Create {
      Logger = AppLogger
      Directories = directories
      FsManager = pfsm
      Platform = platform
      RequestHandler = requestHandler
    }

[<EntryPoint>]
let main argv =
  let logLevel =
    argv
    |> Array.tryPick(fun arg ->
      match arg with
      | "[log=debug]" -> Some LogLevel.Debug
      | "[log=warn]" -> Some LogLevel.Warning
      | "[log=error]" -> Some LogLevel.Error
      | "[log=trace]" -> Some LogLevel.Trace
      | "[log=none]" -> Some LogLevel.None
      | "[log=crit]" -> Some LogLevel.Critical
      | _ -> None)

  let appContainer = Env.SetupAppContainer logLevel

  rootCommand argv {
    description "The Perla Dev Server!"

    configure(fun cfg ->
      // don't replace leading @ strings e.g. @lit-labs/task
      cfg.ResponseFileTokenReplacer <- null
      cfg.RootCommand.TreatUnmatchedTokensAsErrors <- false)

    inputs Input.context
    helpActionAsync

    addCommands [
      Commands.NewProject appContainer
      Commands.Install appContainer
      Commands.AddPackage appContainer
      Commands.RemovePackage appContainer
      Commands.ListPackages appContainer
      Commands.Serve appContainer
      Commands.Build appContainer
      Commands.Test appContainer
      Commands.Template appContainer
      Commands.Describe appContainer
    ]
  }
  |> Async.AwaitTask
  |> Async.RunSynchronously
