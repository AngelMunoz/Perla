namespace Perla.PackageManager

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

[<Extension; Class>]
type DictionaryExtensions =

  [<Extension>]
  static member ToSeq:
    dictionary: Dictionary<'TKey, 'TValue> -> seq<'TKey * 'TValue>

module Types =

  /// <summary>
  /// The <see href="https://wicg.github.io/import-maps">Import Map</see> represents
  /// a list of mappings between javascript imports and where they are located,
  /// the scopes refer to the resources that are available under certain dependency trees
  /// </summary>
  [<Obsolete("Use PkgManager.ImportMap instead")>]
  type ImportMap = {
    imports: Map<string, string>
    scopes: Map<string, Map<string, string>> option
  } with

    member ToJson: ?indented: bool -> string

    /// <summary>A C# Friendly function that creates a new import map</summary>
    /// <param name="imports">Sequence of imports and their urls</param>
    /// <param name="scopes">A Sequence that maps a scoping URL to the imports and their urls</param>
    static member CreateMap:
      imports: Dictionary<string, string> *
      ?scopes: Dictionary<string, Dictionary<string, string>> ->
        ImportMap

    /// Tries to Deserialize an importmap json into an ImportMap object
    static member FromString: content: string -> Result<ImportMap, string>

    /// Tries to Deserialize an importmap json into an ImportMap In an asynchronous way
    static member FromStringAsync:
      content: System.IO.Stream ->
        System.Threading.Tasks.Task<Result<ImportMap, string>>

  /// Used primarily for the JspmGenerator client
  /// This signals the Jspm generator API how to resolve
  /// the files and what kind of dependencies to provide
  [<Struct; RequireQualifiedAccess>]
  type GeneratorEnv =
    | Browser
    | Development
    | Production
    | Module
    | Node
    | Deno

    member AsString: string

  /// User primarily for the JspmGenerator client
  /// This signals the JSPM generator API where to pull
  /// the dependencies and the URLs from, in the case o the
  [<Struct; RequireQualifiedAccess>]
  type Provider =
    | Jspm
    | Skypack
    | Unpkg
    | Jsdelivr
    | JspmSystem
    | EsmSh

    member AsString: string

    static member FromString: string -> Provider

module Constants =

  [<Literal>]
  val JSPM_API: string = "https://api.jspm.io/generate"

  [<Literal>]
  val SKYPACK_CDN: string = "https://cdn.skypack.dev"

  [<Literal>]
  val SKYPACK_API: string = "https://api.skypack.dev/v1"

[<AutoOpen>]
module TypeExtensions =
  open Types

  [<Class; Extension>]
  type ImportMapExtensions =

    /// <summary>A C# Friendly function that returns an updated import map with the given imports</summary>
    /// <remarks>This will replace the previous imports and not merge them.</remarks>
    /// <param name="map">Existing import map</param>
    /// <param name="imports">The new imports for this import map</param>
    /// <example>
    /// <code lang="csharp">
    /// // replace all imports with lodash only
    /// var updated = ImportMap.WithScopes(map, new Dictionary&lt;string, string> { { "lodash", "https://unpkg.com/lodash?module" } }));
    /// </code>
    /// </example>
    [<Extension>]
    static member WithImports:
      map: ImportMap * imports: Dictionary<string, string> -> ImportMap

    /// <summary>A C# Friendly function that returns an import map with the given scopes</summary>
    /// <remarks>This will replace the previous scopes and not merge them.</remarks>
    /// <param name="map">An existing import map</param>
    /// <param name="scopes">A Sequence that maps a scoping URL to the imports and their urls</param>
    [<Extension>]
    static member WithScopes:
      map: ImportMap * ?scopes: Dictionary<string, Dictionary<string, string>> ->
        ImportMap
