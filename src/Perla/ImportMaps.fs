namespace Perla

open System.Text.RegularExpressions
open FSharp.UMX
open FSharp.Data.Adaptive
open Perla
open Perla.Types
open Perla.Units
open Perla.PkgManager
open Perla.Plugins.Plugin

/// Result type for resolved import map and externals
type ImportMapResolution = {
  importMap: ImportMap
  externals: string list
}

module ImportMaps =
  open System
  open System.IO
  open System.Collections.Generic
  // Checks if a path is a relative path (starts with ./ or ../ or similar patterns)
  let isRelativePath(path: string) =
    // Normalize separators to forward slashes for consistency
    let path = path.Replace('\\', '/')

    // Check if the path is not rooted (absolute)
    not(System.IO.Path.IsPathRooted(path))
    &&
    // Optionally check that it doesn't start with Windows-style drive letter (C:\ or C:/)
    not(Regex.IsMatch(path, @"^[a-zA-Z]:[/\\]"))
    &&
    // Basic sanity check that it's not empty or whitespace
    not(String.IsNullOrWhiteSpace(path))


  let withPaths
    (paths: Map<string<BareImport>, string<ResolutionUrl>>)
    (importMap: ImportMap)
    : ImportMap =
    {
      importMap with
          imports =
            paths
            |> Map.fold
              (fun acc k v -> Map.add (UMX.untag k) (UMX.untag v) acc)
              importMap.imports
    }

  /// Adaptive function: merges config.paths into importMap, both as avals
  let withPathsA (config: PerlaConfig aval) (map: ImportMap aval) =
    AVal.map2
      (fun config importMap -> withPaths config.paths importMap)
      config
      map

  // Private helper: determines if a value is local (covered by a mount)
  let private isLocalImport
    (mounts: Map<string<ServerUrl>, string<UserPath>>)
    (v: string)
    =
    if v.StartsWith("http://") || v.StartsWith("https://") then
      false
    elif v.StartsWith("/") then
      mounts
      |> Map.keys
      |> Seq.exists(fun mount -> v.StartsWith(UMX.untag mount))
    elif v.StartsWith("./") then
      let normalized = "/" + v.Substring(2)

      mounts
      |> Map.keys
      |> Seq.exists(fun mount -> normalized.StartsWith(UMX.untag mount))
    else
      false

  let cleanupLocalPaths
    (mounts: Map<string<ServerUrl>, string<UserPath>>)
    (importMap: ImportMap)
    : ImportMap =
    {
      importMap with
          imports =
            importMap.imports
            |> Map.filter(fun _k v -> not(isLocalImport mounts v))
    }

  /// Extracts all external import specifiers from an ImportMap (from both imports and scopes)
  let getExternals(importMap: ImportMap) =
    let importKeys = importMap.imports |> Map.toSeq |> Seq.map fst

    let scopeKeys =
      importMap.scopes |> Map.values |> Seq.collect(Map.toSeq >> Seq.map fst)

    Seq.append importKeys scopeKeys |> Seq.distinct |> Seq.toList

  /// Adaptive function to resolve the import map and externals for the build process
  /// - configA: PerlaConfig aval
  /// - importMapA: ImportMap aval
  /// - esbuildPresentA: aval<bool>
  /// Returns: aval<ImportMapResolution>
  let resolveForBuildA
    (configA: PerlaConfig aval)
    (importMapA: ImportMap aval)
    : aval<ImportMapResolution> =
    adaptive {
      let! config = configA
      let! importMap = importMapA

      let esbuildPresent =
        config.plugins
        |> List.exists(fun p ->
          p.Equals(
            Constants.PerlaEsbuildPluginName,
            StringComparison.InvariantCultureIgnoreCase
          ))

      // Helper: is a value an external URL
      let isExternal(v: string) =
        v.StartsWith("http://") || v.StartsWith("https://")

      // Clean up local paths from the import map (convert to relative if needed)
      let cleanedImportMap = cleanupLocalPaths config.mountDirectories importMap

      // Get all externals (bare specifiers) from the cleaned import map
      let allExternals = getExternals cleanedImportMap

      // Determine which externals to keep based on config and esbuild presence
      let externals =
        if not esbuildPresent then
          []
        elif config.useLocalPkgs then
          allExternals
          |> List.filter(fun spec ->
            cleanedImportMap.imports
            |> Map.tryFind spec
            |> Option.exists(fun v ->
              not(isLocalImport config.mountDirectories v)))
        else
          allExternals
          |> List.filter(fun spec ->
            cleanedImportMap.imports
            |> Map.tryFind spec
            |> Option.exists isExternal)

      // The import map should always have local paths cleaned up
      // (already done above)
      // If there are no imports left, also clear scopes
      let finalImportMap =
        if Map.isEmpty cleanedImportMap.imports then
          {
            cleanedImportMap with
                scopes = Map.empty
          }
        else
          cleanedImportMap

      return {
        importMap = finalImportMap
        externals = externals
      }
    }
