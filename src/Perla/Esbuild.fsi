namespace Perla.Esbuild

open IcedTasks
open FSharp.UMX

open Perla
open Perla.FileSystem
open Perla.Types
open Perla.Units
open Perla.Plugins

[<RequireQualifiedAccess; Struct>]
type LoaderType =
  | Typescript
  | Tsx
  | Jsx
  | Css

type EsbuildServiceArgs = {
  Cwd: string<SystemPath>
  PerlaFsManager: PerlaFsManager
  Logger: Microsoft.Extensions.Logging.ILogger
  PlatformOps: PlatformOps
}

[<Interface>]
type EsbuildService =

  abstract ProcessJS:
    entrypoint: string<ServerUrl> *
    sourcesPath: string<SystemPath> *
    outdir: string<SystemPath> *
    config: EsbuildConfig ->
      CancellableTask<unit>

  abstract ProcessCss:
    entrypoint: string<ServerUrl> *
    sourcesPath: string<SystemPath> *
    outdir: string<SystemPath> *
    config: EsbuildConfig ->
      CancellableTask<unit>

  abstract GetPlugin: config: EsbuildConfig -> PluginInfo

[<RequireQualifiedAccess>]
module Esbuild =
  val Create: serviceArgs: EsbuildServiceArgs -> EsbuildService
