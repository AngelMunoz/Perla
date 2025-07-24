namespace Perla.Build

open System

open AngleSharp
open AngleSharp.Html.Dom

open Perla
open Perla.Types
open Perla.Units
open Perla.Logger

open FSharp.UMX
open FsToolkit.ErrorHandling

open FSharp.Data.Adaptive
open IcedTasks
open System.IO
open Microsoft.Extensions.Logging
open AngleSharp.Html.Parser

type BuildServiceArgs = {
  Logger: Microsoft.Extensions.Logging.ILogger
  FsManager: Perla.FileSystem.PerlaFsManager
  EsbuildService: Perla.Esbuild.EsbuildService
  ExtensibilityService: Perla.Extensibility.ExtensibilityService
  VirtualFileSystem: Perla.VirtualFs.VirtualFileSystem
  FableService: Perla.Fable.FableService
  Directories: PerlaDirectories
}

type BuildOptions = { enablePreview: bool }

type EsbuildOutput = {
  outputDir: string<SystemPath>
  cssFiles: string<ServerUrl> seq
  jsFiles: string<ServerUrl> seq
}

[<Interface>]
type BuildService =
  abstract RunFable: config: PerlaConfig aval -> CancellableTask<unit>

  abstract CleanOutput: config: PerlaConfig aval -> unit

  abstract LoadPlugins:
    config: PerlaConfig aval * vfsOutputDir: string<SystemPath> -> unit

  abstract LoadVfs: config: PerlaConfig aval -> CancellableTask<unit>

  abstract CopyVfsToDisk:
    vfsOutputDir: string<SystemPath> -> CancellableTask<string<SystemPath>>

  abstract EmitEnvFile:
    config: PerlaConfig aval * tempDir: string<SystemPath> -> unit

  abstract RunEsbuild:
    config: PerlaConfig aval *
    tempDir: string<SystemPath> *
    cssPaths: seq<string<ServerUrl>> *
    jsBundleEntrypoints: seq<string<ServerUrl>> *
    externals: string list ->
      CancellableTask<EsbuildOutput>

  abstract MoveOrCopyOutput:
    config: PerlaConfig aval *
    tempDir: string<SystemPath> *
    esbuildOutput: string<SystemPath> ->
      unit

  abstract WriteIndex:
    config: PerlaConfig aval *
    document: AngleSharp.Html.Dom.IHtmlDocument *
    map: Perla.PkgManager.ImportMap *
    jsPaths: seq<string<ServerUrl>> *
    cssPaths: seq<string<ServerUrl>> *
    esbuildCssFiles: seq<string<ServerUrl>> ->
      CancellableTask<unit>

