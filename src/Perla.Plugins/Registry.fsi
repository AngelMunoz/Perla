namespace Perla.Plugins.Registry

open System.Collections.Generic
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell

open Perla.Plugins

[<Struct>]
type PluginLoadError =
  | SessionExists
  | BoundValueMissing
  | AlreadyLoaded of string
  | EvaluationFailed of evalFailure: exn
  | NoPluginFound of pluginName: string


type SessionCache<'Cache
  when 'Cache: (static member SessionCache:
    Lazy<Dictionary<string, FsiEvaluationSession>>)> = 'Cache

type PluginCache<'Cache
  when 'Cache: (static member PluginCache: Lazy<Dictionary<string, PluginInfo>>)>
  = 'Cache

type StdoutStderr<'Writer
  when 'Writer: (static member Stdout: TextWriter)
  and 'Writer: (static member Stderr: TextWriter)> = 'Writer


// Interface-based services for plugin registry (new implementation)

[<Interface>]
type PluginManager =
  // Session management
  abstract CreateSession:
    id: string -> Result<FsiEvaluationSession, PluginLoadError>

  abstract GetSession: id: string -> FsiEvaluationSession option
  abstract RemoveSession: id: string -> bool
  abstract HasSession: id: string -> bool

  // Plugin storage
  abstract AddPlugin: plugin: PluginInfo -> Result<unit, PluginLoadError>
  abstract GetPlugin: name: string -> PluginInfo option
  abstract GetAllPlugins: unit -> PluginInfo list
  abstract HasPlugin: name: string -> bool
  abstract GetRunnablePlugins: order: string list -> RunnablePlugin list

  // Plugin loading
  abstract LoadFromCode: plugin: PluginInfo -> Result<unit, PluginLoadError>

  abstract LoadFromText:
    id: string * content: string * ?cancellationToken: CancellationToken ->
      Result<PluginInfo, PluginLoadError>

  // Plugin execution
  abstract RunPlugins:
    pluginOrder: string list -> fileInput: FileTransform -> Async<FileTransform>

  abstract HasPluginsForExtension: extension: string -> bool

[<RequireQualifiedAccess>]
module PluginManager =
  val Create: stdout: TextWriter * stderr: TextWriter -> PluginManager

[<RequireQualifiedAccess>]
module PluginRegistry =
  val inline GetRunnablePlugins<'Cache when PluginCache<'Cache>> :
    string seq -> RunnablePlugin list

  val inline GetPluginList<'Cache when PluginCache<'Cache>> :
    unit -> PluginInfo list

  val inline RunPlugins<'Cache when PluginCache<'Cache>> :
    pluginOrder: string list -> fileInput: FileTransform -> Async<FileTransform>

  val inline HasPluginsForExtension<'Cache when PluginCache<'Cache>> :
    extension: string -> bool

  val inline LoadFromCode<'Cache when PluginCache<'Cache>> :
    PluginInfo -> Result<unit, PluginLoadError>

[<Class; Sealed>]
type PluginRegistry =

  static member inline LoadFromText<'Cache, 'Writer
    when PluginCache<'Cache> and SessionCache<'Cache> and StdoutStderr<'Writer>> :
    id: string * content: string * ?cancellationToken: CancellationToken ->
      Result<PluginInfo, PluginLoadError>
