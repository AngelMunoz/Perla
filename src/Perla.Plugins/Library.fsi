namespace Perla.Plugins

open System.Threading.Tasks

type FileTransform = {
  /// The text of the file, this will change between plugin
  /// transformations
  content: string
  /// The extension this file is currently holding
  /// this will change between plugin transformations
  /// It also serves for plugin authors to determine
  /// if their plugin should act on this particular file
  extension: string
}

///<summary>
/// A function predicate that allows the plugin author
/// to signal if the file should be processed by the plugin or not
/// </summary>
type FilePredicate = string -> bool
/// <summary>
/// A Synchronous function that takes the content of the file and its extension
/// and returns the processed content and the new extension after processing the file
/// </summary>
/// <remarks>
/// If the content was not modified for one reason or another, the function should return
/// the FileTransform argument and log to the console the error rather than crashing
/// </remarks>
type Transform = FileTransform -> FileTransform
/// <summary>
/// A Task&lt;'T> based asynchronous function that takes the content of the file and its extension
/// and returns the processed content and the new extension after processing the file
/// </summary>
/// <remarks>
/// If the content was not modified for one reason or another, the function should return
/// the FileTransform argument and log to the console the error rather than crashing
/// </remarks>
type TransformTask = FileTransform -> Task<FileTransform>
/// <summary>
/// An Async&lt;'T> based asynchronous function that takes the content of the file and its extension
/// and returns the processed content and the new extension after processing the file
/// </summary>
/// <remarks>
/// If the content was not modified for one reason or another, the function should return
/// the FileTransform argument and log to the console the error rather than crashing
/// </remarks>
type TransformAsync = FileTransform -> Async<FileTransform>
/// <summary>
/// A ValueTask&lt;'T> based function that takes the content of the file and its extension
/// and returns the processed content and the new extension after processing the file
/// </summary>
/// <remarks>
/// If the content was not modified for one reason or another, the function should return
/// the FileTransform argument and log to the console the error rather than crashing
/// </remarks>
/// <remarks>
/// The plugin author is unlikely to need to use
/// this type as this is used by Perla internals to blend Async/Task and synchronous
/// file transforms, that being said it can be used in the same way as the other Transform functions
/// </remarks>
type TransformAction = FileTransform -> ValueTask<FileTransform>

type PluginFunctions =
  | ShouldProcessFile of shouldProcessFile: FilePredicate
  | Transform of transform: TransformAction
  | ShouldLoad of shouldLoad: FilePredicate

type PluginInfo = {
  name: string
  shouldProcessFile: FilePredicate voption
  transform: TransformAction voption
}

type RunnablePlugin = {
  plugin: PluginInfo
  shouldTransform: FilePredicate
  transform: TransformAction
}

type PerlaPluginBuilder =
  new: name: string -> PerlaPluginBuilder
  member Yield: 'a -> 'b list

  [<CustomOperation "should_process_file">]
  member inline WithTransformProcess:
    state: PluginFunctions list * [<InlineIfLambda>] extension: (string -> bool) ->
      PluginFunctions list

  [<CustomOperation "with_transform">]
  member inline WithTransform:
    state: PluginFunctions list * [<InlineIfLambda>] transform: TransformAction ->
      PluginFunctions list

  [<CustomOperation "with_transform">]
  member inline WithTransform:
    state: PluginFunctions list * [<InlineIfLambda>] transform: Transform ->
      PluginFunctions list

  [<CustomOperation "with_transform">]
  member inline WithTransform:
    state: PluginFunctions list * [<InlineIfLambda>] transform: TransformTask ->
      PluginFunctions list

  [<CustomOperation "with_transform">]
  member inline WithTransform:
    state: PluginFunctions list * [<InlineIfLambda>] transform: TransformAsync ->
      PluginFunctions list

  member Run: state: PluginFunctions list -> PluginInfo

[<AutoOpen;
  CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Plugin =
  /// <summary>
  /// This is the Perla Plugins builder, there should be a single plugin call at
  /// the end of an F# file, for the moment only one value of each async operation will be
  /// picked by the builder
  /// </summary>
  /// <example>
  /// <code lang="fsharp">
  ///    plugin "json-to-text" {
  ///      // This plugin will convert all files that end with .json
  ///      // into text files
  ///      should_process_file (fun file -> file.extension = ".json")
  ///      with_transform (fun file -> { content = content; extension = ".txt"})
  ///    }
  /// </code>
  /// </example>
  /// <example>
  /// <code lang="fsharp">
  ///    plugin "json-to-text" {
  ///      // Don't provide more than one function, the others will be ignored
  ///      should_process_file (fun file -> file.extension = ".json")
  ///      // this won't be used by Perla internals
  ///      should_process_file (fun file -> file.extension = ".module.json")
  ///      with_transform (fun file -> { content = content; extension = ".txt"})
  ///      // this won't be used by Perla internals
  ///      with_transform (fun file -> { content = $"export default {content};"; extension = ".js"})
  ///    }
  /// </code>
  /// </example>
  /// <param name="name">
  /// The name that represents this plugin, this name will be used
  /// to to tag diagnostigs in the console logs
  /// </param>
  val plugin: name: string -> PerlaPluginBuilder
