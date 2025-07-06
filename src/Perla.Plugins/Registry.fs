namespace Perla.Plugins.Registry

open System
open System.Collections.Generic
open System.IO
open System.Threading

open FSharp.Compiler.Interactive.Shell
open FSharp.Control
open IcedTasks
open FsToolkit.ErrorHandling
open Perla.Plugins

[<Struct>]
type PluginLoadError =
  | SessionExists
  | BoundValueMissing
  | AlreadyLoaded of string
  | EvaluationFailed of evalFailure: exn
  | NoPluginFound of pluginName: string


module PluginLoadError =
  let AsString =
    function
    | SessionExists -> "A session with this ID already exists."
    | BoundValueMissing -> "The expected bound value is missing."
    | AlreadyLoaded name -> $"Plugin '{name}' is already loaded."
    | EvaluationFailed ex -> $"Evaluation failed: {ex.Message}"
    | NoPluginFound name -> $"No plugin found with the name '{name}'."

module SessionFactory =
  let inline create stdout stderr =
    FsiEvaluationSession.Create(
      FsiEvaluationSession.GetDefaultConfiguration(),
      [| "fsi.exe"; "--optimize+"; "--nologo"; "--gui-"; "--readline-" |],
      new StringReader(""),
      stdout,
      stderr,
      true
    )

module ScriptReflection =
  let inline findPlugin(fsi: FsiEvaluationSession) =
    match fsi.TryFindBoundValue "Plugin", fsi.TryFindBoundValue "plugin" with
    | Some bound, _ -> Some bound.Value
    | None, Some bound -> Some bound.Value
    | None, None -> None

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

  let Create(stdout: TextWriter, stderr: TextWriter) =
    let sessions = Dictionary<string, FsiEvaluationSession>()
    let plugins = Dictionary<string, PluginInfo>()

    { new PluginManager with
        // Session management
        member _.CreateSession(id) =
          if sessions.ContainsKey(id) then
            Error SessionExists
          else
            let session = SessionFactory.create stdout stderr
            sessions.Add(id, session)
            Ok session

        member _.GetSession(id) =
          match sessions.TryGetValue(id) with
          | true, session -> Some session
          | false, _ -> None

        member _.RemoveSession(id) = sessions.Remove(id)

        member _.HasSession(id) = sessions.ContainsKey(id)

        // Plugin storage
        member _.AddPlugin(plugin) =
          if plugins.TryAdd(plugin.name, plugin) then
            Ok()
          else
            Error(AlreadyLoaded plugin.name)

        member _.GetPlugin(name) =
          match plugins.TryGetValue(name) with
          | true, plugin -> Some plugin
          | false, _ -> None

        member _.GetAllPlugins() = plugins.Values |> Seq.toList

        member _.HasPlugin(name) = plugins.ContainsKey(name)

        member _.GetRunnablePlugins(order) = [
          for name in order do
            match plugins.TryGetValue(name) with
            | true, plugin ->
              match plugin.shouldProcessFile, plugin.transform with
              | ValueSome st, ValueSome t -> {
                  plugin = plugin
                  shouldTransform = st
                  transform = t
                }
              | _ -> ()
            | false, _ -> ()
        ]

        // Plugin loading
        member this.LoadFromCode(plugin) = this.AddPlugin(plugin)

        member this.LoadFromText(id, content, ?cancellationToken) = result {
          do! this.HasSession(id) |> Result.requireFalse SessionExists

          let! session = this.CreateSession(id)

          let evaluation, _ =
            session.EvalInteractionNonThrowing(
              content,
              ?cancellationToken = cancellationToken
            )

          let! foundValue = result {
            let! result =
              evaluation |> Result.ofChoice |> Result.mapError EvaluationFailed

            match result with
            | Some result -> return result
            | None ->
              return!
                ScriptReflection.findPlugin session
                |> Result.requireSome BoundValueMissing
          }

          if foundValue.ReflectionType = typeof<PluginInfo> then
            let plugin: PluginInfo = unbox foundValue.ReflectionValue
            do! this.AddPlugin(plugin)
            return plugin
          else
            return! Error(NoPluginFound id)
        }

        // Plugin execution
        member this.RunPlugins (pluginOrder) (fileInput) = async {
          let plugins = this.GetRunnablePlugins(pluginOrder)

          let inline folder result next =
            cancellableValueTask {
              match next.shouldTransform result.extension with
              | true -> return! next.transform result
              | false -> return result
            }
            |> Async.AwaitCancellableValueTask

          return!
            plugins |> AsyncSeq.ofSeq |> AsyncSeq.foldAsync folder fileInput
        }

        member this.HasPluginsForExtension(extension) =
          let allPlugins = this.GetAllPlugins()

          allPlugins
          |> List.exists(fun plugin ->
            match plugin.shouldProcessFile with
            | ValueSome f -> f extension
            | _ -> false)
    }
