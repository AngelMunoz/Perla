namespace Perla.Extensibility

open System
open System.IO
open System.Text
open Microsoft.Extensions.Logging

open FsToolkit.ErrorHandling

open Perla.Plugins
open Perla.Plugins.Registry

[<Interface>]
type ExtensibilityService =
  abstract LoadPlugins:
    pluginFiles: (string * string)[] * ?defaultPlugins: seq<PluginInfo> ->
      Result<PluginInfo list, PluginLoadError>

  abstract GetAllPlugins: unit -> PluginInfo list
  abstract GetRunnablePlugins: order: string list -> RunnablePlugin list

  abstract RunPlugins:
    pluginOrder: string list -> fileInput: FileTransform -> Async<FileTransform>

  abstract HasPluginsForExtension: extension: string -> bool

[<RequireQualifiedAccess>]
module ExtensibilityService =
  // LogTextWriter function that returns a TextWriter object expression
  let logWriter(logger: ILogger, logLevel: LogLevel) =
    let buffer = StringBuilder()
    { new TextWriter() with
        override _.Encoding = Encoding.UTF8

        override _.Write(value: char) =
          if value = '\n' then
            let line = buffer.ToString()
            logger.Log(logLevel, line)
            buffer.Clear() |> ignore
          else
            buffer.Append(value) |> ignore

        override _.Write(value: string) =
          if String.IsNullOrEmpty value then
            ()
          else
            for c in value do
              if c = '\n' then
                let line = buffer.ToString()
                logger.Log(logLevel, line)
                buffer.Clear() |> ignore
              else
                buffer.Append(c) |> ignore

        override _.WriteLine(value: string) = logger.Log(logLevel, value)

        override _.WriteLine() =
          let line = buffer.ToString()
          logger.Log(logLevel, line)
          buffer.Clear() |> ignore

        override _.Flush() =
          if buffer.Length > 0 then
            let line = buffer.ToString()
            logger.Log(logLevel, line)
            buffer.Clear() |> ignore
    }

  let Create(logger: ILogger) =
    let stdout = logWriter(logger, LogLevel.Information)
    let stderr = logWriter(logger, LogLevel.Error)
    let pluginManager = PluginManager.Create(stdout, stderr)

    { new ExtensibilityService with
        member _.LoadPlugins(pluginFiles, ?defaultPlugins) = result {
          let plugins = defaultArg defaultPlugins Seq.empty

          // Load all default plugins first if provided
          for plugin in plugins do
            do! pluginManager.LoadFromCode(plugin)
            logger.LogInformation($"Loaded default plugin: {plugin.name}")

          // Load plugins from files
          do!
            pluginFiles
            |> Array.traverseResultM(fun (path, content) ->
              logger.LogTrace(
                "Loading plugin from {path} with content {content}",
                path,
                content
              )

              logger.LogDebug("Loading plugin from {path}", path)

              pluginManager.LoadFromText(path, content))
            |> Result.teeError(fun (error: PluginLoadError) ->
              logger.LogError("Failure to load a plugin: {error}", error))
            |> Result.ignore

          // Return all loaded plugins
          return pluginManager.GetAllPlugins()
        }

        member _.GetAllPlugins() = pluginManager.GetAllPlugins()

        member _.GetRunnablePlugins(order) =
          pluginManager.GetRunnablePlugins(order)

        member _.RunPlugins pluginOrder fileInput =
          pluginManager.RunPlugins pluginOrder fileInput

        member _.HasPluginsForExtension(extension) =
          pluginManager.HasPluginsForExtension(extension)
    }
