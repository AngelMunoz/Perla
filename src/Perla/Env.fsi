module Perla.Env

open System
open FSharp.UMX
open Perla.Units

[<Obsolete("Use PlatformOps instead")>]
val IsWindows: bool

[<Obsolete("Use PlatformOps instead")>]
val PlatformString: string

[<Obsolete("Use PlatformOps instead")>]
val ArchString: string

[<Obsolete("Use PlatformOps instead")>]
val internal getPerlaEnvVars: unit -> (string * string) list

[<Obsolete("Use PlatformOps instead")>]
val GetEnvContent: unit -> string option

[<Obsolete("Use PlatformOps instead")>]
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
