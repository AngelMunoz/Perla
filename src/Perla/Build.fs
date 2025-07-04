namespace Perla.Build

open System.IO

open AngleSharp
open AngleSharp.Html.Dom

open Perla
open Perla.Types
open Perla.Units
open Perla.FileSystem

open Perla.PackageManager.Types

open Fake.IO.Globbing

open FSharp.UMX
open Spectre.Console
open FsToolkit.ErrorHandling

[<RequireQualifiedAccess>]
module Build =

  let insertCssFiles
    (document: IHtmlDocument, cssEntryPoints: string<ServerUrl> seq)
    =
    for file in cssEntryPoints do
      let style = document.CreateElement("link")
      style.SetAttribute("rel", "stylesheet")
      style.SetAttribute("href", UMX.untag file)
      style |> document.Head.AppendChild |> ignore

  let insertModulePreloads(document: IHtmlDocument, staticDeps: string seq) =
    for dependencyUrl in staticDeps do
      let link = document.CreateElement("link")
      link.SetAttribute("rel", "modulepreload")
      link.SetAttribute("href", dependencyUrl)
      document.Head.AppendChild(link) |> ignore

  let insertImportMap(document: IHtmlDocument, importMap: ImportMap) =
    let script = document.CreateElement("script")
    script.SetAttribute("type", "importmap")
    script.TextContent <- importMap.ToJson()
    document.Head.AppendChild(script) |> ignore

  let insertJsFiles
    (document: IHtmlDocument, jsEntryPoints: string<ServerUrl> seq)
    =
    for entryPoint in jsEntryPoints do
      let script = document.CreateElement("script")
      script.SetAttribute("type", "module")
      script.SetAttribute("src", UMX.untag entryPoint)
      document.Body.AppendChild(script) |> ignore


type Build =

  static member GetIndexFile
    (
      document: IHtmlDocument,
      cssPaths: string<ServerUrl> seq,
      jsPaths: string<ServerUrl> seq,
      importMap: ImportMap,
      ?staticDependencies: string seq,
      ?minify: bool
    ) =

    Build.insertCssFiles(document, cssPaths)

    // importmap needs to go first
    Build.insertImportMap(document, importMap)

    // if we have module preloads
    Build.insertModulePreloads(
      document,
      defaultArg staticDependencies Seq.empty
    )
    // remove any existing entry points, we don't need them at this point
    document.QuerySelectorAll("[data-entry-point][type=module]")
    |> Seq.iter(fun f -> f.Remove())

    document.QuerySelectorAll("[data-entry-point=standalone][type=module]")
    |> Seq.iter(fun f -> f.Remove())

    document.QuerySelectorAll("[data-entry-point][rel=stylesheet]")
    |> Seq.iter(fun f -> f.Remove())

    // insert the resolved entry points which should match paths in mounted directories
    Build.insertJsFiles(document, jsPaths)

    match defaultArg minify false with
    | true -> document.Minify()
    | false -> document.ToHtml()

  static member GetEntryPoints(document: IHtmlDocument) =
    let cssBundles =
      document.QuerySelectorAll("[data-entry-point][rel=stylesheet]")
      |> Seq.choose(fun el -> el.Attributes["href"] |> Option.ofObj)
      |> Seq.map(fun el -> UMX.tag<ServerUrl> el.Value)

    let htmlBundles =
      document.QuerySelectorAll("[data-entry-point][type=module]")
      |> Seq.choose(fun el -> option {
        let! entryPoint = el.Attributes["data-entry-point"].Value

        if entryPoint = "standalone" then
          return! None
        else
          return! el.Attributes["src"] |> Option.ofObj
      })

      |> Seq.map(fun el -> UMX.tag<ServerUrl> el.Value)

    let standaloneBundles =
      document.QuerySelectorAll("[data-entry-point=standalone][type=module]")
      |> Seq.choose(fun el -> el.Attributes["src"] |> Option.ofObj)
      |> Seq.map(fun el -> UMX.tag<ServerUrl> el.Value)

    cssBundles, htmlBundles, standaloneBundles

  static member GetExternals(config: PerlaConfig) =
    let dependencies = config.dependencies

    seq {
      for dependency in dependencies do
        dependency.package

      if config.enableEnv && config.build.emitEnvFile then
        UMX.untag config.envPath
        Constants.EnvBareImport

      yield! config.esbuild.externals
    }

  static member CopyGlobs(config: BuildConfig, tempDir: string<SystemPath>) =

    let outDir = UMX.untag config.outDir |> Path.GetFullPath

    let chooseGlobs (startsWith: string) (contains: string) (glob: string) =
      if glob.StartsWith startsWith then
        Some(glob.Substring startsWith.Length)
      elif not(glob.Contains contains) then
        Some(glob)
      else
        None


    let lfsGlob =

      let localIncludes =
        config.includes |> Seq.choose(chooseGlobs "lfs:" "vfs:") |> Seq.toList

      let localExcludes =
        config.excludes |> Seq.choose(chooseGlobs "lfs:" "vfs:") |> Seq.toList

      {
        BaseDirectory = FileSystem.CurrentWorkingDirectory() |> UMX.untag
        Includes = localIncludes
        Excludes = localExcludes
      }

    let vfsGlob =
      let virtualIncludes =
        config.includes |> Seq.choose(chooseGlobs "vfs:" "lfs:") |> Seq.toList

      let virtualExcludes =
        config.excludes |> Seq.choose(chooseGlobs "vfs:" "lfs:") |> Seq.toList

      {
        BaseDirectory = UMX.untag tempDir
        Includes = virtualIncludes
        Excludes = virtualExcludes
      }

    let copyAndIncrement (cwd: string) (tsk: ProgressTask) (file: string) =
      tsk.Increment 1
      let targetPath = file.Replace(cwd, outDir)

      try
        Path.GetDirectoryName targetPath |> Directory.CreateDirectory |> ignore
      with _ ->
        ()

      File.Copy(file, targetPath, true)


    AnsiConsole
      .Progress()
      .Start(fun ctx ->
        let lfsTask =
          ctx.AddTask(
            "Copy Local Files to Output",
            true,
            lfsGlob |> Seq.length |> float
          )

        let vfsTask =
          ctx.AddTask(
            "Copy virtual files to Output",
            true,
            vfsGlob |> Seq.length |> float
          )

        let copyLocal =
          copyAndIncrement
            (UMX.untag(FileSystem.CurrentWorkingDirectory()))
            lfsTask

        let copyVirtual = copyAndIncrement (UMX.untag tempDir) vfsTask

        vfsGlob |> Seq.toArray |> Array.Parallel.iter copyVirtual

        lfsGlob |> Seq.toArray |> Array.Parallel.iter copyLocal)

  static member EmitEnvFile(config: PerlaConfig, ?tmpPath: string<SystemPath>) =
    let tmpPath = defaultArg tmpPath config.build.outDir |> UMX.untag

    match Env.GetEnvContent() with
    | Some content ->
      // remove the leading slash
      let targetFile = (UMX.untag config.envPath)[1..]

      let path = Path.Combine(tmpPath, targetFile) |> Path.GetFullPath
      File.WriteAllText(path, content)
    | None -> ()
