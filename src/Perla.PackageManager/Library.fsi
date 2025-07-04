namespace Perla.PackageManager

open System
open System.Threading.Tasks
open Perla.PackageManager.Types
open Perla.PackageManager.Jspm
open System.Runtime.InteropServices

[<AutoOpen; Obsolete>]
module PackageManager =

  /// When installing a package use this option
  /// to specify that kind of Dependency it is
  [<Struct; RequireQualifiedAccess>]
  type EnvTarget =
    /// Dependencies that must be present for the website to work
    | Production
    /// Dependencies that are useful just for development and not
    /// required in production
    | Development
    /// Dependencies that are useful just for testing and not
    /// required in production
    | Testing

  [<Class>]
  type PackageManager =

    /// <summary>
    /// Uses JSPM Generator API to generate a new import map based on the package names that were povided
    /// </summary>
    /// <remarks>
    /// An existing import map can be provided to respect existing resolutions and scopes in it.
    /// </remarks>
    /// <remarks>
    /// If the package already exists in the import map, this function will not replace it.
    /// </remarks>
    /// <remarks>
    /// This method will try to produce a flattened scope to make it easier to edit by hand for most users.
    /// </remarks>
    /// <param name="packages">The package names to add</param>
    /// <param name="importMap">An existing map to keep existing resolutions intact if possible.</param>
    /// <param name="environments">JSPM Generator Environment hints to provide the best assets possible</param>
    /// <param name="provider">The current dependency list, this list is useful to differentiate between dev/prod/testing dependencies</param>
    static member AddJspm:
      packages: seq<string> *
      environments: seq<GeneratorEnv> *
      [<Optional>] ?importMap: ImportMap *
      [<Optional>] ?provider: Provider ->
        Task<Result<ImportMap, string>>

    /// <summary>
    /// Uses JSPM Generator API to generate a new import map based on the name that was used for the dependency
    /// </summary>
    /// <remarks>
    /// An existing import map can be provided to respect existing resolutions and scopes in it.
    /// </remarks>
    /// <remarks>
    /// If the package already exists in the import map, this function will not replace it.
    /// </remarks>
    /// <remarks>
    /// This method will try to produce a flattened scope to make it easier to edit by hand for most users.
    /// </remarks>
    /// <param name="package">The package name to add</param>
    /// <param name="importMap">An existing map to keep existing resolutions intact if possible.</param>
    /// <param name="environments">JSPM Generator Environment hints to provide the best assets possible</param>
    /// <param name="provider">The current dependency list, this list is useful to differentiate between dev/prod/testing dependencies</param>
    static member AddJspm:
      package: string *
      environments: seq<GeneratorEnv> *
      [<Optional>] ?importMap: ImportMap *
      [<Optional>] ?provider: Provider ->
        Task<Result<ImportMap, string>>

    /// <summary>
    /// Uses the Skypack API and the JSPM Generator API to generate a new import map based on the name that was used for the dependency.
    /// </summary>
    /// <remarks>
    /// An existing import map can be provided to respect existing resolutions and scopes in it.
    /// </remarks>
    /// <remarks>
    /// If the package already exists in the import map, this function will replace it.
    /// </remarks>
    /// <remarks>
    /// This method will try to produce a flattened scope to make it easier to edit by hand for most users.
    /// </remarks>
    /// <param name="packages">The package names to add</param>
    /// <param name="envTarget">This affects which URLs are used for the package being installed</param>
    /// <param name="importMap">An existing map to keep existing resolutions intact if possible.</param>
    /// <param name="esVersion">In case you need to support older browsers, you can pass a valid Ecmascript version. Example: es2020</param>
    static member AddSkypack:
      packages: seq<string> *
      envTarget: EnvTarget *
      [<Optional>] ?importMap: ImportMap *
      [<Optional>] ?esVersion: string ->
        Task<Result<ImportMap, string>>

    /// <summary>
    /// Uses the Skypack API and the JSPM Generator API to generate a new import map based on the name that was used for the dependency.
    /// </summary>
    /// <remarks>
    /// An existing import map can be provided to respect existing resolutions and scopes in it.
    /// </remarks>
    /// <remarks>
    /// If the package already exists in the import map, this function will replace it.
    /// </remarks>
    /// <remarks>
    /// This method will try to produce a flattened scope to make it easier to edit by hand for most users.
    /// </remarks>
    /// <param name="package">The package name to add</param>
    /// <param name="envTarget">This affects which URLs are used for the package being installed</param>
    /// <param name="importMap">An existing map to keep existing resolutions intact if possible.</param>
    /// <param name="esVersion">In case you need to support older browsers, you can pass a valid Ecmascript version. Example: es2020</param>
    static member AddSkypack:
      package: string *
      envTarget: EnvTarget *
      [<Optional>] ?importMap: ImportMap *
      [<Optional>] ?esVersion: string ->
        Task<Result<ImportMap, string>>

    /// <summary>
    /// Uses JSPM Generator API to generate a new import map based on the package names that were povided
    /// </summary>
    /// <remarks>
    /// This method will try to produce a flattened scope to make it easier to edit by hand for most users.
    /// </remarks>
    /// <param name="packages">An sequence of packages to install.</param>
    /// <param name="environments">JSPM Generator Environment hints to provide the best assets possible</param>
    /// <param name="provider">The current dependency list, this list is useful to differentiate between dev/prod/testing dependencies</param>
    /// <param name="importMap">An existing import map to respect custom resolutions</param>
    static member Regenerate:
      packages: seq<string> *
      environments: seq<GeneratorEnv> *
      [<Optional>] ?provider: Provider *
      [<Optional>] ?importMap: ImportMap ->
        Task<Result<InstallResponse, string>>
