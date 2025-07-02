namespace Medusa.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Medusa
open Medusa.Types



[<TestClass>]
type TypesTests() =

  [<TestMethod>]
  member this.``GeneratorOption toDict should convert BaseUrl correctly``() =
    // Arrange
    let baseUrl = Uri("https://example.com/")
    let options = [ BaseUrl baseUrl ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("baseUrl"))

    Assert.AreEqual<string>(
      "https://example.com/",
      result["baseUrl"] :?> string
    )

  [<TestMethod>]
  member this.``GeneratorOption toDict should convert DefaultProvider correctly``
    ()
    =
    // Arrange
    let options = [ DefaultProvider Provider.JspmIo ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("defaultProvider"))
    Assert.AreEqual<string>("jspm.io", result["defaultProvider"] :?> string)

  [<TestMethod>]
  member this.``GeneratorOption toDict should convert custom Provider correctly``
    ()
    =
    // Arrange
    let options = [ DefaultProvider(Provider.Custom "custom-provider") ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("defaultProvider"))

    Assert.AreEqual<string>(
      "custom-provider",
      result["defaultProvider"] :?> string
    )

  [<TestMethod>]
  member this.``GeneratorOption toDict should convert Cache option correctly``
    ()
    =
    // Arrange
    let options = [ Cache(CacheOption.Enabled true) ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("cache"))
    Assert.AreEqual<bool>(true, result["cache"] :?> bool)

  [<TestMethod>]
  member this.``GeneratorOption toDict should convert offline Cache option correctly``
    ()
    =
    // Arrange
    let options = [ Cache CacheOption.Offline ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("cache"))
    Assert.AreEqual<string>("offline", result["cache"] :?> string)

  [<TestMethod>]
  member this.``GeneratorOption toDict should convert Env option correctly``() =
    // Arrange
    let envSet =
      Set.ofList [ ExportCondition.Development; ExportCondition.Browser ]

    let options = [ Env envSet ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("env"))
    let envResult = result["env"] :?> Set<string>
    Assert.IsTrue(envResult.Contains("development"))
    Assert.IsTrue(envResult.Contains("browser"))
    Assert.AreEqual<int>(2, envResult.Count)

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle custom ExportCondition``
    ()
    =
    // Arrange
    let envSet = Set.ofList [ ExportCondition.Custom "custom-env" ]
    let options = [ Env envSet ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("env"))
    let envResult = result["env"] :?> Set<string>
    Assert.IsTrue(envResult.Contains("custom-env"))

  [<TestMethod>]
  member this.``GeneratorOption toDict should convert multiple options correctly``
    ()
    =
    // Arrange
    let options = [
      BaseUrl(Uri("https://example.com/"))
      FlattenScopes true
      CombineSubPaths false
    ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.AreEqual<int>(3, result.Count)
    Assert.IsTrue(result.ContainsKey("baseUrl"))
    Assert.IsTrue(result.ContainsKey("flattenScopes"))
    Assert.IsTrue(result.ContainsKey("combineSubPaths"))

    Assert.AreEqual<string>(
      "https://example.com/",
      result["baseUrl"] :?> string
    )

    Assert.AreEqual<bool>(true, result["flattenScopes"] :?> bool)
    Assert.AreEqual<bool>(false, result["combineSubPaths"] :?> bool)

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle Providers map``() =
    // Arrange
    let providers = Map.ofList [ ("react", "jsdelivr"); ("lodash", "unpkg") ]
    let options = [ Providers providers ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("providers"))
    let providersResult = result["providers"] :?> Map<string, string>
    Assert.AreEqual<string>("jsdelivr", providersResult["react"])
    Assert.AreEqual<string>("unpkg", providersResult["lodash"])

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle Ignore set``() =
    // Arrange
    let ignoreSet = Set.ofList [ "node_modules"; ".git" ]
    let options = [ Ignore ignoreSet ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("ignore"))
    let ignoreResult = result["ignore"] :?> Set<string>
    Assert.IsTrue(ignoreResult.Contains("node_modules"))
    Assert.IsTrue(ignoreResult.Contains(".git"))
    Assert.AreEqual<int>(2, ignoreResult.Count)

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle MapUrl correctly``() =
    // Arrange
    let mapUrl = Uri("https://example.com/importmap.json")
    let options = [ MapUrl mapUrl ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("mapUrl"))

    Assert.AreEqual<string>(
      "https://example.com/importmap.json",
      result["mapUrl"] :?> string
    )

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle RootUrl correctly``() =
    // Arrange
    let rootUrl = Uri("https://example.com/root/")
    let options = [ RootUrl rootUrl ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("rootUrl"))

    Assert.AreEqual<string>(
      "https://example.com/root/",
      result["rootUrl"] :?> string
    )

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle InputMap correctly``() =
    // Arrange
    let importMap: ImportMap = {
      imports = Map.ofList [ ("react", "https://example.com/react.js") ]
      scopes = Map.empty
      integrity = Map.empty
    }

    let options = [ InputMap importMap ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("inputMap"))
    let resultMap = result["inputMap"] :?> ImportMap
    Assert.AreEqual<int>(1, resultMap.imports.Count)

    Assert.AreEqual<string>(
      "https://example.com/react.js",
      resultMap.imports["react"]
    )

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle all Provider types correctly``
    ()
    =
    // Arrange & Act & Assert
    let testCases = [
      (Provider.JspmIo, "jspm.io")
      (Provider.JspmIoSystem, "jspm.io#system")
      (Provider.NodeModules, "nodemodles")
      (Provider.Skypack, "skypack")
      (Provider.JsDelivr, "jsdelivr")
      (Provider.Unpkg, "unpkg")
      (Provider.EsmSh, "esm.sh")
      (Provider.Custom "my-provider", "my-provider")
    ]

    for provider, expected in testCases do
      let options = [ DefaultProvider provider ]
      let result = GeneratorOption.toDict options
      Assert.IsTrue(result.ContainsKey("defaultProvider"))
      Assert.AreEqual<string>(expected, result["defaultProvider"] :?> string)

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle ProviderConfig correctly``
    ()
    =
    // Arrange
    let providerConfig =
      Map.ofList [
        ("jsdelivr", Map.ofList [ ("timeout", "5000"); ("retries", "3") ])
        ("unpkg", Map.ofList [ ("cdn", "https://unpkg.com") ])
      ]

    let options = [ ProviderConfig providerConfig ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("providerConfig"))

    let resultConfig =
      result["providerConfig"] :?> Map<string, Map<string, string>>

    Assert.AreEqual<int>(2, resultConfig.Count)
    Assert.AreEqual<string>("5000", resultConfig["jsdelivr"]["timeout"])
    Assert.AreEqual<string>("https://unpkg.com", resultConfig["unpkg"]["cdn"])

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle Resolutions correctly``() =
    // Arrange
    let resolutions = Map.ofList [ ("react", "18.2.0"); ("vue", "3.5.17") ]
    let options = [ Resolutions resolutions ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("resolutions"))
    let resultResolutions = result["resolutions"] :?> Map<string, string>
    Assert.AreEqual<int>(2, resultResolutions.Count)
    Assert.AreEqual<string>("18.2.0", resultResolutions["react"])
    Assert.AreEqual<string>("3.5.17", resultResolutions["vue"])

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle all ExportCondition types correctly``
    ()
    =
    // Arrange
    let envSet =
      Set.ofList [
        ExportCondition.Development
        ExportCondition.Browser
        ExportCondition.Module
        ExportCondition.Custom "test"
      ]

    let options = [ Env envSet ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("env"))
    let envResult = result["env"] :?> Set<string>
    Assert.AreEqual<int>(4, envResult.Count)
    Assert.IsTrue(envResult.Contains("development"))
    Assert.IsTrue(envResult.Contains("browser"))
    Assert.IsTrue(envResult.Contains("module"))
    Assert.IsTrue(envResult.Contains("test"))

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle boolean flags correctly``
    ()
    =
    // Arrange
    let options = [ FlattenScopes true; CombineSubPaths false ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("flattenScopes"))
    Assert.IsTrue(result.ContainsKey("combineSubPaths"))
    Assert.AreEqual<bool>(true, result["flattenScopes"] :?> bool)
    Assert.AreEqual<bool>(false, result["combineSubPaths"] :?> bool)

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle empty collections correctly``
    ()
    =
    // Arrange
    let options = [ Providers Map.empty; Ignore Set.empty; Env Set.empty ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    Assert.IsTrue(result.ContainsKey("providers"))
    Assert.IsTrue(result.ContainsKey("ignore"))
    Assert.IsTrue(result.ContainsKey("env"))

    let providers = result["providers"] :?> Map<string, string>
    let ignore = result["ignore"] :?> Set<string>
    let env = result["env"] :?> Set<string>

    Assert.IsTrue(providers.IsEmpty)
    Assert.IsTrue(ignore.IsEmpty)
    Assert.IsTrue(env.IsEmpty)

  [<TestMethod>]
  member this.``GeneratorOption toDict should handle duplicate options by keeping first value``
    ()
    =
    // Arrange
    let options = [
      BaseUrl(Uri("https://first.com/"))
      BaseUrl(Uri("https://second.com/"))
      FlattenScopes true
      FlattenScopes false
    ]

    // Act
    let result = GeneratorOption.toDict options

    // Assert
    // TryAdd only adds if key doesn't exist, so first option should win
    Assert.AreEqual<string>("https://first.com/", result["baseUrl"] :?> string)
    Assert.AreEqual<bool>(true, result["flattenScopes"] :?> bool)

[<TestClass>]
type PartialImportMapTests() =

  [<TestMethod>]
  member this.``toImportMap should convert empty partial import map``() =
    // Arrange
    let partial: PartialImportMap = {
      imports = None
      scopes = None
      integrity = None
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    Assert.IsTrue(result.imports.IsEmpty)
    Assert.IsTrue(result.scopes.IsEmpty)
    Assert.IsTrue(result.integrity.IsEmpty)

  [<TestMethod>]
  member this.``toImportMap should convert partial with only imports``() =
    // Arrange
    let imports =
      Map.ofList [
        ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js")
        ("vue", "https://ga.jspm.io/npm:vue@3.5.17/dist/vue.esm-browser.js")
      ]

    let partial: PartialImportMap = {
      imports = Some imports
      scopes = None
      integrity = None
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    Assert.AreEqual<int>(2, result.imports.Count)

    Assert.AreEqual<string>(
      "https://ga.jspm.io/npm:react@18.2.0/index.js",
      result.imports["react"]
    )

    Assert.AreEqual<string>(
      "https://ga.jspm.io/npm:vue@3.5.17/dist/vue.esm-browser.js",
      result.imports["vue"]
    )

    Assert.IsTrue(result.scopes.IsEmpty)
    Assert.IsTrue(result.integrity.IsEmpty)

  [<TestMethod>]
  member this.``toImportMap should convert partial with only scopes``() =
    // Arrange
    let scopes =
      Map.ofList [
        ("https://ga.jspm.io/",
         Map.ofList [
           ("react-dom", "https://ga.jspm.io/npm:react-dom@18.2.0/index.js")
           ("lodash", "https://ga.jspm.io/npm:lodash@4.17.21/lodash.js")
         ])
      ]

    let partial: PartialImportMap = {
      imports = None
      scopes = Some scopes
      integrity = None
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    Assert.IsTrue(result.imports.IsEmpty)
    Assert.AreEqual<int>(1, result.scopes.Count)
    Assert.AreEqual<int>(2, result.scopes["https://ga.jspm.io/"].Count)

    Assert.AreEqual<string>(
      "https://ga.jspm.io/npm:react-dom@18.2.0/index.js",
      result.scopes["https://ga.jspm.io/"]["react-dom"]
    )

    Assert.AreEqual<string>(
      "https://ga.jspm.io/npm:lodash@4.17.21/lodash.js",
      result.scopes["https://ga.jspm.io/"]["lodash"]
    )

    Assert.IsTrue(result.integrity.IsEmpty)

  [<TestMethod>]
  member this.``toImportMap should convert partial with only integrity``() =
    // Arrange
    let integrity =
      Map.ofList [ ("react", "sha384-abc123"); ("vue", "sha384-def456") ]

    let partial: PartialImportMap = {
      imports = None
      scopes = None
      integrity = Some integrity
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    Assert.IsTrue(result.imports.IsEmpty)
    Assert.IsTrue(result.scopes.IsEmpty)
    Assert.AreEqual<int>(2, result.integrity.Count)
    Assert.AreEqual<string>("sha384-abc123", result.integrity["react"])
    Assert.AreEqual<string>("sha384-def456", result.integrity["vue"])

  [<TestMethod>]
  member this.``toImportMap should convert fully populated partial import map``
    ()
    =
    // Arrange
    let imports =
      Map.ofList [ ("react", "https://ga.jspm.io/npm:react@18.2.0/index.js") ]

    let scopes =
      Map.ofList [
        ("https://ga.jspm.io/",
         Map.ofList [
           ("react-dom", "https://ga.jspm.io/npm:react-dom@18.2.0/index.js")
         ])
      ]

    let integrity = Map.ofList [ ("react", "sha384-abc123") ]

    let partial: PartialImportMap = {
      imports = Some imports
      scopes = Some scopes
      integrity = Some integrity
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    Assert.AreEqual<int>(1, result.imports.Count)

    Assert.AreEqual<string>(
      "https://ga.jspm.io/npm:react@18.2.0/index.js",
      result.imports["react"]
    )

    Assert.AreEqual<int>(1, result.scopes.Count)
    Assert.AreEqual<int>(1, result.scopes["https://ga.jspm.io/"].Count)

    Assert.AreEqual<string>(
      "https://ga.jspm.io/npm:react-dom@18.2.0/index.js",
      result.scopes["https://ga.jspm.io/"]["react-dom"]
    )

    Assert.AreEqual<int>(1, result.integrity.Count)
    Assert.AreEqual<string>("sha384-abc123", result.integrity["react"])

  [<TestMethod>]
  member this.``toImportMap should handle empty maps when provided``() =
    // Arrange
    let partial: PartialImportMap = {
      imports = Some Map.empty
      scopes = Some Map.empty
      integrity = Some Map.empty
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    Assert.IsTrue(result.imports.IsEmpty)
    Assert.IsTrue(result.scopes.IsEmpty)
    Assert.IsTrue(result.integrity.IsEmpty)

  [<TestMethod>]
  member this.``toImportMap should handle mixed Some and None properties``() =
    // Arrange
    let partial: PartialImportMap = {
      imports = Some(Map.ofList [ ("react", "https://example.com/react.js") ])
      scopes = None
      integrity = Some(Map.ofList [ ("react", "sha384-hash") ])
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    Assert.AreEqual<int>(1, result.imports.Count)

    Assert.AreEqual<string>(
      "https://example.com/react.js",
      result.imports["react"]
    )

    Assert.IsTrue(result.scopes.IsEmpty)
    Assert.AreEqual<int>(1, result.integrity.Count)
    Assert.AreEqual<string>("sha384-hash", result.integrity["react"])

  [<TestMethod>]
  member this.``toImportMap should handle complex nested scopes``() =
    // Arrange
    let complexScopes =
      Map.ofList [
        ("scope1", Map.ofList [ ("lib1", "url1"); ("lib2", "url2") ])
        ("scope2", Map.ofList [ ("lib3", "url3") ])
        ("scope3", Map.empty)
      ]

    let partial: PartialImportMap = {
      imports = None
      scopes = Some complexScopes
      integrity = None
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    Assert.IsTrue(result.imports.IsEmpty)
    Assert.AreEqual<int>(3, result.scopes.Count)
    Assert.AreEqual<int>(2, result.scopes["scope1"].Count)
    Assert.AreEqual<int>(1, result.scopes["scope2"].Count)
    Assert.AreEqual<int>(0, result.scopes["scope3"].Count)
    Assert.AreEqual<string>("url1", result.scopes["scope1"]["lib1"])
    Assert.AreEqual<string>("url2", result.scopes["scope1"]["lib2"])
    Assert.AreEqual<string>("url3", result.scopes["scope2"]["lib3"])
    Assert.IsTrue(result.integrity.IsEmpty)

  [<TestMethod>]
  member this.``toImportMap should preserve all data when converting fully populated partial``
    ()
    =
    // Arrange
    let imports =
      Map.ofList [
        ("react", "https://cdn.react.com/react.js")
        ("vue", "https://cdn.vue.com/vue.js")
        ("lodash", "https://cdn.lodash.com/lodash.js")
      ]

    let scopes =
      Map.ofList [
        ("https://cdn.react.com/",
         Map.ofList [ ("react-dom", "https://cdn.react.com/react-dom.js") ])
        ("https://cdn.vue.com/",
         Map.ofList [ ("vue-router", "https://cdn.vue.com/vue-router.js") ])
      ]

    let integrity =
      Map.ofList [
        ("react", "sha384-react-hash")
        ("vue", "sha384-vue-hash")
        ("lodash", "sha384-lodash-hash")
      ]

    let partial: PartialImportMap = {
      imports = Some imports
      scopes = Some scopes
      integrity = Some integrity
    }

    // Act
    let result = PartialImportMap.toImportMap partial

    // Assert
    // Verify imports
    Assert.AreEqual<int>(3, result.imports.Count)

    Assert.AreEqual<string>(
      "https://cdn.react.com/react.js",
      result.imports["react"]
    )

    Assert.AreEqual<string>("https://cdn.vue.com/vue.js", result.imports["vue"])

    Assert.AreEqual<string>(
      "https://cdn.lodash.com/lodash.js",
      result.imports["lodash"]
    )

    // Verify scopes
    Assert.AreEqual<int>(2, result.scopes.Count)

    Assert.AreEqual<string>(
      "https://cdn.react.com/react-dom.js",
      result.scopes["https://cdn.react.com/"]["react-dom"]
    )

    Assert.AreEqual<string>(
      "https://cdn.vue.com/vue-router.js",
      result.scopes["https://cdn.vue.com/"]["vue-router"]
    )

    // Verify integrity
    Assert.AreEqual<int>(3, result.integrity.Count)
    Assert.AreEqual<string>("sha384-react-hash", result.integrity["react"])
    Assert.AreEqual<string>("sha384-vue-hash", result.integrity["vue"])
    Assert.AreEqual<string>("sha384-lodash-hash", result.integrity["lodash"])
