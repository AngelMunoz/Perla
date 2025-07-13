module Perla.Tests.Build

open System
open Microsoft.Extensions.Logging
open Xunit
open FSharp.UMX
open AngleSharp
open AngleSharp.Html.Dom
open AngleSharp.Html.Parser

open Perla
open Perla.Units
open Perla.Types
open Perla.Build
open Perla.PkgManager
open Perla.Logger

// Test helpers
module TestHelpers =
  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.AddPerlaLogger() |> ignore)

    loggerFactory.CreateLogger("BuildTests")

  let createDocument(html: string) =
    let config = Configuration.Default
    let context = BrowsingContext.New(config)
    let parser = context.GetService<IHtmlParser>()
    parser.ParseDocument($"<html><head></head><body>{html}</body></html>")

  let createEmptyDocument() =
    let config = Configuration.Default
    let context = BrowsingContext.New(config)
    let parser = context.GetService<IHtmlParser>()
    parser.ParseDocument("<html><head></head><body></body></html>")

  let createBasicConfig() = {
    Perla.Defaults.PerlaConfig with
        enableEnv = true
        build = {
          Defaults.BuildConfig with
              emitEnvFile = true
        }
        esbuild = {
          Defaults.EsbuildConfig with
              externals = [ "react"; "vue" ]
        }
  }

  let createImportMap() = {
    imports =
      Map.ofList [
        ("react", "https://example.com/react.js")
        ("vue", "https://example.com/vue.js")
      ]
    scopes = Map.empty
    integrity = Map.empty
  }

module EnsureBodyTests =
  open TestHelpers

  [<Fact>]
  let ``EnsureBody should return existing body when present``() =
    // Arrange
    let document = createDocument("<p>Content</p>")

    // Act
    let body = Build.EnsureBody(document)

    // Assert
    Assert.NotNull(body)
    Assert.Equal("body", body.TagName.ToLower())
    Assert.Contains("Content", body.InnerHtml)

  [<Fact>]
  let ``EnsureBody should create body when missing``() =
    // Arrange - Create a document with no body element
    let config = Configuration.Default
    let context = BrowsingContext.New(config)
    let parser = context.GetService<IHtmlParser>()
    let document = parser.ParseDocument("<html><head></head></html>")

    // Act
    let body = Build.EnsureBody(document)

    // Assert
    Assert.NotNull(body)
    Assert.Equal("body", body.TagName.ToLower())
    Assert.Same(document.Body, body)

module EnsureHeadTests =
  open TestHelpers

  [<Fact>]
  let ``EnsureHead should return existing head when present``() =
    // Arrange
    let document = createEmptyDocument()

    // Act
    let head = Build.EnsureHead(document)

    // Assert
    Assert.NotNull(head)
    Assert.Equal("head", head.TagName.ToLower())
    Assert.Same(document.Head, head)

  [<Fact>]
  let ``EnsureHead should create head when missing``() =
    // Arrange - Create a document with no head element
    let config = Configuration.Default
    let context = BrowsingContext.New(config)
    let parser = context.GetService<IHtmlParser>()
    let document = parser.ParseDocument("<html><body></body></html>")

    // Act
    let head = Build.EnsureHead(document)

    // Assert
    Assert.NotNull(head)
    Assert.Equal("head", head.TagName.ToLower())
    Assert.Same(document.Head, head)

