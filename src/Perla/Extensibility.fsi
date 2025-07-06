namespace Perla.Extensibility

open Microsoft.Extensions.Logging

open Perla.Plugins
open Perla.Plugins.Registry

[<Interface>]
type ExtensibilityService =
  abstract LoadPlugins:
    pluginFiles: (string * string)[] * ?esbuildPlugin: PluginInfo ->
      Result<PluginInfo list, PluginLoadError>

  abstract GetAllPlugins: unit -> PluginInfo list
  abstract GetRunnablePlugins: order: string list -> RunnablePlugin list

  abstract RunPlugins:
    pluginOrder: string list -> fileInput: FileTransform -> Async<FileTransform>

  abstract HasPluginsForExtension: extension: string -> bool

[<RequireQualifiedAccess>]
module ExtensibilityService =
  val Create: logger: ILogger -> ExtensibilityService
