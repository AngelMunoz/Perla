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
