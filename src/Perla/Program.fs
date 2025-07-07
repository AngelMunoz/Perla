// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System.CommandLine
open FSharp.SystemCommandLine
open Perla

open Perla.Logger

module Env =
  open Microsoft.Extensions.Logging
  open Perla.FileSystem

  let SetupAppContainer() =
    let lf =
      LoggerFactory.Create(fun builder ->
        builder
          .AddPerlaLogger()
#if DEBUG
          .SetMinimumLevel(LogLevel.Debug)
#else
          .SetMinimumLevel(LogLevel.Information)
#endif
        |> ignore)

    let AppLogger = lf.CreateLogger("Perla")

    let directories = FileSystem.GetDirectories()

    directories.SetCwdToProject()

    let platform = PlatformOps.Create()
    let pfsm = FileSystem.GetManager(AppLogger, platform, directories)

    AppContainer.Create {
      Logger = AppLogger
      Directories = directories
      FsManager = pfsm
      Platform = platform
    }

[<EntryPoint>]
let main argv =

  let appContainer = Env.SetupAppContainer()

  rootCommand argv {
    description "The Perla Dev Server!"

    configure(fun cfg ->
      // don't replace leading @ strings e.g. @lit-labs/task
      cfg.ResponseFileTokenReplacer <- null)

    noActionAsync

    addCommands [
      Commands.Setup
      Commands.Template
      Commands.Describe
      Commands.Build
      Commands.Serve
      Commands.Test
      Commands.AddPackage
      Commands.RemovePackage
      Commands.ListPackages
      Commands.NewProject
    ]

    helpAction
  }
  |> Async.AwaitTask
  |> Async.RunSynchronously
