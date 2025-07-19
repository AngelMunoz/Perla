namespace Perla.Build


open AngleSharp.Html.Dom

open FSharp.UMX
open FSharp.Data.Adaptive
open IcedTasks


open Perla.Types
open Perla.Units
open Perla.PkgManager

type BuildOptions = { enablePreview: bool }

[<RequireQualifiedAccess>]
module Build =
  val EnsureBody: IHtmlDocument -> AngleSharp.Dom.IElement
  val EnsureHead: IHtmlDocument -> AngleSharp.Dom.IElement

  val EntryPoints:
    IHtmlDocument ->
      string<ServerUrl> seq * string<ServerUrl> seq * string<ServerUrl> seq

  val Externals: PerlaConfig -> string seq

  val Index:
    IHtmlDocument *
    ImportMap *
    jsExtras: string<ServerUrl> seq *
    cssExtras: string<ServerUrl> seq ->
      string

  val collectFilesFromDirectory:
    string<SystemPath> -> string<ServerUrl> seq * string<ServerUrl> seq
// --- BuildService interface and args ---
type BuildServiceArgs = {
  Logger: Microsoft.Extensions.Logging.ILogger
  FsManager: Perla.FileSystem.PerlaFsManager
  EsbuildService: Perla.Esbuild.EsbuildService
  ExtensibilityService: Perla.Extensibility.ExtensibilityService
  VirtualFileSystem: Perla.VirtualFs.VirtualFileSystem
  FableService: Perla.Fable.FableService
  Directories: Perla.PerlaDirectories
}

type EsbuildOutput = {
  outputDir: string<SystemPath>
  cssFiles: string<ServerUrl> seq
  jsFiles: string<ServerUrl> seq
}

[<Interface>]
type BuildService =
  abstract RunFable: config: PerlaConfig aval -> CancellableTask<unit>
  abstract CleanOutput: config: PerlaConfig aval -> unit

  abstract LoadPlugins:
    config: PerlaConfig aval * vfsOutputDir: string<SystemPath> -> unit

  abstract LoadVfs: config: PerlaConfig aval -> CancellableTask<unit>

  abstract CopyVfsToDisk:
    vfsOutputDir: string<SystemPath> -> CancellableTask<string<SystemPath>>

  abstract EmitEnvFile:
    config: PerlaConfig aval * tempDir: string<SystemPath> -> unit

  abstract RunEsbuild:
    config: PerlaConfig aval *
    tempDir: string<SystemPath> *
    cssPaths: seq<string<Perla.Units.ServerUrl>> *
    jsBundleEntrypoints: seq<string<Perla.Units.ServerUrl>> *
    externals: string list ->
      CancellableTask<EsbuildOutput>

  abstract MoveOrCopyOutput:
    config: PerlaConfig aval *
    tempDir: string<SystemPath> *
    esbuildOutput: string<SystemPath> ->
      unit

  abstract WriteIndex:
    config: PerlaConfig aval *
    document: AngleSharp.Html.Dom.IHtmlDocument *
    map: Perla.PkgManager.ImportMap *
    jsPaths: seq<string<Perla.Units.ServerUrl>> *
    cssPaths: seq<string<Perla.Units.ServerUrl>> *
    esbuildCssFiles: seq<string<Perla.Units.ServerUrl>> ->
      CancellableTask<unit>

module BuildService =
  val Create: BuildServiceArgs -> BuildService
