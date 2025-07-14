namespace Perla.Fable

open IcedTasks

open Perla
open Perla.Types
open Perla.Units

open FSharp.Control
open FSharp.Control.Reactive
open FSharp.UMX
open System.Collections.Generic
open Microsoft.Extensions.Logging
open System.Threading

[<RequireQualifiedAccess>]
type FableEvent =
  | Log of string
  | ErrLog of string
  | WaitingForChanges

[<Interface>]
type FableService =

  abstract member Run: FableConfig -> CancellableTask<unit>

  abstract member Monitor:
    config: FableConfig * ?cancellationToken: CancellationToken ->
      IAsyncEnumerable<FableEvent>

  abstract member IsPresent: unit -> CancellableTask<bool>

type FableArgs = {
  Platform: PlatformOps
  Logger: ILogger
}

module Fable =

  let Create(args: FableArgs) : FableService =
    { new FableService with
        member _.Run config = cancellableTask {
          do!
            args.Platform.RunFable(
              config.project,
              config.outDir,
              config.extension
            )

          return ()
        }

        member _.Monitor(config, ?cancellationToken) = taskSeq {
          let stream =
            args.Platform.StreamFable(
              config.project,
              config.outDir,
              config.extension,
              ?cancellationToken = cancellationToken
            )

          for event in stream do
            match event with
            | ProcessEvent.Started processId ->
              args.Logger.LogInformation(
                "Fable started with pid: [{ProcessId}]",
                processId
              )
            | ProcessEvent.StandardOutput stdout ->
              if stdout.ToLowerInvariant().Contains "watching" then
                FableEvent.WaitingForChanges
              else
                FableEvent.Log stdout
            | ProcessEvent.StandardError stderr -> FableEvent.ErrLog stderr
            | ProcessEvent.Exited exitCode ->
              args.Logger.LogInformation(
                "Fable exited with code: [{ExitCode}]",
                exitCode
              )

              if exitCode <> 0 then
                FableEvent.ErrLog $"Fable exited with code {exitCode}"
        }

        member _.IsPresent() = cancellableTask {
          return! args.Platform.IsFableAvailable()
        }
    }
