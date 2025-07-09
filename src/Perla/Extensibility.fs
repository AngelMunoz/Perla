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
    pluginFiles: (string * string)[] * ?esbuildPlugin: PluginInfo ->
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
            buffer.Clear() |> ignore

            if not(String.IsNullOrWhiteSpace(line)) then
              logger.Log(logLevel, line)
          else
            buffer.Append(value) |> ignore

        override _.Write(value: string) =
          if String.IsNullOrEmpty value then
            ()
          else
            for char in value do
              base.Write(char)

        override _.WriteLine(value: string) =
          if String.IsNullOrEmpty value then
            logger.Log(logLevel, String.Empty)
          else
            logger.Log(logLevel, value)

        override _.WriteLine() =
          let line = buffer.ToString()
          buffer.Clear() |> ignore
          logger.Log(logLevel, line)

        override _.Flush() =
          if buffer.Length > 0 then
            let line = buffer.ToString()
            buffer.Clear() |> ignore

            if not(String.IsNullOrWhiteSpace(line)) then
              logger.Log(logLevel, line)
    }

  let Create(logger: ILogger) =
    let stdout = logWriter(logger, LogLevel.Information)
    let stderr = logWriter(logger, LogLevel.Error)
    let pluginManager = PluginManager.Create(stdout, stderr)

    { new ExtensibilityService with
        member _.LoadPlugins(pluginFiles, ?esbuildPlugin) = result {
          // Load the esbuild plugin first if provided
          match esbuildPlugin with
          | Some plugin ->
            do! pluginManager.LoadFromCode(plugin)
            logger.LogInformation($"Loaded esbuild plugin: {plugin.name}")
          | None -> ()

          // Load plugins from files
          let! loadResults =
            pluginFiles
            |> Array.traverseResultM(fun (path, content) ->
              pluginManager.LoadFromText(path, content))
            |> Result.teeError(fun (error: PluginLoadError) ->
              logger.LogError("Failure to load a plugin: {error}", error))


          // Return all loaded plugins
          return pluginManager.GetAllPlugins()
        }

        member _.GetAllPlugins() = pluginManager.GetAllPlugins()

        member _.GetRunnablePlugins(order) =
          pluginManager.GetRunnablePlugins(order)

        member _.RunPlugins (pluginOrder) (fileInput) =
          pluginManager.RunPlugins (pluginOrder) fileInput

        member _.HasPluginsForExtension(extension) =
          pluginManager.HasPluginsForExtension(extension)
    }