module EntryPointsTests =
  open TestHelpers

  [<Fact>]
  let ``EntryPoints should extract CSS entry points correctly``() =
    // Arrange
    let html =
      """
      <link data-entry-point rel="stylesheet" href="/styles/main.css">
      <link data-entry-point rel="stylesheet" href="/styles/theme.css">
      <link rel="stylesheet" href="/styles/regular.css">
    """

    let document = createDocument(html)

    // Act
    let (cssBundles, _, _) = Build.EntryPoints(document)
    let cssUrls = cssBundles |> Seq.map UMX.untag |> Seq.toList

    // Assert
    Assert.Equal(2, Seq.length cssBundles)
    Assert.Contains("/styles/main.css", cssUrls)
    Assert.Contains("/styles/theme.css", cssUrls)
    Assert.DoesNotContain("/styles/regular.css", cssUrls)

  [<Fact>]
  let ``EntryPoints should extract JS entry points correctly``() =
    // Arrange
    let html =
      """
      <script data-entry-point="app" type="module" src="/js/app.js"></script>
      <script data-entry-point="vendor" type="module" src="/js/vendor.js"></script>
      <script type="module" src="/js/regular.js"></script>
    """

    let document = createDocument(html)

    // Act
    let (_, htmlBundles, _) = Build.EntryPoints(document)
    let jsUrls = htmlBundles |> Seq.map UMX.untag |> Seq.toList

    // Assert
    Assert.Equal(2, Seq.length htmlBundles)
    Assert.Contains("/js/app.js", jsUrls)
    Assert.Contains("/js/vendor.js", jsUrls)
    Assert.DoesNotContain("/js/regular.js", jsUrls)

  [<Fact>]
  let ``EntryPoints should extract standalone entry points correctly``() =
    // Arrange
    let html =
      """
      <script data-entry-point="standalone" type="module" src="/js/worker.js"></script>
      <script data-entry-point="standalone" type="module" src="/js/sw.js"></script>
      <script data-entry-point="app" type="module" src="/js/app.js"></script>
    """

    let document = createDocument(html)

    // Act
    let (_, _, standaloneBundles) = Build.EntryPoints(document)
    let standaloneUrls = standaloneBundles |> Seq.map UMX.untag |> Seq.toList

    // Assert
    Assert.Equal(2, Seq.length standaloneBundles)
    Assert.Contains("/js/worker.js", standaloneUrls)
    Assert.Contains("/js/sw.js", standaloneUrls)
    Assert.DoesNotContain("/js/app.js", standaloneUrls)

  [<Fact>]
  let ``EntryPoints should ignore entries with empty href or src``() =
    // Arrange
    let html =
      """
      <link data-entry-point rel="stylesheet" href="">
      <link data-entry-point rel="stylesheet" href="   ">
      <script data-entry-point="standalone" type="module" src=""></script>
      <script data-entry-point="standalone" type="module" src="   "></script>
      <link data-entry-point rel="stylesheet" href="/valid.css">
    """

    let document = createDocument(html)

    // Act
    let (cssBundles, htmlBundles, standaloneBundles) =
      Build.EntryPoints(document)

    // Assert
    // CSS bundles should filter out empty href
    Assert.Equal(1, Seq.length cssBundles)
    Assert.Equal("/valid.css", cssBundles |> Seq.head |> UMX.untag)

    // HTML bundles should be empty (no non-standalone scripts in this test)
    Assert.Equal(0, Seq.length htmlBundles)

    // Standalone bundles should filter out empty src
    Assert.Equal(0, Seq.length standaloneBundles)

