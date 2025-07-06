module Perla.Env

open System
open System.Runtime.InteropServices
open System.Text
open FSharp.UMX
open Perla.Units

open FsToolkit.ErrorHandling


[<Literal>]
let PerlaEnvPrefix = "PERLA_"


let GetEnvContent
  (args:
    {|
      GetEnvVars: unit -> (string * string) list
      ReadFile: string<SystemPath> -> string[]
    |})
  =
  option {
    let env = [
      for (key, value) in args.GetEnvVars() do
        if key.StartsWith PerlaEnvPrefix then
          key.Replace(PerlaEnvPrefix, String.Empty), value
    ]

    let content =
      (StringBuilder(), env)
      ||> List.fold(fun sb (key, value) ->
        sb.Append $"""export const %s{key} = "%s{value}";""")
      |> _.ToString()

    if String.IsNullOrWhiteSpace content then
      return! None
    else
      return content
  }

type PlatformOps =
  abstract member IsWindows: unit -> bool
  abstract member PlatformString: unit -> string
  abstract member ArchString: unit -> string

let Create() : PlatformOps =
  { new PlatformOps with
      member _.IsWindows() =
        RuntimeInformation.IsOSPlatform OSPlatform.Windows

      member _.PlatformString() =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
          "win32"
        else if RuntimeInformation.IsOSPlatform OSPlatform.Linux then
          "linux"
        else if RuntimeInformation.IsOSPlatform OSPlatform.OSX then
          "darwin"
        else if RuntimeInformation.IsOSPlatform OSPlatform.FreeBSD then
          "freebsd"
        else
          failwith "Unsupported OS"

      member _.ArchString() =
        match RuntimeInformation.OSArchitecture with
        | Architecture.Arm -> "arm"
        | Architecture.Arm64 -> "arm64"
        | Architecture.X64 -> "x64"
        | Architecture.X86 -> "ia32"
        | _ -> failwith "Unsupported Architecture"
  }
