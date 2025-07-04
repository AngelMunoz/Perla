namespace Perla.Fable

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

open Microsoft.Extensions.Logging

open IcedTasks
open CliWrap

open Perla.Types
open Perla.Env

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

  val Create: args: FableArgs -> FableService


[<Class; ObsoleteAttribute("Use Fable.Create instead")>]
type Fable =

  /// Use this method to run a one-off fable execution
  static member Start:
    config: FableConfig *
    [<Optional>] ?stdout: (string -> unit) *
    [<Optional>] ?stderr: (string -> unit) *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<CommandResult>

  /// Use this method to monitor fable stdout/stderr logs
  /// and get notice when a compilation finishes
  static member Observe:
    config: FableConfig *
    [<Optional>] ?isWatch: bool *
    [<Optional>] ?stdout: (string -> unit) *
    [<Optional>] ?stderr: (string -> unit) *
    [<Optional>] ?cancellationToken: CancellationToken ->
      IObservable<FableEvent>
