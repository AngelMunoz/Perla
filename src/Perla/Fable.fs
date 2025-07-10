namespace Perla.Fable

open IcedTasks

open CliWrap
open CliWrap.EventStream

open Perla
open Perla.Types
open Perla.Units

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

  abstract member Run: FableConfig -> CancellableTask<unit>

  abstract member Monitor: config: FableConfig -> IAsyncEnumerable<FableEvent>

  abstract member IsPresent: unit -> CancellableTask<bool>

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

          do! command.Task :> System.Threading.Tasks.Task
          return ()
        }

        member _.Monitor(config) = taskSeq {
          let command = newerFableCmd args (config, false)

          for event: CommandEvent in command.ListenAsync() do
            match event with
            | :? StartedCommandEvent as started ->
              args.Logger.LogInformation(
                "Fable started with pid: [{ProcessId}]",
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

        member _.IsPresent() = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let command =
            let execBinName =
              if args.Platform.IsWindows() then "dotnet.exe" else "dotnet"

            Cli
              .Wrap(execBinName)
              .WithArguments([ "fable"; "--version" ])
              .WithValidation(CommandResultValidation.None)
              .ExecuteAsync(cancellationToken = token)

          let! result = command.Task
          return result.ExitCode = 0
        }
    }
