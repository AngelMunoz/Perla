// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging

open FSharp.Control
open IcedTasks

open Spectre.Console
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

[<AutoOpen>]
module Args =
  /// `[log=...]`-style tokens are handled before parsing and are not part of
  /// the CLI grammar, so they must not reach the command line parser.
  let stripBracketTokens (args: string array) =
    args
    |> Array.filter(fun arg ->
      not(arg.StartsWith "[" && arg.EndsWith "]"))

module Interactive =
  open System.CommandLine.Parsing

  let runRoot
    (token: CancellationToken, container: AppContainer)
    (line: string[])
    =
    asyncEx {
      match line with
      | [| arg |] when
        arg.Trim() = "exit"
        || arg.Trim() = "quit"
        || arg.Trim() = "q"
        || arg.Trim() = ""
        ->
        return Ok "No command supplied"
      | args ->

        let! result = rootCommand (stripBracketTokens args) {
          configureParser(fun cfg -> cfg.ResponseFileTokenReplacer <- null)

          inputs Input.context
          helpActionAsync

          addCommands [
            Commands.Restore(token, container)
            Commands.AddPackage(token, container)
            Commands.RemovePackage(token, container)
            Commands.ListPackages(token, container)
            Commands.Template(token, container)
            Commands.Describe(token, container)
          ]
        }

        match result with
        | 0 -> return Ok "Command succeeded"
        | _ -> return Error "Command failed"
    }

  let GetStdinLineMonitor (container: AppContainer) (ct: CancellationToken) = taskSeq {
    use stream = Console.OpenStandardInput()
    use sr = new StreamReader(stream, Console.InputEncoding)

    try
      while not ct.IsCancellationRequested do
        AnsiConsole.Markup "[yellow]Perla >>> [/]"
        let! line = sr.ReadLineAsync(ct)

        if isNull line then
          // EOF
          ()
        else if String.IsNullOrWhiteSpace line then
          ()
        else
          let args =
            line.Trim() |> CommandLineParser.SplitCommandLine |> Array.ofSeq

          let! result = runRoot (ct, container) args

          yield args, result
    with
    | :? OperationCanceledException -> ()
    | ex ->
      yield Array.empty, Error(sprintf "Error reading line: %s" ex.Message)
  }


  let RunInteractive
    (container: AppContainer)
    (cts: CancellationTokenSource)
    argv
    =
    let pickedInteractive =
      argv
      |> Array.tryPick(fun arg ->
        match arg with
        | "serve"
        | "s"
        | "start"
        | "test"
        | "t" -> Some()
        | _ -> None)

    match pickedInteractive with
    | Some _ ->
      let work = asyncEx {
        let! token = Async.CancellationToken
        let monitor = GetStdinLineMonitor container token

        for line, evaluation in monitor do
          let joinedLine = String.Join(' ', line)

          match evaluation with
          | Ok msg when msg <> "" ->
            container.Logger.LogDebug(
              "Command succeeded: {Line} - {Message}",
              joinedLine,
              msg
            )
          | Ok _ -> ()
          | Error msg ->
            container.Logger.LogError(
              "Command failed: {Line} - {Message}",
              joinedLine,
              msg
            )

          match line with
          | [| arg |] when
            arg.Trim() = "exit" || arg.Trim() = "quit" || arg.Trim() = "q"
            ->
            container.Logger.LogInformation "Thank you for using Perla!"
            cts.Cancel()
          | _ -> ()

      }

      Async.Start(work, cancellationToken = cts.Token)
    | None -> ()

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
  use cts = new CancellationTokenSource()

  Console.CancelKeyPress.Add(fun _ ->
    appContainer.Logger.LogInformation "User Requested Shutdown..."
    cts.Cancel())

  Interactive.RunInteractive appContainer cts argv

  let work() =
    rootCommand (stripBracketTokens argv) {
      description "The Perla Dev Server!"

      // don't replace leading @ strings e.g. @lit-labs/task
      configureParser(fun cfg -> cfg.ResponseFileTokenReplacer <- null)

      inputs Input.context
      helpActionAsync

      addCommands [
        Commands.NewProject(cts.Token, appContainer)
        Commands.Restore(cts.Token, appContainer)
        Commands.AddPackage(cts.Token, appContainer)
        Commands.RemovePackage(cts.Token, appContainer)
        Commands.ListPackages(cts.Token, appContainer)
        Commands.Serve(cts.Token, appContainer)
        Commands.Build(cts.Token, appContainer)
        Commands.Test(cts.Token, appContainer)
        Commands.Template(cts.Token, appContainer)
        Commands.Describe(cts.Token, appContainer)
      ]
    }
    |> Async.AwaitTask

  let exit = Async.RunSynchronously(work(), cancellationToken = cts.Token)
  cts.Cancel()
  exit
