module Perla.PkgManager.Types.Tests

open System
open Xunit
open Perla.PkgManager

[<Fact>]
let ``GeneratorOption toDict should convert BaseUrl correctly``() =
  // Arrange
  let baseUrl = Uri("https://example.com/")
  let options = [ BaseUrl baseUrl ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("baseUrl"))

  Assert.Equal<string>("https://example.com/", result["baseUrl"] :?> string)

[<Fact>]
let ``GeneratorOption toDict should convert DefaultProvider correctly``() =
  // Arrange
  let options = [ DefaultProvider Provider.JspmIo ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("defaultProvider"))
  Assert.Equal<string>("jspm.io", result["defaultProvider"] :?> string)

[<Fact>]
let ``GeneratorOption toDict should convert custom Provider correctly``() =
  // Arrange
  let options = [ DefaultProvider(Provider.Custom "custom-provider") ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("defaultProvider"))

  Assert.Equal<string>("custom-provider", result["defaultProvider"] :?> string)

[<Fact>]
let ``GeneratorOption toDict should convert Cache option correctly``() =
  // Arrange
  let options = [ Cache(CacheOption.Enabled true) ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("cache"))
  Assert.Equal<bool>(true, result["cache"] :?> bool)

[<Fact>]
let ``GeneratorOption toDict should convert offline Cache option correctly``() =
  // Arrange
  let options = [ Cache CacheOption.Offline ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("cache"))
  Assert.Equal<string>("offline", result["cache"] :?> string)

[<Fact>]
let ``GeneratorOption toDict should convert Env option correctly``() =
  // Arrange
  let envSet =
    Set.ofList [ ExportCondition.Development; ExportCondition.Browser ]

  let options = [ Env envSet ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("env"))
  let envResult = result["env"] :?> Set<string>
  Assert.True(envResult.Contains("development"))
  Assert.True(envResult.Contains("browser"))
  Assert.Equal<int>(2, envResult.Count)

[<Fact>]
let ``GeneratorOption toDict should handle custom ExportCondition``() =
  // Arrange
  let envSet = Set.ofList [ ExportCondition.Custom "custom-env" ]
  let options = [ Env envSet ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("env"))
  let envResult = result["env"] :?> Set<string>
  Assert.True(envResult.Contains("custom-env"))

