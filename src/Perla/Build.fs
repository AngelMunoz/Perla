namespace Perla.Build

open System.IO

open AngleSharp
open AngleSharp.Html.Dom

open Perla
open Perla.Types
open Perla.Units
open Perla.FileSystem

open FSharp.Data.Adaptive
open Fake.IO.Globbing

open FSharp.UMX
open Spectre.Console
open FsToolkit.ErrorHandling

[<RequireQualifiedAccess>]
module Build =
  open Microsoft.Extensions.Logging
  open System.Text

  let insertCssFiles
    (document: IHtmlDocument, cssEntryPoints: string<ServerUrl> seq)
    =
    for file in cssEntryPoints do
      let style = document.CreateElement("link")
      style.SetAttribute("rel", "stylesheet")
      style.SetAttribute("href", UMX.untag file)
      style |> document.Head.AppendChild |> ignore

  let insertImportMap
    (document: IHtmlDocument, importMap: PkgManager.ImportMap)
    =
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

  let EntryPoints(document: IHtmlDocument) =
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

  let Externals(config: PerlaConfig) = seq {

    if config.enableEnv && config.build.emitEnvFile then
      UMX.untag config.envPath
      Constants.EnvBareImport

    yield! config.esbuild.externals
  }

  let Index
    (
      document: IHtmlDocument,
      cssPaths: string<ServerUrl> seq,
      jsPaths: string<ServerUrl> seq,
      importMap: PkgManager.ImportMap
    ) =

    insertCssFiles(document, cssPaths)

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
    insertJsFiles(document, jsPaths)

    document.Minify()
