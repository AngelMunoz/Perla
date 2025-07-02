module Perla.PackageManager.Jspm

open System.Threading.Tasks

/// Enlists the dependencies of a given file in the graph
/// when it is present.
type FileDependencies = {
  staticDeps: seq<string> option
  dynamicDeps: seq<string> option
}

type File = Map<string, FileDependencies>

type DependencyGraph = Map<string, File>

/// The Jspm Generator API response when calling the generator endpoint
/// this provides the import map that results from the packages requested to install
type InstallResponse = {

  /// List of URLs that can be preloaded in the browser
  /// These dependencies are known to be part of the import map dependency graph
  staticDeps: seq<string>
  dynamicDeps: seq<string>

  /// the resulting import map from the install request to the generator endpoint
  map: Types.ImportMap

  /// A dependency graph describing how each dependency is pulling each file
  graph: DependencyGraph option
} with

  /// <summary>
  /// Writes an HTML string that exemplifies how to use the current install result
  /// </summary>
  /// <param name="esModulesShim">Include the polyfill to enable import maps</param>
  /// <param name="indentMap">Write the map with indentation or minified, by default the map is minified</param>
  /// <param name="preload">Include the link statements with module preload as the rel attribute</param>
  /// <returns>An string with HTML content</returns>
  member ToHtml:
    ?esModulesShim: bool * ?indentMap: bool * ?preload: bool -> string

  member ToJson: ?indented: bool -> string

[<Class>]
type JspmGenerator =

  /// <summary>
  /// Makes an HTTP Request to the Jspm Generator API to generate an import map with the packages
  /// provided, the response can be an <see cref="Perla.PackageManager.InstallResponse">Install Response</see>
  /// or an error string depending on the success of the operation.
  /// </summary>
  /// <param name="packages">The packages that will be installed</param>
  /// <param name="environments">The kind of targets this import map is targeted</param>
  /// <param name="provider">Which CDN will be used to pull the imports from</param>
  /// <param name="inputMap">
  /// If you have custom mappings or an existing import map,
  /// you can pass it to ensure your resolutions are respected where possible
  /// </param>
  /// <param name="flattenScope">Avoid using scopes where possible and only use single URLs</param>
  /// <param name="graph">Include the dependency graph for this install request</param>
  static member Install:
    packages: seq<string> *
    ?environments: seq<Types.GeneratorEnv> *
    ?provider: Types.Provider *
    ?inputMap: Types.ImportMap *
    ?flattenScope: bool *
    ?graph: bool ->
      Task<Result<InstallResponse, string>>