[<Fact>]
let ``GeneratorOption toDict should convert multiple options correctly``() =
  // Arrange
  let options = [
    BaseUrl(Uri("https://example.com/"))
    FlattenScopes true
    CombineSubPaths false
  ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.Equal<int>(3, result.Count)
  Assert.True(result.ContainsKey("baseUrl"))
  Assert.True(result.ContainsKey("flattenScopes"))
  Assert.True(result.ContainsKey("combineSubPaths"))

  Assert.Equal<string>("https://example.com/", result["baseUrl"] :?> string)

  Assert.Equal<bool>(true, result["flattenScopes"] :?> bool)
  Assert.Equal<bool>(false, result["combineSubPaths"] :?> bool)

[<Fact>]
let ``GeneratorOption toDict should handle Providers map``() =
  // Arrange
  let providers = Map.ofList [ ("react", "jsdelivr"); ("lodash", "unpkg") ]
  let options = [ Providers providers ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("providers"))
  let providersResult = result["providers"] :?> Map<string, string>
  Assert.Equal<string>("jsdelivr", providersResult["react"])
  Assert.Equal<string>("unpkg", providersResult["lodash"])

[<Fact>]
let ``GeneratorOption toDict should handle Ignore set``() =
  // Arrange
  let ignoreSet = Set.ofList [ "node_modules"; ".git" ]
  let options = [ Ignore ignoreSet ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("ignore"))
  let ignoreResult = result["ignore"] :?> Set<string>
  Assert.True(ignoreResult.Contains("node_modules"))
  Assert.True(ignoreResult.Contains(".git"))
  Assert.Equal<int>(2, ignoreResult.Count)

[<Fact>]
let ``GeneratorOption toDict should handle MapUrl correctly``() =
  // Arrange
  let mapUrl = Uri("https://example.com/importmap.json")
  let options = [ MapUrl mapUrl ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("mapUrl"))

  Assert.Equal<string>(
    "https://example.com/importmap.json",
    result["mapUrl"] :?> string
  )

[<Fact>]
let ``GeneratorOption toDict should handle RootUrl correctly``() =
  // Arrange
  let rootUrl = Uri("https://example.com/root/")
  let options = [ RootUrl rootUrl ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("rootUrl"))

  Assert.Equal<string>(
    "https://example.com/root/",
    result["rootUrl"] :?> string
  )

[<Fact>]
let ``GeneratorOption toDict should handle InputMap correctly``() =
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
  Assert.True(result.ContainsKey("inputMap"))
  let resultMap = result["inputMap"] :?> ImportMap
  Assert.Equal<int>(1, resultMap.imports.Count)

  Assert.Equal<string>(
    "https://example.com/react.js",
    resultMap.imports["react"]
  )

[<Fact>]
let ``GeneratorOption toDict should handle all Provider types correctly``() =
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
    Assert.True(result.ContainsKey("defaultProvider"))
    Assert.Equal<string>(expected, result["defaultProvider"] :?> string)

[<Fact>]
let ``GeneratorOption toDict should handle ProviderConfig correctly``() =
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
  Assert.True(result.ContainsKey("providerConfig"))

  let resultConfig =
    result["providerConfig"] :?> Map<string, Map<string, string>>

  Assert.Equal<int>(2, resultConfig.Count)
  Assert.Equal<string>("5000", resultConfig["jsdelivr"]["timeout"])
  Assert.Equal<string>("https://unpkg.com", resultConfig["unpkg"]["cdn"])

[<Fact>]
let ``GeneratorOption toDict should handle Resolutions correctly``() =
  // Arrange
  let resolutions = Map.ofList [ ("react", "18.2.0"); ("vue", "3.5.17") ]
  let options = [ Resolutions resolutions ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("resolutions"))
  let resultResolutions = result["resolutions"] :?> Map<string, string>
  Assert.Equal<int>(2, resultResolutions.Count)
  Assert.Equal<string>("18.2.0", resultResolutions["react"])
  Assert.Equal<string>("3.5.17", resultResolutions["vue"])

[<Fact>]
let ``GeneratorOption toDict should handle all ExportCondition types correctly``
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
  Assert.True(result.ContainsKey("env"))
  let envResult = result["env"] :?> Set<string>
  Assert.Equal<int>(4, envResult.Count)
  Assert.True(envResult.Contains("development"))
  Assert.True(envResult.Contains("browser"))
  Assert.True(envResult.Contains("module"))
  Assert.True(envResult.Contains("test"))

[<Fact>]
let ``GeneratorOption toDict should handle boolean flags correctly``() =
  // Arrange
  let options = [ FlattenScopes true; CombineSubPaths false ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("flattenScopes"))
  Assert.True(result.ContainsKey("combineSubPaths"))
  Assert.Equal<bool>(true, result["flattenScopes"] :?> bool)
  Assert.Equal<bool>(false, result["combineSubPaths"] :?> bool)

[<Fact>]
let ``GeneratorOption toDict should handle empty collections correctly``() =
  // Arrange
  let options = [ Providers Map.empty; Ignore Set.empty; Env Set.empty ]

  // Act
  let result = GeneratorOption.toDict options

  // Assert
  Assert.True(result.ContainsKey("providers"))
  Assert.True(result.ContainsKey("ignore"))
  Assert.True(result.ContainsKey("env"))

  let providers = result["providers"] :?> Map<string, string>
  let ignore = result["ignore"] :?> Set<string>
  let env = result["env"] :?> Set<string>

  Assert.True(providers.IsEmpty)
  Assert.True(ignore.IsEmpty)
  Assert.True(env.IsEmpty)

[<Fact>]
let ``GeneratorOption toDict should handle duplicate options by keeping first value``
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
  Assert.Equal<string>("https://first.com/", result["baseUrl"] :?> string)
  Assert.Equal<bool>(true, result["flattenScopes"] :?> bool)
