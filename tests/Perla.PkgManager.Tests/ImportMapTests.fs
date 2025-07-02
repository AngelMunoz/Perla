namespace Medusa.Tests

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Microsoft.VisualStudio.TestTools.UnitTesting
open IcedTasks
open Medusa
open Medusa.Types
open Medusa.RequestHandler

/// Fake implementation of JspmService for testing
type FakeJspmService
  (
    ?installResponse: GeneratorResponse,
    ?updateResponse: GeneratorResponse,
    ?uninstallResponse: GeneratorResponse,
    ?downloadResponse: DownloadResponse
  ) =

  let defaultInstallResponse = {
    staticDeps = [| "react" |]
    dynamicDeps = [||]
    map = {
      imports =
        Some(
          Map.ofList [
            ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js")
          ]
        )
      scopes = None
      integrity = None
    }
  }

  let defaultUpdateResponse = {
    staticDeps = [| "react"; "vue" |]
    dynamicDeps = [||]
    map = {
      imports =
        Some(
          Map.ofList [
            ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js")
            ("vue", "https://ga.jspm.io/npm:vue@3.5.17/dist/vue.esm-browser.js")
          ]
        )
      scopes = None
      integrity = None
    }
  }

  let defaultUninstallResponse = {
    staticDeps = [| "react" |]
    dynamicDeps = [||]
    map = {
      imports =
        Some(
          Map.ofList [
            ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js")
          ]
        )
      scopes = None
      integrity = None
    }
  }

  let defaultDownloadResponse =
    DownloadSuccess(
      Map.ofList [
        ("react@18.2.0",
         {
           pkgUrl = Uri("https://ga.jspm.io/npm:react@18.2.0/")
           files = [| "index.js"; "package.json" |]
         })
      ]
    )

  interface JspmService with
    member _.Install(options, ?cancellationToken) =
      Task.FromResult(defaultArg installResponse defaultInstallResponse)

    member _.Update(options, ?cancellationToken) =
      Task.FromResult(defaultArg updateResponse defaultUpdateResponse)

    member _.Uninstall(options, ?cancellationToken) =
      Task.FromResult(defaultArg uninstallResponse defaultUninstallResponse)

    member _.Download(packages, options, ?cancellationToken) =
      Task.FromResult(defaultArg downloadResponse defaultDownloadResponse)

[<TestClass>]
type ImportMapTests() =

  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder ->
        builder.AddConsole().SetMinimumLevel(LogLevel.Debug) |> ignore)

    loggerFactory.CreateLogger(nameof ImportMapTests)

  let createImportMapService(fakeJspmService: JspmService option) =
    let logger = createLogger()

    let jspmService =
      defaultArg fakeJspmService (FakeJspmService() :> JspmService)

    let dependencies = {
      ImportMap.ImportMapServiceArgs.reqHandler = jspmService
      ImportMap.ImportMapServiceArgs.logger = logger
    }

    ImportMapService.create dependencies

  [<TestMethod>]
  member _.``install should create proper request with packages``() = taskUnit {
    // Arrange
    let packages = [ "react"; "vue" ]
    let service = createImportMapService(None)

    // Act
    let! result = service.Install(packages)

    // Assert
    Assert.IsNotNull(result)
    Assert.AreEqual<int>(1, result.staticDeps.Length)
    Assert.AreEqual<string>("react", result.staticDeps[0])
    Assert.IsTrue(result.map.imports.IsSome)
    Assert.IsTrue(result.map.imports.Value.ContainsKey("react"))
  }

  [<TestMethod>]
  member _.``update should accept ImportMap and packages``() = taskUnit {
    // Arrange
    let importMap: ImportMap = {
      imports = Map.ofList [ ("react", "https://example.com/react.js") ]
      scopes = Map.empty
      integrity = Map.empty
    }

    let packages = [ "vue" ]
    let service = createImportMapService(None)

    // Act
    let! result = service.Update(importMap, packages)

    // Assert
    Assert.IsNotNull(result)
    Assert.AreEqual<int>(2, result.staticDeps.Length)
    Assert.IsTrue(result.map.imports.IsSome)
    Assert.IsTrue(result.map.imports.Value.ContainsKey("react"))
    Assert.IsTrue(result.map.imports.Value.ContainsKey("vue"))
  }

  [<TestMethod>]
  member _.``uninstall should accept ImportMap and packages``() = taskUnit {
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("react", "https://example.com/react.js")
          ("vue", "https://example.com/vue.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }

    let packages = [ "vue" ]
    let service = createImportMapService(None)

    // Act
    let! result = service.Uninstall(importMap, packages)

    // Assert
    Assert.IsNotNull(result)
    Assert.AreEqual<int>(1, result.staticDeps.Length)
    Assert.AreEqual<string>("react", result.staticDeps[0])
    Assert.IsTrue(result.map.imports.IsSome)
    Assert.IsTrue(result.map.imports.Value.ContainsKey("react"))
  }

  [<TestMethod>]
  member _.``goOffline should accept ImportMap and options``() = taskUnit {
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [ ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js") ]
      scopes = Map.empty
      integrity = Map.empty
    }

    let service = createImportMapService(None)

    // Act
    let! result = service.GoOffline(importMap)

    // Assert
    Assert.IsNotNull(result)
    Assert.IsTrue(result.imports.ContainsKey("react"))
    // The URL should be converted to a local path starting with /node_modules
    Assert.IsTrue(result.imports["react"].StartsWith("/node_modules"))
  }

  [<TestMethod>]
  member _.``ImportMap creation should work with empty maps``() =
    // Arrange & Act
    let importMap: ImportMap = {
      imports = Map.empty
      scopes = Map.empty
      integrity = Map.empty
    }

    // Assert
    Assert.AreEqual<int>(0, importMap.imports.Count)
    Assert.AreEqual<int>(0, importMap.scopes.Count)
    Assert.AreEqual<int>(0, importMap.integrity.Count)

  [<TestMethod>]
  member _.``ImportMap creation should work with populated maps``() =
    // Arrange & Act
    let imports =
      Map.ofList [
        ("react", "https://example.com/react.js")
        ("vue", "https://example.com/vue.js")
      ]

    let scopes =
      Map.ofList [
        ("scope1", Map.ofList [ ("lodash", "https://example.com/lodash.js") ])
      ]

    let integrity = Map.ofList [ ("react", "sha384-abc123") ]

    let importMap: ImportMap = {
      imports = imports
      scopes = scopes
      integrity = integrity
    }

    // Assert
    Assert.AreEqual<int>(2, importMap.imports.Count)
    Assert.AreEqual<int>(1, importMap.scopes.Count)
    Assert.AreEqual<int>(1, importMap.integrity.Count)

    Assert.AreEqual<string>(
      "https://example.com/react.js",
      importMap.imports["react"]
    )

    Assert.AreEqual<string>(
      "https://example.com/lodash.js",
      importMap.scopes["scope1"]["lodash"]
    )

    Assert.AreEqual<string>("sha384-abc123", importMap.integrity["react"])
