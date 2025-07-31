namespace Perla


open Microsoft.Extensions.Logging
open IcedTasks

open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open Perla
open Perla.Types
open Perla.Database
open Perla.RequestHandler
open Perla.FileSystem
open Perla.Fable
open Perla.Esbuild
open Perla.Extensibility
open Perla.VirtualFs
open Perla.Scaffolding
open Perla.Configuration
open Perla.PkgManager


module Warmup =
  type RecoverableAssets =
    | Esbuild
    | Templates
    | Fable
    | Playwright

  type MiddlewareResult =
    | Continue
    | Recover of RecoverableAssets Set
    | HardExit


  [<RequireQualifiedAccess>]
  module Check =

    val Setup:
      logger: ILogger *
      db: PerlaDatabase *
      config: PerlaConfig aval *
      fable: FableService *
      directories: PerlaDirectories *
      requiredAssets: RecoverableAssets seq ->
        CancellableTask<MiddlewareResult>

  module Recover =
    type SetupFailure =
      | EsbuildFailed of string
      | TemplatesFailed
      | FableFailed
      | PlaywrightFailed
      | HardExitRequested

    type RecoverArgs = {
      config: PerlaConfig aval
      db: PerlaDatabase
      pfsm: PerlaFsManager
      logger: ILogger
      skipPrompts: bool
      ci: bool
    }

    val From:
      args: RecoverArgs ->
      result: MiddlewareResult ->
        CancellableTaskResult<unit, SetupFailure>

type HasLogger =
  abstract member Logger: ILogger

type HasRequestHandler =
  abstract member RequestHandler: RequestHandler

type HasPlatformOps =
  abstract member PlatformOps: PlatformOps

type HasDirectories =
  abstract member Directories: PerlaDirectories

type HasFsManager =
  abstract member FsManager: PerlaFsManager

type HasDatabase =
  abstract member Db: PerlaDatabase

type HasEsbuildService =
  abstract member EsbuildService: EsbuildService

type HasExtensibilityService =
  abstract member ExtensibilityService: ExtensibilityService

type HasVirtualFileSystem =
  abstract member VirtualFileSystem: VirtualFileSystem

type HasFableService =
  abstract member FableService: FableService

type HasTemplateService =
  abstract member TemplateService: TemplateService

type HasConfiguration =
  abstract member Configuration: ConfigurationManager

type HasPkgManager =
  abstract member PkgManager: PkgManager

type HasBuildService =
  abstract member BuildService: Perla.Build.BuildService

[<Interface>]
type AppContainer =
  inherit HasLogger
  inherit HasPlatformOps
  inherit HasDirectories
  inherit HasFsManager
  inherit HasDatabase
  inherit HasEsbuildService
  inherit HasExtensibilityService
  inherit HasVirtualFileSystem
  inherit HasFableService
  inherit HasTemplateService
  inherit HasConfiguration
  inherit HasPkgManager
  inherit HasRequestHandler
  inherit HasBuildService

type AppContainerArgs = {
  Logger: ILogger
  Directories: PerlaDirectories
  FsManager: PerlaFsManager
  Platform: PlatformOps
  RequestHandler: RequestHandler
}


module AppContainer =
  val Create: args: AppContainerArgs -> AppContainer

[<AutoOpen>]
module Patterns =
  val inline (|Logger|): container: #HasLogger -> ILogger
  val inline (|PlatformOps|): container: #HasPlatformOps -> PlatformOps
  val inline (|Directories|): container: #HasDirectories -> PerlaDirectories
  val inline (|FsManager|): container: #HasFsManager -> PerlaFsManager
  val inline (|Database|): container: #HasDatabase -> PerlaDatabase
  val inline (|EsbuildService|): container: #HasEsbuildService -> EsbuildService
  val inline (|RequestHandler|): container: #HasRequestHandler -> RequestHandler

  val inline (|ExtensibilityService|):
    container: #HasExtensibilityService -> ExtensibilityService

  val inline (|VirtualFileSystem|):
    container: #HasVirtualFileSystem -> VirtualFileSystem

  val inline (|FableService|): container: #HasFableService -> FableService

  val inline (|TemplateService|):
    container: #HasTemplateService -> TemplateService

  val inline (|Configuration|):
    container: #HasConfiguration -> ConfigurationManager

  val inline (|PkgManager|): container: #HasPkgManager -> PkgManager
