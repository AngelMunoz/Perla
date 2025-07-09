namespace Perla.Fable

open System.Collections.Generic
open Microsoft.Extensions.Logging

open IcedTasks

open Perla
open Perla.Types

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

  val Create: args: FableArgs -> FableService
