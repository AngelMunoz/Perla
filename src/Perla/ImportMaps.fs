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
    not(System.String.IsNullOrWhiteSpace(path))


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

  /// Replaces module names in import statements using the provided paths map.
  /// Returns the modified code string with replacements applied.
  let replaceImports
    (paths: Map<string<BareImport>, string<ResolutionUrl>>)
    (importingFile: string)
    (sourcesRoot: string<SystemPath>)
    (content: string)
    : string =
    let pattern =
      "import\\s+(?:.+?\\s+from\\s+['\"]([^'\"]+)['\"]|['\"]([^'\"]+)['\"])|import\\s*\\(\\s*(['\"])([^'\"]+)\\3\\s*([,)])"

    let extractModuleName(m: System.Text.RegularExpressions.Match) : string =
      if m.Groups[1].Success then m.Groups[1].Value
      elif m.Groups[2].Success then m.Groups[2].Value
      elif m.Groups[4].Success then m.Groups[4].Value
      else ""

    let computeRelativeImport
      (importingDir: string)
      (replacementStr: string)
      (rest: string)
      : string =
      let target =
        if replacementStr.StartsWith("./") then
          replacementStr.Substring(2)
        elif replacementStr.StartsWith("../") then
          replacementStr
        else
          replacementStr
      // Compute absolute paths
      let importingDirAbs =
        let dir =
          if System.String.IsNullOrWhiteSpace(importingDir) then
            UMX.untag sourcesRoot
          elif System.IO.Path.IsPathRooted(importingDir) then
            importingDir
          else
            System.IO.Path.Combine(UMX.untag sourcesRoot, importingDir)

        System.IO.Path.GetFullPath(dir)

      let targetAbs =
        System.IO.Path.GetFullPath(
          System.IO.Path.Combine(UMX.untag sourcesRoot, target)
        )

      let rel =
        System.IO.Path
          .GetRelativePath(importingDirAbs, targetAbs)
          .Replace('\\', '/')

      let rel =
        if rel.StartsWith(".") || rel.StartsWith("/") then
          rel
        else
          "./" + rel

      let rel = rel.TrimEnd('/')
      let rest = rest.TrimStart('/')
      if rest = "" then rel else rel + "/" + rest

    let computeNewImport
      (importingDir: string)
      (moduleName: string)
      (prefixStr: string)
      (replacementStr: string)
      : string =
      let rest = moduleName.Substring(prefixStr.Length)

      if
        isRelativePath replacementStr
        && not(System.String.IsNullOrWhiteSpace importingDir)
      then
        computeRelativeImport importingDir replacementStr rest
      else
        replacementStr + rest

    let replaceMatch(m: System.Text.RegularExpressions.Match) : string =
      let moduleName = extractModuleName m

      // Ensure importingFile is absolute, fallback to sourcesRoot if not
      let importingFileAbs =
        if System.IO.Path.IsPathRooted(importingFile) then
          importingFile
        else
          System.IO.Path.Combine(UMX.untag sourcesRoot, importingFile)
          |> System.IO.Path.GetFullPath

      let importingDir =
        System.IO.Path.GetDirectoryName(importingFileAbs) |> nonNull

      let tryReplace
        (prefix: string<BareImport>, replacement: string<ResolutionUrl>)
        : string option =
        let prefixStr, replacementStr = UMX.untag prefix, UMX.untag replacement

        if moduleName.StartsWith(prefixStr) then
          let newImport =
            computeNewImport importingDir moduleName prefixStr replacementStr

          Some(m.Value.Replace(moduleName, newImport))
        else
          None

      paths
      |> Map.toSeq
      |> Seq.tryPick tryReplace
      |> Option.defaultValue m.Value

    Regex.Replace(content, pattern, replaceMatch)

  /// Creates a PluginInfo for the perla-paths-replacer-plugin
  let createPathsReplacerPlugin
    (pathsA: Map<string<BareImport>, string<ResolutionUrl>> aval)
    (sourcesRoot: string<SystemPath>)
    : Perla.Plugins.PluginInfo =
    let shouldTransform ext =
      [ ".js"; ".ts"; ".jsx"; ".tsx" ] |> List.contains ext

    let transform: Plugins.Transform =
      fun file ->
        let paths = pathsA |> AVal.force

        let replaced =
          replaceImports paths file.fileLocation sourcesRoot file.content

        { file with content = replaced }

    plugin Constants.PerlaPathsReplacerPluginName {
      should_process_file shouldTransform
      with_transform transform
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
