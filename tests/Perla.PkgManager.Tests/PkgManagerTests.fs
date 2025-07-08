namespace Perla.PkgManager.Tests

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Perla.Logger
open Xunit
open IcedTasks
open Perla.PkgManager
open Perla.PkgManager.PkgManager
open Perla.PkgManager.RequestHandler

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
        Map.ofList [ ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js") ]

      scopes = Map.empty
      integrity = Map.empty
    }
  }

  let defaultUpdateResponse = {
    staticDeps = [| "react"; "vue" |]
    dynamicDeps = [||]
    map = {
      imports =

        Map.ofList [
          ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js")
          ("vue", "https://ga.jspm.io/npm:vue@3.5.17/dist/vue.esm-browser.js")
        ]

      scopes = Map.empty
      integrity = Map.empty
    }
  }

  let defaultUninstallResponse = {
    staticDeps = [| "react" |]
    dynamicDeps = [||]
    map = {
      imports =
        Map.ofList [ ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js") ]
      scopes = Map.empty
      integrity = Map.empty
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
      // Simulate adding/updating packages in the import map
      let inputMap =
        match options.TryGetValue("inputMap") with
        | true, (:? ImportMap as m) -> m
        | _ -> defaultUpdateResponse.map

      let updateSet =
        match options.TryGetValue("update") with
        | true, (:? Set<string> as s) -> s
        | _ -> Set.empty

      let updatedImports =
        updateSet
        |> Seq.fold
          (fun acc pkg ->
            acc |> Map.add pkg ($"https://ga.jspm.io/npm:{pkg}/index.js"))
          inputMap.imports

      let newMap = {
        inputMap with
            imports = updatedImports
      }

      let resp = {
        defaultUpdateResponse with
            map = newMap
      }

      Task.FromResult(defaultArg updateResponse resp)

    member _.Uninstall(options, ?cancellationToken) =
      // Simulate removing packages from the import map
      let inputMap =
        match options.TryGetValue("inputMap") with
        | true, (:? ImportMap as m) -> m
        | _ -> defaultUninstallResponse.map

      let uninstallSet =
        match options.TryGetValue("uninstall") with
        | true, (:? Set<string> as s) -> s
        | _ -> Set.empty

      let updatedImports =
        inputMap.imports |> Map.filter(fun k _ -> not(uninstallSet.Contains k))

      let newMap = {
        inputMap with
            imports = updatedImports
      }

      let resp = {
        defaultUninstallResponse with
            map = newMap
      }

      Task.FromResult(defaultArg uninstallResponse resp)

    member _.Download(packages, options, ?cancellationToken) =
      Task.FromResult(defaultArg downloadResponse defaultDownloadResponse)

module ImportMapTests =

  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder ->
        builder.AddPerlaLogger().SetMinimumLevel(LogLevel.Debug) |> ignore)

    loggerFactory.CreateLogger("ImportMapTests")

  let createImportMapService(fakeJspmService: JspmService option) =
    let logger = createLogger()

    let jspmService =
      defaultArg fakeJspmService (FakeJspmService() :> JspmService)

    let path = IO.Directory.CreateTempSubdirectory("importmaptests").FullName

    let pkgManagerConfig = {
      GlobalCachePath = path
      cwd = Environment.CurrentDirectory
    }

    let dependencies: PkgManagerServiceArgs = {
      reqHandler = jspmService
      logger = logger
      config = pkgManagerConfig
    }

    PkgManager.create dependencies

  [<Fact>]
  let ``install should create proper request with packages``() = taskUnit {
    // Arrange
    let packages: string list = [ "react"; "vue" ]
    let service = createImportMapService(None)

    // Act
    let! result = service.Install(packages)

    // Assert
    Assert.Equal<int>(1, result.staticDeps.Length)
    Assert.Equal<string>("react", result.staticDeps[0])
    Assert.True(result.map.imports.ContainsKey("react"))
  }

  [<Fact>]
  let ``update should accept ImportMap and packages``() = taskUnit {
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
    Assert.Equal<int>(2, result.staticDeps.Length)
    Assert.True(result.map.imports.ContainsKey("react"))
    Assert.True(result.map.imports.ContainsKey("vue"))
  }

  [<Fact>]
  let ``uninstall should accept ImportMap and packages``() = taskUnit {
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
    Assert.Equal<int>(1, result.staticDeps.Length)
    Assert.Equal<string>("react", result.staticDeps[0])
    Assert.True(result.map.imports.ContainsKey("react"))
  }

  [<Fact>]
  let ``goOffline should accept ImportMap and options``() = taskUnit {
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
    Assert.True(result.imports.ContainsKey("react"))
    // The URL should be converted to a local path starting with /node_modules
    Assert.True(result.imports["react"].StartsWith("/node_modules"))
  }

  [<Fact>]
  let ``ImportMap creation should work with empty maps``() =
    // Arrange & Act
    let importMap: ImportMap = {
      imports = Map.empty
      scopes = Map.empty
      integrity = Map.empty
    }

    // Assert
    Assert.Equal<int>(0, importMap.imports.Count)
    Assert.Equal<int>(0, importMap.scopes.Count)
    Assert.Equal<int>(0, importMap.integrity.Count)

  [<Fact>]
  let ``ImportMap creation should work with populated maps``() =
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
    Assert.Equal<int>(2, importMap.imports.Count)
    Assert.Equal<int>(1, importMap.scopes.Count)
    Assert.Equal<int>(1, importMap.integrity.Count)

    Assert.Equal<string>(
      "https://example.com/react.js",
      importMap.imports["react"]
    )

    Assert.Equal<string>(
      "https://example.com/lodash.js",
      importMap.scopes["scope1"]["lodash"]
    )

    Assert.Equal<string>("sha384-abc123", importMap.integrity["react"])

  [<Fact>]
  let ``FindDependency should return exact package match with version``() =
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js")
          ("react-dom", "https://ga.jspm.io/npm:react-dom@18.2.0/index.js")
          ("@babel/core",
           "https://ga.jspm.io/npm:@babel/core@7.20.0/lib/index.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }

    // Act & Assert - Exact matches
    let reactResult = importMap.FindDependency("react")
    Assert.True(reactResult.IsSome)
    let (name, version) = reactResult.Value
    Assert.Equal("react", name)
    Assert.Equal("18.2.0", version.Value)

    let reactDomResult = importMap.FindDependency("react-dom")
    Assert.True(reactDomResult.IsSome)
    let (name2, version2) = reactDomResult.Value
    Assert.Equal("react-dom", name2)
    Assert.Equal("18.2.0", version2.Value)

    let babelResult = importMap.FindDependency("@babel/core")
    Assert.True(babelResult.IsSome)
    let (name3, version3) = babelResult.Value
    Assert.Equal("@babel/core", name3)
    Assert.Equal("7.20.0", version3.Value)

  [<Fact>]
  let ``FindDependency should return None for partial matches``() =
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js")
          ("react-dom", "https://ga.jspm.io/npm:react-dom@18.2.0/index.js")
          ("@babel/core",
           "https://ga.jspm.io/npm:@babel/core@7.20.0/lib/index.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }

    // Act & Assert - Partial matches should return None
    Assert.True(importMap.FindDependency("reac").IsNone) // partial match should fail
    Assert.True(importMap.FindDependency("react-").IsNone) // partial match should fail
    Assert.True(importMap.FindDependency("@babel").IsNone) // partial scoped match should fail
    Assert.True(importMap.FindDependency("core").IsNone) // part of scoped package should fail
    Assert.True(importMap.FindDependency("nonexistent").IsNone) // non-existent should fail

  [<Fact>]
  let ``FindDependency should be case insensitive``() =
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("React", "https://ga.jspm.io/npm:react@18.2.0/index.js")
          ("LODASH", "https://ga.jspm.io/npm:lodash@4.17.21/lodash.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }

    // Act & Assert - Case insensitive matching
    let reactResult = importMap.FindDependency("react")
    Assert.True(reactResult.IsSome)
    let (name, version) = reactResult.Value
    Assert.Equal("React", name) // Should return the original key
    Assert.Equal("18.2.0", version.Value)

    let lodashResult = importMap.FindDependency("lodash")
    Assert.True(lodashResult.IsSome)
    let (name2, version2) = lodashResult.Value
    Assert.Equal("LODASH", name2)
    Assert.Equal("4.17.21", version2.Value)

  [<Fact>]
  let ``FindDependency should return None for packages without version``() =
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("react", "https://ga.jspm.io/npm:react/index.js") // no version
          ("lodash", "https://ga.jspm.io/npm:lodash@4.17.21/lodash.js") // with version
        ]
      scopes = Map.empty
      integrity = Map.empty
    }

    // Act & Assert
    let reactResult = importMap.FindDependency("react")
    Assert.True(reactResult.IsNone) // Should return None because no version found

    let lodashResult = importMap.FindDependency("lodash")
    Assert.True(lodashResult.IsSome) // Should find this one with version
    let (name, version) = lodashResult.Value
    Assert.Equal("lodash", name)
    Assert.Equal("4.17.21", version.Value)

  [<Fact>]
  let ``FindDependency should handle empty ImportMap``() =
    // Arrange
    let importMap: ImportMap = {
      imports = Map.empty
      scopes = Map.empty
      integrity = Map.empty
    }

    // Act & Assert
    Assert.True(importMap.FindDependency("react").IsNone)
    Assert.True(importMap.FindDependency("").IsNone)

  [<Fact>]
  let ``ExtractDependencies should handle deep imports and return correct format``
    ()
    =
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("solid-js", "https://ga.jspm.io/npm:solid-js@1.9.7/dist/dev.js")
          ("solid-js/web",
           "https://ga.jspm.io/npm:solid-js@1.9.7/web/dist/dev.js")
          ("solid-js/html",
           "https://ga.jspm.io/npm:solid-js@1.9.7/html/dist/html.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }
    // Act
    let deps = importMap.ExtractDependencies()
    // Assert
    Assert.Contains(("solid-js", Some "1.9.7"), deps)
    Assert.Contains(("solid-js/web", Some "1.9.7"), deps)
    Assert.Contains(("solid-js/html", Some "1.9.7"), deps)

  [<Fact>]
  let ``uninstall should handle deep imports in ImportMap``() = taskUnit {
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("solid-js", "https://ga.jspm.io/npm:solid-js@1.9.7/dist/dev.js")
          ("solid-js/web",
           "https://ga.jspm.io/npm:solid-js@1.9.7/web/dist/dev.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }

    let packages = [ "solid-js/web" ]
    let service = createImportMapService(None)
    // Act
    let! result = service.Uninstall(importMap, packages)
    // Assert
    Assert.True(result.map.imports.ContainsKey("solid-js"))
    Assert.False(result.map.imports.ContainsKey("solid-js/web"))
  }

  [<Fact>]
  let ``update should handle deep imports in ImportMap``() = taskUnit {
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("solid-js", "https://ga.jspm.io/npm:solid-js@1.9.7/dist/dev.js")
          ("solid-js/web",
           "https://ga.jspm.io/npm:solid-js@1.9.7/web/dist/dev.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }

    let packages = [ "solid-js/web" ]
    let service = createImportMapService(None)
    // Act
    let! result = service.Update(importMap, packages)
    // Assert
    Assert.True(result.map.imports.ContainsKey("solid-js"))
    Assert.True(result.map.imports.ContainsKey("solid-js/web"))
  }

  [<Fact>]
  let ``ExtractDependencies should not transform package names``() =
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("solid-js", "https://ga.jspm.io/npm:solid-js@1.9.7/dist/dev.js")
          ("solid-js/web",
           "https://ga.jspm.io/npm:solid-js@1.9.7/web/dist/dev.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }
    // Act
    let deps = importMap.ExtractDependencies()
    // Assert
    Assert.Contains(("solid-js", Some "1.9.7"), deps)
    Assert.Contains(("solid-js/web", Some "1.9.7"), deps)

  [<Fact>]
  let ``FindDependency should not transform package names and support deep imports``
    ()
    =
    // Arrange
    let importMap: ImportMap = {
      imports =
        Map.ofList [
          ("solid-js", "https://ga.jspm.io/npm:solid-js@1.9.7/dist/dev.js")
          ("solid-js/web",
           "https://ga.jspm.io/npm:solid-js@1.9.7/web/dist/dev.js")
        ]
      scopes = Map.empty
      integrity = Map.empty
    }
    // Act
    let result = importMap.FindDependency("solid-js/web")
    // Assert
    Assert.True(result.IsSome)
    let (name, version) = result.Value
    Assert.Equal("solid-js/web", name)
    Assert.Equal("1.9.7", version.Value)