[<RequireQualifiedAccess>]
module Build =

  let EnsureBody(document: IHtmlDocument) =
    match document.Body with
    | null ->
      let b = document.CreateElement("body")
      document.AppendChild(b) |> ignore
      b
    | body -> body

  let EnsureHead(document: IHtmlDocument) =
    match document.Head with
    | null ->
      let h = document.CreateElement("head")
      document.InsertBefore(h, document.Body) |> ignore
      h
    | head -> head

  let insertCssFiles
    (document: IHtmlDocument, cssEntryPoints: string<ServerUrl> seq)
    =
    let head = EnsureHead document

    for file in cssEntryPoints do
      let style = document.CreateElement("link")
      style.SetAttribute("rel", "stylesheet")
      style.SetAttribute("href", UMX.untag file)
      style |> head.AppendChild |> ignore

  let insertImportMap
    (document: IHtmlDocument, importMap: PkgManager.ImportMap)
    =
    let head = EnsureHead document
    let script = document.CreateElement("script")
    script.SetAttribute("type", "importmap")
    script.TextContent <- importMap.ToJson()
    head.AppendChild(script) |> ignore

  let insertJsFiles
    (document: IHtmlDocument, jsEntryPoints: string<ServerUrl> seq)
    =
    let body = EnsureBody document

    for entryPoint in jsEntryPoints do
      let script = document.CreateElement("script")
      script.SetAttribute("type", "module")
      script.SetAttribute("src", UMX.untag entryPoint)
      body.AppendChild(script) |> ignore

  let EntryPoints(document: IHtmlDocument) =
    let cssBundles =
      document.QuerySelectorAll("[data-entry-point][rel=stylesheet]")
      |> Seq.choose(fun el -> option {
        let! href = el.Attributes["href"]

        if String.IsNullOrWhiteSpace href.Value then
          return! None
        else
          return UMX.tag<ServerUrl> href.Value
      })

    let jsBundles =
      document.QuerySelectorAll("[data-entry-point][type=module]")
      |> Seq.choose(fun el -> option {
        let! dataEntryPoint = el.Attributes["data-entry-point"]
        let! entryPoint = dataEntryPoint.Value

        if entryPoint = "standalone" then
          return! None
        else
          let! src = el.Attributes["src"]
          return UMX.tag<ServerUrl> src.Value
      })

    let standaloneBundles =
      document.QuerySelectorAll("[data-entry-point=standalone][type=module]")
      |> Seq.choose(fun el -> option {
        let! src = el.Attributes["src"]

        if String.IsNullOrWhiteSpace src.Value then
          return! None
        else
          return UMX.tag<ServerUrl> src.Value
      })

    cssBundles, jsBundles, standaloneBundles

  let Externals(config: PerlaConfig) = seq {

    if config.enableEnv && config.build.emitEnvFile then
      UMX.untag config.envPath
      Constants.EnvBareImport

    yield! config.esbuild.externals
  }

  let Index
    (
      document: IHtmlDocument,
      importMap: PkgManager.ImportMap,
      jsExtras: string<ServerUrl> seq,
      cssExtras: string<ServerUrl> seq
    ) =

    insertCssFiles(document, cssExtras)

    // importmap needs to go first
    insertImportMap(document, importMap)

    // remove any existing entry points, we don't need them at this point
    document.QuerySelectorAll("[data-entry-point][type=module]")
    |> Seq.iter(_.Remove())

    document.QuerySelectorAll("[data-entry-point=standalone][type=module]")
    |> Seq.iter(_.Remove())

    document.QuerySelectorAll("[data-entry-point][rel=stylesheet]")
    |> Seq.iter(_.Remove())

    // insert the resolved entry points which should match paths in mounted directories
    insertJsFiles(document, jsExtras)

    document.Minify()

  let collectFilesFromDirectory(outputDir: string<SystemPath>) =
    let dir = DirectoryInfo(UMX.untag outputDir)

    let allFiles = dir.GetFiles("*.*", SearchOption.AllDirectories)

    let toServerUrl(file: FileInfo) =
      let relativePath =
        Path.GetRelativePath(UMX.untag outputDir, file.FullName)

      let serverPath = "/" + relativePath.Replace("\\", "/")
      UMX.tag<ServerUrl> serverPath

    let cssFiles =
      allFiles
      |> Seq.filter(fun file ->
        file.Extension.Equals(".css", StringComparison.OrdinalIgnoreCase))
      |> Seq.map toServerUrl

    let jsFiles =
      allFiles
      |> Seq.filter(fun file ->
        file.Extension.Equals(".js", StringComparison.OrdinalIgnoreCase))
      |> Seq.map toServerUrl

    cssFiles, jsFiles

  /// Checks for missing local dependencies referenced in the import map (from both imports and scopes).
  /// Returns a list of missing package@version strings.
  let getMissingLocalDependencies
    (config: PerlaConfig)
    (importMap: Perla.PkgManager.ImportMap)
    : string list =
    if not config.useLocalPkgs then
      []
    else
      let tryExtractPkgVer(path: string) =
        let marker = "/node_modules/.perla/"
        let idx = path.IndexOf(marker)

        if idx >= 0 then
          let rest = path.Substring(idx + marker.Length)

          let parts =
            rest.Split([| '/' |], System.StringSplitOptions.RemoveEmptyEntries)

          if parts.Length = 0 then
            None
          elif parts.[0].StartsWith("@") && parts.Length >= 2 then
            // Scoped package: join first two segments
            Some(parts.[0] + "/" + parts.[1])
          else
            // Unscoped package: just the first segment
            Some(parts.[0])
        else
          None

      let allPkgVers =
        importMap.imports.Values
        |> Seq.append(
          importMap.scopes |> Seq.collect(fun kv -> kv.Value.Values)
        )
        |> Seq.choose tryExtractPkgVer
        |> Seq.distinct
        |> Seq.toList

      let cwd = System.IO.Directory.GetCurrentDirectory()

      let getTopLevelNodeModulesPath (cwd: string) (pkgVer: string) =
        if pkgVer.StartsWith("@") then
          // Scoped: @scope/name@version
          let atIdx = pkgVer.IndexOf("@", 1) // skip first char

          if atIdx > 0 then
            let scope = pkgVer.Substring(0, atIdx)
            let nameAndVersion = pkgVer.Substring(atIdx + 1)
            let nameEndIdx = nameAndVersion.IndexOf("@")

            if nameEndIdx > 0 then
              let name = nameAndVersion.Substring(0, nameEndIdx)
              System.IO.Path.Combine(cwd, "node_modules", scope, name)
            else
              System.IO.Path.Combine(cwd, "node_modules", scope)
          else
            System.IO.Path.Combine(cwd, "node_modules", pkgVer)
        else
          // Unscoped: name@version
          let nameEndIdx = pkgVer.IndexOf("@")

          let name =
            if nameEndIdx > 0 then
              pkgVer.Substring(0, nameEndIdx)
            else
              pkgVer

          System.IO.Path.Combine(cwd, "node_modules", name)

      allPkgVers
      |> List.filter(fun pkgVer ->
        let topLevelPath = getTopLevelNodeModulesPath cwd pkgVer

        not(
          System.IO.Directory.Exists(topLevelPath)
          || System.IO.File.Exists(topLevelPath)
        ))

