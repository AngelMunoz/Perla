namespace Perla.Plugins.Registry

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

module PluginLoadError =
  val AsString: PluginLoadError -> string


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
