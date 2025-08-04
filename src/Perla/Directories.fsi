namespace Perla

open FSharp.UMX
open Perla.Units

[<Interface>]
type PerlaDirectories =
  abstract AssemblyRoot: string<SystemPath> with get
  abstract PerlaArtifactsRoot: string<SystemPath> with get
  abstract Database: string<SystemPath> with get
  abstract Templates: string<SystemPath> with get
  abstract OfflineTemplates: string<SystemPath> with get
  abstract PerlaConfigPath: string<SystemPath> with get
  abstract OriginalCwd: string<SystemPath> with get
  abstract CurrentWorkingDirectory: string<SystemPath> with get
  abstract PlaywrightArtifactsRoot: string<SystemPath> with get
  abstract SetCwdToProject: ?fromPath: string<SystemPath> -> unit

module PerlaDirectories =
  val Create: ops: PlatformOps -> PerlaDirectories