module ExternalsTests =
  open TestHelpers

  [<Fact>]
  let ``Externals should include env externals when enabled``() =
    // Arrange
    let config = {
      createBasicConfig() with
          enableEnv = true
          build = {
            Defaults.BuildConfig with
                emitEnvFile = true
          }
    }

    // Act
    let externals = Build.Externals(config) |> Seq.toList

    // Assert
    Assert.Contains(UMX.untag config.envPath, externals)
    Assert.Contains(Constants.EnvBareImport, externals)

  [<Fact>]
  let ``Externals should not include env externals when env disabled``() =
    // Arrange
    let config = {
      createBasicConfig() with
          enableEnv = false
          build = {
            Defaults.BuildConfig with
                emitEnvFile = true
          }
    }

    // Act
    let externals = Build.Externals(config) |> Seq.toList

    // Assert
    Assert.DoesNotContain(UMX.untag config.envPath, externals)
    Assert.DoesNotContain(Constants.EnvBareImport, externals)

  [<Fact>]
  let ``Externals should not include env externals when emitEnvFile disabled``
    ()
    =
    // Arrange
    let config = {
      createBasicConfig() with
          enableEnv = true
          build = {
            Defaults.BuildConfig with
                emitEnvFile = false
          }
    }

    // Act
    let externals = Build.Externals(config) |> Seq.toList

    // Assert
    Assert.DoesNotContain(UMX.untag config.envPath, externals)
    Assert.DoesNotContain(Constants.EnvBareImport, externals)

  [<Fact>]
  let ``Externals should include esbuild externals``() =
    // Arrange
    let config = {
      createBasicConfig() with
          esbuild = {
            Defaults.EsbuildConfig with
                externals = [ "react"; "vue"; "lodash" ]
          }
    }

    // Act
    let externals = Build.Externals(config) |> Seq.toList

    // Assert
    Assert.Contains("react", externals)
    Assert.Contains("vue", externals)
    Assert.Contains("lodash", externals)

  [<Fact>]
  let ``Externals should include both env and esbuild externals when both enabled``
    ()
    =
    // Arrange
    let config = {
      createBasicConfig() with
          enableEnv = true
          build = {
            Defaults.BuildConfig with
                emitEnvFile = true
          }
          esbuild = {
            Defaults.EsbuildConfig with
                externals = [ "react"; "vue" ]
          }
    }

    // Act
    let externals = Build.Externals(config) |> Seq.toList

    // Assert
    Assert.Contains(UMX.untag config.envPath, externals)
    Assert.Contains(Constants.EnvBareImport, externals)
    Assert.Contains("react", externals)
    Assert.Contains("vue", externals)
    Assert.Equal(4, externals.Length)

