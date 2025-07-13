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
}

type SuaveTestingContext = {
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  Config: PerlaConfig aval
  FsManager: PerlaFsManager
  FileChangedEvents: IObservable<FileChangedEvent>
  TestEvents: ISubject<TestEvent>
}

type SuaveServerContext =
  | SuaveContext of SuaveContext
  | SuaveTestingContext of SuaveTestingContext

  member Logger: ILogger
  member VirtualFileSystem: VirtualFileSystem
  member Config: PerlaConfig aval
  member FsManager: PerlaFsManager
  member FileChangedEvents: IObservable<FileChangedEvent>
  member TestEvents: ISubject<TestEvent> option

/// MIME type utilities
module MimeTypes =

  /// Try to get content type for a file path
  val tryGetContentType: filePath: string -> string option

  /// Get content type for a file path, with fallback to default
  val getContentType: filePath: string -> string

/// Port utilities for server binding
module PortUtils =

  /// Check if a port is occupied on the given address
  val isAddressPortOccupied: address: string -> port: int -> bool

/// HTTP proxy functionality using Suave.Proxy
module ProxyService =

  /// Create proxy webparts from configuration map
  val createProxyWebparts: proxyConfig: Map<string, string> -> WebPart

/// Virtual file system integration
module VirtualFiles =

  // Types needed for internal testable API
  [<Struct>]
  type RequestedAs =
    | JS
    | Normal

  type FileProcessingResult = {
    ContentType: string
    Content: byte[]
    ShouldProcess: bool
  }

  /// Create webpart that resolves files from VFS
  val resolveFile: suaveCtx: SuaveServerContext -> WebPart

  /// Internal: Pure function to transform CSS content to JS (for testing)
  val internal processCssAsJs: content: string -> url: string -> string

  /// Internal: Pure function to transform JSON content to JS (for testing)
  val internal processJsonAsJs: content: string -> string

  /// Internal: Pure function to determine how to process a file (for testing)
  val internal determineFileProcessing:
    mimeType: string ->
    requestedAs: RequestedAs ->
    content: byte[] ->
    reqPath: string ->
      FileProcessingResult

/// Live reload functionality using Server-Sent Events
module LiveReload =

  /// Create reload message for file changes
  val createReloadMessage: event: FileChangedEvent -> Suave.EventSource.Message

  /// Create HMR message for CSS files
  val createHmrMessage:
    event: FileChangedEvent -> file: FileContent -> Suave.EventSource.Message

  /// Create live reload message (HMR for CSS, reload for others)
  val createLiveReloadMessage:
    vfs: VirtualFileSystem ->
    event: FileChangedEvent ->
      Suave.EventSource.Message

  /// Internal: Pure function to create reload event data (for testing)
  val internal createReloadEventData: event: FileChangedEvent -> string

  /// Internal: Pure function to create HMR event data (for testing)
  val internal createHmrEventData:
    event: FileChangedEvent -> transform: Perla.Plugins.FileTransform -> string

  /// Create SSE handler for live reload events
  val sseHandler:
    vfs: VirtualFileSystem ->
    fileChangedEvents: IObservable<FileChangedEvent> ->
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

  /// Mocha test runner script endpoint
  val mochaRunner: fsManager: PerlaFsManager -> WebPart

  /// Index page handler
  val indexHandler: config: PerlaConfig * fsManager: PerlaFsManager -> WebPart

  /// Testing index page handler
  val testingIndexHandler:
    config: PerlaConfig *
    fsManager: PerlaFsManager *
    importMap: Perla.PkgManager.ImportMap ->
      WebPart

  /// Environment variables endpoint handler
  val envHandler: fsManager: PerlaFsManager -> logger: ILogger -> WebPart

/// Testing endpoints functionality
module TestingHandlers =

  /// Testing files endpoint
  val testingFiles:
    fileGlobs: string seq option * testConfig: TestConfig -> WebPart

  /// Testing environment endpoint
  val testingEnvironment: testConfig: TestConfig -> WebPart

  /// Mocha settings endpoint
  val mochaSettings: mochaConfig: Map<string, obj> option -> WebPart

  /// Testing events POST endpoint
  val testingEvents:
    logger: ILogger * testEvents: System.Reactive.Subjects.ISubject<TestEvent> ->
      WebPart

/// Main Suave server configuration and startup
module SuaveServer =

  /// Create the main Suave application
  val createTestingApp: suaveCtx: SuaveServerContext -> WebPart

  /// Create the main Suave application
  val createApp: suaveCtx: SuaveServerContext -> WebPart

  /// Create a static server application
  val createStaticServerApp: suaveCtx: SuaveServerContext -> WebPart

  /// Start the Suave server
  val startServer:
    suaveCtx: SuaveServerContext ->
    cancellationToken: Threading.CancellationToken ->
      unit

  val startStaticServer:
    suaveCtx: SuaveContext ->
    cancellationToken: Threading.CancellationToken ->
      unit
