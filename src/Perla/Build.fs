namespace Perla.Build

open System

open AngleSharp
open AngleSharp.Html.Dom

open Perla
open Perla.Types
open Perla.Units

open FSharp.UMX
open FsToolkit.ErrorHandling

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

    let htmlBundles =
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

    cssBundles, htmlBundles, standaloneBundles

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
    |> Seq.iter(fun f -> f.Remove())

    document.QuerySelectorAll("[data-entry-point=standalone][type=module]")
    |> Seq.iter(fun f -> f.Remove())

    document.QuerySelectorAll("[data-entry-point][rel=stylesheet]")
    |> Seq.iter(fun f -> f.Remove())

    // insert the resolved entry points which should match paths in mounted directories
    insertJsFiles(document, jsExtras)

    document.Minify()
