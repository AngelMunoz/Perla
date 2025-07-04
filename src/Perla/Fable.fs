namespace Perla.Fable

open System
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

open IcedTasks

open CliWrap
open CliWrap.EventStream

open Perla
open Perla.Types
open Perla.Units
open Perla.Logger
open Perla.Env

open FSharp.Control
open FSharp.Control.Reactive
open FSharp.UMX
open System.Collections.Generic
open Microsoft.Extensions.Logging

[<RequireQualifiedAccess>]
type FableEvent =
  | Log of string
  | ErrLog of string
  | WaitingForChanges

[<Interface>]
type FableService =

  abstract member Run: FableConfig -> CancellableTask<int>

  abstract member Monitor: config: FableConfig -> IAsyncEnumerable<FableEvent>

type FableArgs = {
  Platform: PlatformOps
  Logger: ILogger
}

module Fable =

  let addProject
    (project: string<SystemPath>)
    (args: Builders.ArgumentsBuilder)
    =
    args.Add $"{project}"

  let addOutDir
    (outdir: string<SystemPath> option)
    (args: Builders.ArgumentsBuilder)
    =
    match outdir with
    | Some outdir -> args.Add([ "-o"; $"{outdir}" ])
    | None -> args

  let addExtension
    (extension: string<FileExtension>)
    (args: Builders.ArgumentsBuilder)
    =
    args.Add([ "-e"; $"{extension}" ])

  let addWatch (watch: bool) (args: Builders.ArgumentsBuilder) =
    if watch then args.Add $"watch" else args

  let fableCmd
    (
      config: FableConfig,
      isWatch: bool,
      stdout: Action<string>,
      stderr: Action<string>
    ) =
    let execBinName = if Env.IsWindows then "dotnet.exe" else "dotnet"

    Cli
      .Wrap(execBinName)
      .WithArguments(fun args ->
        args.Add("fable")
        |> addWatch isWatch
        |> addProject config.project
        |> addOutDir config.outDir
        |> addExtension config.extension
        |> ignore)
      .WithStandardErrorPipe(PipeTarget.ToDelegate(stderr))
      .WithStandardOutputPipe(PipeTarget.ToDelegate(stdout))

  let newerFableCmd
    (dependencies: FableArgs)
    (config: FableConfig, isWatch: bool)
    =
    let { Platform = platform } = dependencies

    let execBinName = if platform.IsWindows() then "dotnet.exe" else "dotnet"

    Cli
      .Wrap(execBinName)
      .WithArguments(fun args ->
        args.Add "fable"
        |> addWatch isWatch
        |> addProject config.project
        |> addOutDir config.outDir
        |> addExtension config.extension
        |> ignore)


  let Create(args: FableArgs) : FableService =
    { new FableService with
        member _.Run config = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let command =
            newerFableCmd args (config, false)
            |> _.WithStandardErrorPipe(
              PipeTarget.ToDelegate args.Logger.LogInformation
            )
              .WithStandardOutputPipe(
                PipeTarget.ToDelegate args.Logger.LogError
              )
              .ExecuteAsync(cancellationToken = token)

          let! result = command.Task
          return result.ExitCode
        }

        member _.Monitor(config) = taskSeq {
          let command = newerFableCmd args (config, false)

          for event: CommandEvent in command.ListenAsync() do
            match event with
            | :? StartedCommandEvent as started ->
              args.Logger.LogInformation(
                "Fable started with pid: []",
                started.ProcessId
              )
            | :? StandardOutputCommandEvent as stdout ->
              args.Logger.LogInformation stdout.Text

              if stdout.Text.ToLowerInvariant().Contains("watching") then
                FableEvent.WaitingForChanges
              else
                FableEvent.Log stdout.Text
            | :? StandardErrorCommandEvent as stderr ->
              args.Logger.LogError stderr.Text
              FableEvent.ErrLog stderr.Text
            | :? ExitedCommandEvent as exited ->
              args.Logger.LogInformation(
                "Fable exited with code: [{ExitCode}]",
                exited.ExitCode
              )

              if exited.ExitCode <> 0 then
                FableEvent.ErrLog $"Fable exited with code {exited.ExitCode}"
            | _ -> ()
        }
    }

type Fable =

  static member Start
    (
      config: FableConfig,
      ?stdout: string -> unit,
      ?stderr: string -> unit,
      ?cancellationToken: CancellationToken
    ) =
    task {
      let stdout =
        let stdout = stdout |> Option.map(fun log -> Action<string>(log))
        defaultArg stdout (Action<string>(printfn "Fable: %s"))

      let stderr =
        let stderr = stderr |> Option.map(fun log -> Action<string>(log))
        defaultArg stderr (Action<string>(eprintfn "Fable: %s"))

      let cmdResult =
        Fable
          .fableCmd(config, false, stdout, stderr)
          .ExecuteAsync(?cancellationToken = cancellationToken)

      Logger.log $"Starting Fable with pid: [{cmdResult.ProcessId}]"

      return! cmdResult.Task
    }

  static member Observe
    (
      config: FableConfig,
      [<Optional>] ?isWatch: bool,
      [<Optional>] ?stdout: string -> unit,
      [<Optional>] ?stderr: string -> unit,
      [<Optional>] ?cancellationToken: CancellationToken
    ) : IObservable<FableEvent> =
    let sub = Subject.replay

    let EmitFableEvent(value: string) =
      if value.ToLowerInvariant().Contains("watching") then
        sub.OnNext(FableEvent.WaitingForChanges)
      else
        sub.OnNext(FableEvent.Log value)

    let stdout =
      match stdout with
      | None -> Action<string>(fun e -> EmitFableEvent e)
      | Some stdout ->
        Action.Combine(Action<string>(stdout), Action<string>(EmitFableEvent))
        :?> Action<string>

    let stderr =
      let stderr = stderr |> Option.map(fun stderr -> Action<string>(stderr))
      defaultArg stderr (Action<string>(eprintfn "%s"))

    let cmdResult =
      Fable.fableCmd(config, defaultArg isWatch true, stdout, stderr)

    async {
      try
        let cmdResult =
          cmdResult.ExecuteAsync(?cancellationToken = cancellationToken)

        Logger.log $"Starting Fable with pid: [{cmdResult.ProcessId}]"
        do! cmdResult.Task :> Task |> Async.AwaitTask
        sub.OnCompleted()
      with ex ->
        sub.OnError(ex)
    }
    |> Async.Start

    sub
