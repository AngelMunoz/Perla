namespace Perla.SuaveService

open System
open Microsoft.Extensions.Logging
open System.Reactive.Subjects
open Perla
open Perla.Types
open Perla.Units
open Perla.VirtualFs
open Perla.FileSystem
open FSharp.Data.Adaptive
open Suave

/// Context containing all dependencies needed for Suave server
type SuaveContext = {
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  Config: PerlaConfig aval
  FsManager: PerlaFsManager
  FileChangedEvents: IObservable<FileChangedEvent>
  CompileErrorEvents: IObservable<string option>
}

/// MIME type utilities
module MimeTypes =

  /// Try to get content type for a file path
  val tryGetContentType: filePath: string -> string option

  /// Get content type for a file path, with fallback to default
  val getContentType: filePath: string -> string

/// HTTP proxy functionality using Suave.Proxy
module ProxyService =

  /// Create proxy webparts from configuration map
  val createProxyWebparts: proxyConfig: Map<string, string> -> WebPart

/// Virtual file system integration
module VirtualFiles =

  /// Create webpart that resolves files from VFS
  val resolveFile: suaveCtx: SuaveContext -> WebPart

/// Live reload functionality using Server-Sent Events
module LiveReload =

  /// Create SSE handler for live reload events
  val sseHandler:
    fileChangedEvents: IObservable<FileChangedEvent> ->
    compileErrorEvents: IObservable<string option> ->
      WebPart

/// SPA fallback functionality
module SpaFallback =

  /// Create SPA fallback webpart
  val spaFallback: config: PerlaConfig -> WebPart

/// Perla-specific request handlers
module PerlaHandlers =

  /// Live reload script endpoint
  val liveReloadScript: fsManager: PerlaFsManager -> WebPart

  /// Worker script endpoint
  val workerScript: fsManager: PerlaFsManager -> WebPart

  /// Testing helpers script endpoint
  val testingHelpers: fsManager: PerlaFsManager -> WebPart

  /// Index page handler
  val indexHandler: config: PerlaConfig * fsManager: PerlaFsManager -> WebPart

/// Main Suave server configuration and startup
module SuaveServer =

  /// Create the main Suave application
  val createApp: suaveCtx: SuaveContext -> WebPart

  /// Start the Suave server
  val startServer:
    suaveCtx: SuaveContext ->
    cancellationToken: Threading.CancellationToken ->
      unit
