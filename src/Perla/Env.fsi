module Perla.Env

open FSharp.UMX
open Perla.Units

val IsWindows: bool
val PlatformString: string
val ArchString: string
val internal getPerlaEnvVars: unit -> (string * string) list
val GetEnvContent: unit -> string option
val LoadEnvFiles: files: string<SystemPath> seq -> unit

type PlatformOpsArgs = {
  GetEnvVars: unit -> (string * string) list
  ReadFile: string<SystemPath> -> string[]
}

type PlatformOps =
  abstract member IsWindows: unit -> bool
  abstract member PlatformString: unit -> string
  abstract member ArchString: unit -> string
  abstract member GetEnvContent: unit -> string option
  abstract member LoadEnvFiles: files: string<SystemPath> seq -> unit

val Create: args: PlatformOpsArgs -> PlatformOps
