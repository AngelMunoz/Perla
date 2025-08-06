namespace Perla

open IcedTasks
open FSharp.UMX
open Perla.Units
open Perla.Types
open Microsoft.Extensions.Logging
open System.Threading
open System.Collections.Generic

[<RequireQualifiedAccess>]
type ProcessEvent =
  | Started of processId: int
  | StandardOutput of text: string
  | StandardError of text: string
  | Exited of exitCode: int

type ProcessResult = {
  ExitCode: int
  StandardOutput: string
  StandardError: string
}

type PlatformOps =
  abstract member IsWindows: unit -> bool
  abstract member PlatformString: unit -> string
  abstract member ArchString: unit -> string

  // Process execution methods
  abstract CheckDotnetToolVersion:
    toolName: string -> CancellableTask<ProcessResult>

  abstract InstallDotnetTool: toolName: string -> CancellableTask<ProcessResult>

  abstract RunFable:
    project: string<SystemPath> *
    outDir: string<SystemPath> option *
    extension: string<FileExtension> ->
      CancellableTask<unit>

  abstract StreamFable:
    project: string<SystemPath> *
    outDir: string<SystemPath> option *
    extension: string<FileExtension> *
    ?cancellationToken: CancellationToken ->
      IAsyncEnumerable<ProcessEvent>

  abstract IsFableAvailable: unit -> CancellableTask<bool>

  abstract RunEsbuildTransform:
    esbuildPath: string<SystemPath> *
    sourceCode: string *
    loader: string option *
    target: string *
    jsxAutomatic: bool *
    jsxImportSource: string option *
    tsconfig: string option ->
      CancellableTask<string>

  abstract RunEsbuildCss:
    esbuildPath: string<SystemPath> *
    workingDir: string<SystemPath> *
    entrypoint: string *
    outdir: string *
    minify: bool *
    fileLoaders: Map<string, string> ->
      CancellableTask<unit>

  abstract RunEsbuildJs:
    esbuildPath: string<SystemPath> *
    workingDir: string<SystemPath> *
    entrypoint: string *
    outdir: string *
    tsconfig: string option *
    config: EsbuildConfig ->
      CancellableTask<unit>

module PlatformOps =
  val Create: logger: ILogger -> PlatformOps
