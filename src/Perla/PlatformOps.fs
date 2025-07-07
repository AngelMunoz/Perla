namespace Perla

open System.Runtime.InteropServices


type PlatformOps =
  abstract member IsWindows: unit -> bool
  abstract member PlatformString: unit -> string
  abstract member ArchString: unit -> string

module PlatformOps =
  let Create() =
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