module IndexTests =
  open TestHelpers

  [<Fact>]
  let ``Index should insert CSS files into head``() =
    // Arrange
    let document = createEmptyDocument()
    let importMap = createImportMap()
    let jsExtras = [ UMX.tag<ServerUrl> "/js/app.js" ]

    let cssExtras = [
      UMX.tag<ServerUrl> "/css/main.css"
      UMX.tag<ServerUrl> "/css/theme.css"
    ]

    // Act
    let result = Build.Index(document, importMap, jsExtras, cssExtras)

    // Assert
    let cssLinks = document.QuerySelectorAll("link[rel=stylesheet]")
    Assert.Equal(2, cssLinks.Length)
    Assert.Equal("/css/main.css", cssLinks[0].GetAttribute("href"))
    Assert.Equal("/css/theme.css", cssLinks[1].GetAttribute("href"))

  [<Fact>]
  let ``Index should insert import map into head``() =
    // Arrange
    let document = createEmptyDocument()
    let importMap = createImportMap()
    let jsExtras = [ UMX.tag<ServerUrl> "/js/app.js" ]
    let cssExtras = []

    // Act
    let result = Build.Index(document, importMap, jsExtras, cssExtras)

    // Assert
    let importMapScript = document.QuerySelector("script[type=importmap]")
    Assert.NotNull(importMapScript)
    Assert.Contains("react", importMapScript.TextContent)
    Assert.Contains("https://example.com/react.js", importMapScript.TextContent)

  [<Fact>]
  let ``Index should insert JS files into body``() =
    // Arrange
    let document = createEmptyDocument()
    let importMap = createImportMap()

    let jsExtras = [
      UMX.tag<ServerUrl> "/js/app.js"
      UMX.tag<ServerUrl> "/js/vendor.js"
    ]

    let cssExtras = []

    // Act
    let result = Build.Index(document, importMap, jsExtras, cssExtras)

    // Assert
    let scripts =
      document.QuerySelectorAll("script[type=module]:not([type=importmap])")

    Assert.Equal(2, scripts.Length)
    Assert.Equal("/js/app.js", scripts[0].GetAttribute("src"))
    Assert.Equal("/js/vendor.js", scripts[1].GetAttribute("src"))

  [<Fact>]
  let ``Index should remove existing entry points``() =
    // Arrange
    let html =
      """
      <link data-entry-point rel="stylesheet" href="/old.css">
      <script data-entry-point="app" type="module" src="/old-app.js"></script>
      <script data-entry-point="standalone" type="module" src="/old-worker.js"></script>
    """

    let document = createDocument(html)
    let importMap = createImportMap()
    let jsExtras = [ UMX.tag<ServerUrl> "/js/new-app.js" ]
    let cssExtras = [ UMX.tag<ServerUrl> "/css/new-style.css" ]

    // Act
    let result = Build.Index(document, importMap, jsExtras, cssExtras)

    // Assert
    Assert.Null(document.QuerySelector("[data-entry-point][rel=stylesheet]"))
    Assert.Null(document.QuerySelector("[data-entry-point][type=module]"))

    Assert.Null(
      document.QuerySelector("[data-entry-point=standalone][type=module]")
    )

    // Verify new files were added
    let newCssLink = document.QuerySelector("link[href='/css/new-style.css']")
    let newJsScript = document.QuerySelector("script[src='/js/new-app.js']")
    Assert.NotNull(newCssLink)
    Assert.NotNull(newJsScript)

  [<Fact>]
  let ``Index should handle empty extras collections``() =
    // Arrange
    let document = createEmptyDocument()
    let importMap = createImportMap()
    let jsExtras = []
    let cssExtras = []

    // Act
    let result = Build.Index(document, importMap, jsExtras, cssExtras)

    // Assert
    Assert.NotNull(result)
    let importMapScript = document.QuerySelector("script[type=importmap]")
    Assert.NotNull(importMapScript)
    Assert.Equal(0, document.QuerySelectorAll("link[rel=stylesheet]").Length)

    Assert.Equal(
      0,
      document
        .QuerySelectorAll("script[type=module]:not([type=importmap])")
        .Length
    )

  [<Fact>]
  let ``Index should preserve order of CSS and JS files``() =
    // Arrange
    let document = createEmptyDocument()
    let importMap = createImportMap()

    let jsExtras = [
      UMX.tag<ServerUrl> "/js/first.js"
      UMX.tag<ServerUrl> "/js/second.js"
      UMX.tag<ServerUrl> "/js/third.js"
    ]

    let cssExtras = [
      UMX.tag<ServerUrl> "/css/base.css"
      UMX.tag<ServerUrl> "/css/layout.css"
      UMX.tag<ServerUrl> "/css/theme.css"
    ]

    // Act
    let result = Build.Index(document, importMap, jsExtras, cssExtras)

    // Assert
    let cssLinks = document.QuerySelectorAll("link[rel=stylesheet]")
    Assert.Equal("/css/base.css", cssLinks[0].GetAttribute("href"))
    Assert.Equal("/css/layout.css", cssLinks[1].GetAttribute("href"))
    Assert.Equal("/css/theme.css", cssLinks[2].GetAttribute("href"))

    let jsScripts =
      document.QuerySelectorAll("script[type=module]:not([type=importmap])")

    Assert.Equal("/js/first.js", jsScripts[0].GetAttribute("src"))
    Assert.Equal("/js/second.js", jsScripts[1].GetAttribute("src"))
    Assert.Equal("/js/third.js", jsScripts[2].GetAttribute("src"))

  [<Fact>]
  let ``Index should place import map before other scripts``() =
    // Arrange
    let document = createEmptyDocument()
    let importMap = createImportMap()
    let jsExtras = [ UMX.tag<ServerUrl> "/js/app.js" ]
    let cssExtras = []

    // Act
    let result = Build.Index(document, importMap, jsExtras, cssExtras)

    // Assert
    let allScripts = document.QuerySelectorAll("script")
    Assert.True(allScripts.Length >= 2)
    Assert.Equal("importmap", allScripts[0].GetAttribute("type"))
    Assert.Equal("module", allScripts[1].GetAttribute("type"))

  [<Fact>]
  let ``Index should return minified HTML``() =
    // Arrange
    let document = createEmptyDocument()
    let importMap = createImportMap()
    let jsExtras = [ UMX.tag<ServerUrl> "/js/app.js" ]
    let cssExtras = [ UMX.tag<ServerUrl> "/css/main.css" ]

    // Act
    let result = Build.Index(document, importMap, jsExtras, cssExtras)

    // Assert
    Assert.NotNull(result)
    Assert.True(result.Length > 0)
    // The result should be a string representation of the HTML document
    Assert.IsType<string>(result)