module BuildService =
  let Create(args: BuildServiceArgs) : BuildService =
    { new BuildService with
        member _.RunFable(config) = cancellableTask {
          let config = config |> AVal.force

          match config.fable with
          | None ->
            args.Logger.LogWarning(
              "Fable configuration not found. Skipping Fable build."
            )

            return ()
          | Some fableConfig ->
            args.Logger.LogInformation(
              "Fable configuration found. Running Fable build."
            )

            do! args.FableService.Run fableConfig
        }

        member _.CleanOutput(config) =
          let config = config |> AVal.force
          let outDir = DirectoryInfo(UMX.untag config.build.outDir)

          try
            outDir.Delete(true)
          with ex ->
            args.Logger.LogWarning(
              "Failed to clean output directory {path}: {error}",
              outDir.FullName,
              ex.Message
            )

        member _.LoadPlugins(config, vfsOutputDir) =
          let config = config |> AVal.force
          let plugins = args.FsManager.ResolvePluginPaths()

          let isEsbuildPluginPresent =
            config.plugins |> List.contains Constants.PerlaEsbuildPluginName

          let isPathsReplacerPresent =
            config.plugins
            |> List.contains Constants.PerlaPathsReplacerPluginName

          let defaultPlugins = seq {
            if isPathsReplacerPresent || not(Map.isEmpty config.paths) then
              ImportMaps.createPathsReplacerPlugin
                (AVal.constant config.paths)
                vfsOutputDir

            if isEsbuildPluginPresent then
              args.EsbuildService.GetPlugin config.esbuild
          }

          args.ExtensibilityService.LoadPlugins(plugins, defaultPlugins)
          |> Result.teeError(fun err ->
            args.Logger.LogError("Failed to load plugins: {error}", err))
          |> Result.ignore
          |> Result.ignoreError

        member _.LoadVfs(config) = cancellableTask {
          let config = config |> AVal.force
          return! args.VirtualFileSystem.Load config.mountDirectories
        }

        member _.CopyVfsToDisk(vfsOutputDir) = cancellableTask {
          return! args.VirtualFileSystem.ToDisk vfsOutputDir
        }

        member _.EmitEnvFile(config, tempDir) =
          let config = config |> AVal.force

          if config.build.emitEnvFile then
            args.Logger.LogInformation("Writing Env File")
            args.FsManager.EmitEnvFile(config, tempDir)

        member _.RunEsbuild
          (config, tempDir, cssPaths, jsBundleEntrypoints, externals)
          =
          cancellableTask {
            let config = config |> AVal.force
            let esbuildOutput = Path.Combine(UMX.untag tempDir, "esbuild")

            Directory.CreateDirectory esbuildOutput |> ignore

            let isEsbuildPluginPresent =
              config.plugins |> List.contains Constants.PerlaEsbuildPluginName

            if not isEsbuildPluginPresent then
              return {
                outputDir = UMX.tag esbuildOutput
                cssFiles = Seq.empty
                jsFiles = Seq.empty
              }
            else

            for entrypoint in jsBundleEntrypoints do
              do!
                args.EsbuildService.ProcessJS(
                  entrypoint,
                  tempDir,
                  UMX.tag esbuildOutput,
                  {
                    config.esbuild with
                        externals =
                          externals @ (Build.Externals config |> Seq.toList)
                  }
                )

            for entrypoint in cssPaths do
              do!
                args.EsbuildService.ProcessCss(
                  entrypoint,
                  tempDir,
                  UMX.tag esbuildOutput,
                  config.esbuild
                )

            // Collect CSS and JS files generated by esbuild
            let cssFiles, jsFiles =
              Build.collectFilesFromDirectory(UMX.tag esbuildOutput)

            args.Logger.LogDebug(
              "Found {CssFileCount} CSS files in esbuild output",
              cssFiles |> Seq.length
            )

            args.Logger.LogDebug(
              "Found {JsFileCount} JS files in esbuild output",
              jsFiles |> Seq.length
            )

            return {
              outputDir = UMX.tag esbuildOutput
              cssFiles = cssFiles
              jsFiles = jsFiles
            }
          }

        member _.MoveOrCopyOutput(config, tempDir, esbuildOutputPath) =
          let config = config |> AVal.force

          let outDir =
            Path.Combine(
              UMX.untag args.Directories.CurrentWorkingDirectory,
              UMX.untag config.build.outDir
            )


          let isEsbuildPluginPresent =
            config.plugins |> List.contains Constants.PerlaEsbuildPluginName

          if isEsbuildPluginPresent then
            args.Logger.LogDebug(
              "Copying all files from esbuild output to outDir (esbuild present)"
            )

            args.Logger.LogDebug("{from} -> {to}", esbuildOutputPath, outDir)

            args.FsManager.CopyFiles(
              DirectoryInfo(UMX.untag esbuildOutputPath),
              UMX.tag outDir
            )
          else
            args.Logger.LogDebug(
              "Copying all files from tempDir to outDir (esbuild not present)"
            )

            args.FsManager.CopyFiles(
              DirectoryInfo(UMX.untag tempDir),
              UMX.tag outDir
            )

          // globs are always copied
          args.FsManager.CopyGlobs(config.build, tempDir)

        member _.WriteIndex
          (config, document, map, jsPaths, cssPaths, esbuildCssFiles)
          =
          cancellableTask {
            let! token = CancellableTask.getCancellationToken()
            let config = config |> AVal.force

            // Helper function to normalize paths for comparison
            let normalizePath(path: string<ServerUrl>) =
              let pathStr = UMX.untag path
              // Remove leading ./ and normalize to start with /
              if pathStr.StartsWith("./") then "/" + pathStr.Substring(2)
              elif pathStr.StartsWith("/") then pathStr
              else "/" + pathStr

            // Log the paths for debugging
            args.Logger.LogDebug("Original CSS paths: {Paths}", cssPaths)

            args.Logger.LogDebug("Esbuild CSS files: {Paths}", esbuildCssFiles)

            // Combine original CSS paths with esbuild-generated CSS files, avoiding duplicates
            let uniqueCssPaths =
              let existingCssSet =
                cssPaths |> Seq.map normalizePath |> Set.ofSeq

              let esbuildCssSet =
                esbuildCssFiles |> Seq.map normalizePath |> Set.ofSeq

              args.Logger.LogDebug(
                "Normalized existing CSS paths: {Paths}",
                existingCssSet
              )

              args.Logger.LogDebug(
                "Normalized esbuild CSS paths: {Paths}",
                esbuildCssSet
              )

              // Find esbuild CSS files that aren't already in the original CSS paths
              let newCssFiles = Set.difference esbuildCssSet existingCssSet

              args.Logger.LogDebug("New CSS files to add: {Paths}", newCssFiles)

              // Convert new files back to original format and append to original paths
              let newCssUrls =
                newCssFiles
                |> Set.map(fun normalizedPath ->
                  // Find the original esbuild path that normalizes to this path
                  esbuildCssFiles
                  |> Seq.find(fun path -> normalizePath path = normalizedPath))

              Seq.append cssPaths newCssUrls

            let indexContent =
              Build.Index(document, map, jsPaths, uniqueCssPaths)

            let outPath =
              Path.Combine(UMX.untag config.build.outDir, "index.html")

            // Ensure the output directory exists before writing
            Directory.CreateDirectory(UMX.untag config.build.outDir) |> ignore

            do! File.WriteAllTextAsync(outPath, indexContent, token)
          }
    }
