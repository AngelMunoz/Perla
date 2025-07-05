namespace Perla.Esbuild

open System
open System.IO
open System.Text
open System.Runtime.InteropServices

open CliWrap

open IcedTasks
open FSharp.UMX

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
  LoadTsConfig: unit -> CancellableTask<string option>
  Logger: Microsoft.Extensions.Logging.ILogger
}

[<Interface>]
type EsbuildService =
  abstract SetupEsbuild: version: string<Semver> -> CancellableTask<unit>
  abstract ProcessJS:
    entrypoint: string * outdir: string * config: EsbuildConfig ->
      CancellableTask<unit>

  abstract ProcessCss:
    entrypoint: string * outdir: string * config: EsbuildConfig ->
      CancellableTask<unit>

  abstract GetPlugin: config: EsbuildConfig -> PluginInfo

module Esbuild =
  val Create: serviceArgs: EsbuildServiceArgs * perlaDirs: PerlaDirectories -> EsbuildService

[<Class>]
type Esbuild =
  /// Uses esbuild's build API
  /// This is the most flexible option and allows for simpler customization
  /// This should be performed only when the file system is available
  static member ProcessJS:
    workingDirectory: string *
    entryPoint: string *
    config: EsbuildConfig *
    outDir: string *
    [<Optional>] ?externals: string seq *
    [<Optional>] ?aliases: Map<string<BareImport>, string<ResolutionUrl>> ->
      Command

  static member ProcessCss:
    workingDirectory: string *
    entryPoint: string *
    config: EsbuildConfig *
    outDir: string ->
      Command

  /// Uses esbuild's transform API via stdin/stdout
  /// This means each file will be processed in isolation
  static member BuildSingleFile:
    config: EsbuildConfig *
    content: string *
    resultsContainer: StringBuilder *
    ?loader: LoaderType ->
      Command

  static member GetPlugin: config: EsbuildConfig -> PluginInfo
