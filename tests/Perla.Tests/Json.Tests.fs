// tests/Perla.Tests/Json.Tests.fs
module Perla.Tests.Json

open System
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open Perla.Json
open Perla.Types
open Perla.Units
open Perla.PkgManager
open JDeck
open FSharp.UMX

[<Fact>]
let ``DefaultJsonNodeOptions should have correct settings``() =
  let options = DefaultJsonNodeOptions()
  Assert.True(options.PropertyNameCaseInsensitive)

[<Fact>]
let ``DefaultJsonDocumentOptions should have correct settings``() =
  let options = DefaultJsonDocumentOptions()
  Assert.True(options.AllowTrailingCommas)
  Assert.Equal(JsonCommentHandling.Skip, options.CommentHandling)

[<Fact>]
let ``Json.ToBytes and Json.FromBytes should be symmetric``() =
  let testObj = {| Name = "Test"; Value = 42 |}
  let bytes = Json.ToBytes(testObj)
  let result = Json.FromBytes<obj>(bytes)

  // We can't directly compare the objects, but we can check the properties
  let resultNode = Json.ToNode(result)
  Assert.Equal("Test", resultNode.["Name"].GetValue<string>())
  Assert.Equal(42, resultNode.["Value"].GetValue<int>())

[<Fact>]
let ``Json.ToText should serialize correctly``() =
  let testObj = {| Name = "Test"; Value = 42 |}
  let jsonText = Json.ToText(testObj, minify = false)

  // Check for indented formatting
  Assert.Contains("\n", jsonText)
  Assert.Contains("  ", jsonText)

  let minifiedJsonText = Json.ToText(testObj, minify = true)
  // Check for minified formatting
  Assert.DoesNotContain("\n", minifiedJsonText)
  Assert.DoesNotContain("  ", minifiedJsonText)

[<Fact>]
let ``Json.ToNode should convert to JsonNode``() =
  let testObj = {| Name = "Test"; Value = 42 |}
  let node = Json.ToNode(testObj)
  Assert.NotNull(node)
  Assert.Equal("Test", node.["Name"].GetValue<string>())
  Assert.Equal(42, node.["Value"].GetValue<int>())

[<Fact>]
let ``PerlaConfigSection should have correct discriminated union cases``() =
  let indexSection = PerlaConfigSection.Index(Some "/index.html")
  let fableSection = PerlaConfigSection.Fable(None)
  let devServerSection = PerlaConfigSection.DevServer(None)
  let buildSection = PerlaConfigSection.Build(None)
  let dependenciesSection = PerlaConfigSection.Dependencies(None)

  match indexSection with
  | PerlaConfigSection.Index(Some path) -> Assert.Equal("/index.html", path)
  | _ -> Assert.True(false, "PerlaConfigSection.Index not matched correctly")

  match fableSection with
  | PerlaConfigSection.Fable(None) -> ()
  | _ -> Assert.True(false, "PerlaConfigSection.Fable not matched correctly")

  match devServerSection with
  | PerlaConfigSection.DevServer(None) -> ()
  | _ ->
    Assert.True(false, "PerlaConfigSection.DevServer not matched correctly")

  match buildSection with
  | PerlaConfigSection.Build(None) -> ()
  | _ -> Assert.True(false, "PerlaConfigSection.Build not matched correctly")

  match dependenciesSection with
  | PerlaConfigSection.Dependencies(None) -> ()
  | _ ->
    Assert.True(false, "PerlaConfigSection.Dependencies not matched correctly")

[<Fact>]
let ``Json.FromConfigFile should decode valid config correctly``() =
  let json =
    """
  {
    "index": "./index.html",
    "provider": "jspm",
    "useLocalPkgs": true,
    "fable": {
      "project": "./src/App.fsproj",
      "extension": ".fs",
      "sourceMaps": true,
      "outDir": "./dist"
    },
    "devServer": {
      "port": 8080,
      "host": "localhost",
      "liveReload": true,
      "useSSL": false
    },
    "build": {
      "includes": ["src/**/*.fs"],
      "excludes": ["node_modules/**"],
      "outDir": "./dist",
      "emitEnvFile": true
    },
    "dependencies": {
      "preact": "^10.11.0",
      "preact/hooks": "^1.0.0"
    },
    "paths": {
      "preact": "https://esm.sh/preact@10.11.0"
    }
  }
  """

  let result = Json.FromConfigFile(json)

  match result with
  | Ok config ->
    Assert.Equal(Some "./index.html", config.index |> Option.map UMX.untag)
    Assert.Equal(Some DownloadProvider.JspmIo, config.provider)
    Assert.Equal(Some true, config.useLocalPkgs)
    Assert.True(config.fable.IsSome)
    Assert.True(config.devServer.IsSome)
    Assert.True(config.build.IsSome)
    Assert.True(config.dependencies.IsSome)
    Assert.True(config.paths.IsSome)
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``Json.TestEventFromJson should decode SessionStart correctly``() =
  let runId = Guid.NewGuid()

  let json =
    $"""
  {{
    "event": "__perla-session-start",
    "runId": "{runId}",
    "stats": {{
      "suites": 5,
      "tests": 20,
      "passes": 18,
      "pending": 1,
      "failures": 1,
      "start": "2023-01-01T00:00:00Z",
      "end": "2023-01-01T00:01:00Z"
    }},
    "totalTests": 20
  }}
  """

  let result = Json.TestEventFromJson(json)

  match result with
  | Ok(SessionStart(id, stats, totalTests)) ->
    Assert.Equal(runId, id)
    Assert.Equal(5, stats.suites)
    Assert.Equal(20, stats.tests)
    Assert.Equal(18, stats.passes)
    Assert.Equal(1, stats.pending)
    Assert.Equal(1, stats.failures)
    Assert.Equal(20, totalTests)
  | Ok other -> Assert.True(false, $"Expected SessionStart but got {other}")
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``Json.TestEventFromJson should decode TestPass correctly``() =
  let runId = Guid.NewGuid()

  let json =
    $"""
  {{
    "event": "__perla-test-pass",
    "runId": "{runId}",
    "stats": {{
      "suites": 1,
      "tests": 1,
      "passes": 1,
      "pending": 0,
      "failures": 0,
      "start": "2023-01-01T00:00:00Z"
    }},
    "test": {{
      "body": "test body",
      "duration": 100.5,
      "fullTitle": "Suite Test",
      "id": "test-1",
      "pending": false,
      "speed": "fast",
      "state": "passed",
      "title": "Test",
      "type": "test"
    }}
  }}
  """

  let result = Json.TestEventFromJson(json)

  match result with
  | Ok(TestPass(id, stats, test)) ->
    Assert.Equal(runId, id)
    Assert.Equal(1, stats.passes)
    Assert.Equal("test body", test.body)
    Assert.Equal(Some 100.5, test.duration)
    Assert.Equal("Suite Test", test.fullTitle)
    Assert.Equal("test-1", test.id)
    Assert.False(test.pending)
    Assert.Equal(Some "fast", test.speed)
    Assert.Equal(Some "passed", test.state)
    Assert.Equal("Test", test.title)
    Assert.Equal("test", test.``type``)
  | Ok other -> Assert.True(false, $"Expected TestPass but got {other}")
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``Json.TestEventFromJson should decode TestFailed correctly``() =
  let runId = Guid.NewGuid()

  let json =
    $"""
  {{
    "event": "__perla-test-failed",
    "runId": "{runId}",
    "stats": {{
      "suites": 1,
      "tests": 1,
      "passes": 0,
      "pending": 0,
      "failures": 1,
      "start": "2023-01-01T00:00:00Z"
    }},
    "test": {{
      "body": "test body",
      "fullTitle": "Suite Test",
      "id": "test-1",
      "pending": false,
      "title": "Test",
      "type": "test"
    }},
    "message": "Test failed",
    "stack": "Error: Test failed\\n    at test.js:10:5"
  }}
  """

  let result = Json.TestEventFromJson(json)

  match result with
  | Ok(TestFailed(id, stats, test, message, stack)) ->
    Assert.Equal(runId, id)
    Assert.Equal(1, stats.failures)
    Assert.Equal("test-1", test.id)
    Assert.Equal("Test failed", message)
    Assert.Contains("Error: Test failed", stack)
  | Ok other -> Assert.True(false, $"Expected TestFailed but got {other}")
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``Json.TestEventFromJson should decode SuiteStart correctly``() =
  let runId = Guid.NewGuid()

  let json =
    $"""
  {{
    "event": "__perla-suite-start",
    "runId": "{runId}",
    "stats": {{
      "suites": 1,
      "tests": 0,
      "passes": 0,
      "pending": 0,
      "failures": 0,
      "start": "2023-01-01T00:00:00Z"
    }},
    "suite": {{
      "id": "suite-1",
      "title": "Test Suite",
      "fullTitle": "Test Suite",
      "root": true,
      "parent": null,
      "pending": false,
      "tests": []
    }}
  }}
  """

  let result = Json.TestEventFromJson(json)

  match result with
  | Ok(SuiteStart(id, stats, suite)) ->
    Assert.Equal(runId, id)
    Assert.Equal(1, stats.suites)
    Assert.Equal("suite-1", suite.id)
    Assert.Equal("Test Suite", suite.title)
    Assert.True(suite.root)
    Assert.Equal(None, suite.parent)
    Assert.False(suite.pending)
    Assert.Empty(suite.tests)
  | Ok other -> Assert.True(false, $"Expected SuiteStart but got {other}")
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``Json.TestEventFromJson should decode SessionEnd correctly``() =
  let runId = Guid.NewGuid()

  let json =
    $"""
  {{
    "event": "__perla-session-end",
    "runId": "{runId}",
    "stats": {{
      "suites": 5,
      "tests": 20,
      "passes": 18,
      "pending": 1,
      "failures": 1,
      "start": "2023-01-01T00:00:00Z",
      "end": "2023-01-01T00:01:00Z"
    }}
  }}
  """

  let result = Json.TestEventFromJson(json)

  match result with
  | Ok(SessionEnd(id, stats)) ->
    Assert.Equal(runId, id)
    Assert.Equal(5, stats.suites)
    Assert.Equal(20, stats.tests)
    Assert.Equal(18, stats.passes)
    Assert.Equal(1, stats.pending)
    Assert.Equal(1, stats.failures)
  | Ok other -> Assert.True(false, $"Expected SessionEnd but got {other}")
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``Json.TestEventFromJson should decode TestImportFailed correctly``() =
  let runId = Guid.NewGuid()

  let json =
    $"""
  {{
    "event": "__perla-test-import-failed",
    "runId": "{runId}",
    "message": "Failed to import test file",
    "stack": "Error: Failed to import\\n    at import.js:5:10"
  }}
  """

  let result = Json.TestEventFromJson(json)

  match result with
  | Ok(TestImportFailed(id, message, stack)) ->
    Assert.Equal(runId, id)
    Assert.Equal("Failed to import test file", message)
    Assert.Contains("Error: Failed to import", stack)
  | Ok other -> Assert.True(false, $"Expected TestImportFailed but got {other}")
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``Json.TestEventFromJson should decode TestRunFinished correctly``() =
  let runId = Guid.NewGuid()

  let json =
    $"""
  {{
    "event": "__perla-test-run-finished",
    "runId": "{runId}"
  }}
  """

  let result = Json.TestEventFromJson(json)

  match result with
  | Ok(TestRunFinished id) -> Assert.Equal(runId, id)
  | Ok other -> Assert.True(false, $"Expected TestRunFinished but got {other}")
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``Json.TestEventFromJson should return error for unknown event``() =
  let json =
    """
  {
    "event": "__perla-unknown-event",
    "runId": "12345678-1234-1234-1234-123456789012"
  }
  """

  let result = Json.TestEventFromJson(json)

  match result with
  | Ok _ -> Assert.True(false, "Expected Error but got Ok")
  | Error error -> Assert.Contains("is not a known event", error.message)

[<Fact>]
let ``PerlaConfig.FromString should create valid config from JSON``() =
  let json =
    """
  {
    "index": "./index.html",
    "provider": "jspm",
    "useLocalPkgs": true,
    "fable": {
      "project": "./src/App.fsproj",
      "extension": ".fs",
      "sourceMaps": true,
      "outDir": "./dist"
    },
    "devServer": {
      "port": 8080,
      "host": "localhost",
      "liveReload": true,
      "useSSL": false
    },
    "build": {
      "includes": ["src/**/*.fs"],
      "excludes": ["node_modules/**"],
      "outDir": "./dist",
      "emitEnvFile": true
    },
    "dependencies": {
      "preact": "^10.11.0"
    }
  }
  """

  let config = PerlaConfig.FromString(json)

  Assert.Equal("./index.html", config.index |> UMX.untag)
  Assert.Equal(DownloadProvider.JspmIo, config.provider)
  Assert.True(config.useLocalPkgs)
  Assert.True(config.fable.IsSome)
  Assert.Equal(8080, config.devServer.port)
  Assert.Equal("localhost", config.devServer.host)
  Assert.True(config.devServer.liveReload)
  Assert.False(config.devServer.useSSL)
  Assert.Contains("src/**/*.fs", config.build.includes)
  Assert.Contains("node_modules/**", config.build.excludes)
  Assert.Equal("./dist", config.build.outDir |> UMX.untag)
  Assert.True(config.build.emitEnvFile)

[<Fact>]
let ``PerlaConfig.UpdateFileFields should update provider correctly``() =
  let originalJson = JsonObject()
  let fields = [ PerlaConfig.Provider DownloadProvider.JspmIo ]

  let result = PerlaConfig.UpdateFileFields (Some originalJson) fields

  Assert.NotNull(result.["provider"])
  Assert.Equal("jspm.io", result.["provider"].GetValue<string>())

[<Fact>]
let ``PerlaConfig.UpdateFileFields should update dependencies correctly``() =
  let originalJson = JsonObject()

  let deps =
    Set.ofList [
      {
        package = "preact"
        version = "^10.11.0" |> UMX.tag<Semver>
      }
      {
        package = "preact/hooks"
        version = "^1.0.0" |> UMX.tag<Semver>
      }
    ]

  let fields = [ PerlaConfig.Dependencies deps ]

  let result = PerlaConfig.UpdateFileFields (Some originalJson) fields

  Assert.NotNull(result.["dependencies"])
  let depsObj = result.["dependencies"].AsObject()
  Assert.Equal("^10.11.0", depsObj.["preact"].GetValue<string>())
  Assert.Equal("^1.0.0", depsObj.["preact/hooks"].GetValue<string>())

[<Fact>]
let ``PerlaConfig.UpdateFileFields should update useLocalPkgs correctly``() =
  let originalJson = JsonObject()
  let fields = [ PerlaConfig.UseLocalPkgs true ]

  let result = PerlaConfig.UpdateFileFields (Some originalJson) fields

  Assert.NotNull(result.["useLocalPkgs"])
  Assert.True(result.["useLocalPkgs"].GetValue<bool>())

[<Fact>]
let ``PerlaConfig.UpdateFileFields should update fable fields correctly``() =
  let originalJson = JsonObject()

  let fableFields = [
    PerlaConfig.FableField.Project "./src/App.fsproj"
    PerlaConfig.FableField.Extension ".fs"
    PerlaConfig.FableField.SourceMaps true
    PerlaConfig.FableField.OutDir true
  ]

  let fields = [ PerlaConfig.Fable fableFields ]

  let result = PerlaConfig.UpdateFileFields (Some originalJson) fields

  Assert.NotNull(result.["fable"])
  let fableObj = result.["fable"].AsObject()
  Assert.Equal("./src/App.fsproj", fableObj.["project"].GetValue<string>())
  Assert.Equal(".fs", fableObj.["extension"].GetValue<string>())
  Assert.True(fableObj.["sourceMaps"].GetValue<bool>())
  Assert.True(fableObj.["outDir"].GetValue<bool>())

[<Fact>]
let ``PerlaConfig.UpdateFileFields should update paths correctly``() =
  let originalJson = JsonObject()

  let paths =
    Map.ofList [
      ("preact" |> UMX.tag<BareImport>,
       "https://esm.sh/preact@10.11.0" |> UMX.tag<ResolutionUrl>)
      ("preact/hooks" |> UMX.tag<BareImport>,
       "https://esm.sh/preact@10.11.0/hooks" |> UMX.tag<ResolutionUrl>)
    ]

  let fields = [ PerlaConfig.Paths paths ]

  let result = PerlaConfig.UpdateFileFields (Some originalJson) fields

  Assert.NotNull(result.["paths"])
  let pathsObj = result.["paths"].AsObject()

  Assert.Equal(
    "https://esm.sh/preact@10.11.0",
    pathsObj.["preact"].GetValue<string>()
  )

  Assert.Equal(
    "https://esm.sh/preact@10.11.0/hooks",
    pathsObj.["preact/hooks"].GetValue<string>()
  )

[<Fact>]
let ``PerlaConfig.UpdateFileFields should add schema when not present``() =
  let originalJson = JsonObject()
  let fields = [ PerlaConfig.UseLocalPkgs true ]

  let result = PerlaConfig.UpdateFileFields (Some originalJson) fields

  Assert.NotNull(result.["$schema"])
  Assert.Contains("perla.schema.json", result.["$schema"].GetValue<string>())

[<Fact>]
let ``PerlaConfig.UpdateFileFields should preserve existing schema``() =
  let originalJson = JsonObject()
  originalJson["$schema"] <- JsonValue.Create("custom-schema.json")
  let fields = [ PerlaConfig.UseLocalPkgs true ]

  let result = PerlaConfig.UpdateFileFields (Some originalJson) fields

  Assert.NotNull(result.["$schema"])
  Assert.Equal("custom-schema.json", result.["$schema"].GetValue<string>())

[<Fact>]
let ``PerlaConfig.UpdateFileFields should handle None jsonContents``() =
  let fields = [ PerlaConfig.UseLocalPkgs true ]

  let result = PerlaConfig.UpdateFileFields None fields

  Assert.NotNull(result.["$schema"])
  Assert.NotNull(result.["useLocalPkgs"])
  Assert.True(result.["useLocalPkgs"].GetValue<bool>())

// Tests for TemplateDecoders
[<Fact>]
let ``TemplateConfigItemDecoder should decode correctly``() =
  let json =
    """
  {
    "id": "template-1",
    "name": "React Template",
    "path": "./templates/react",
    "shortName": "react",
    "description": "A React template"
  }
  """

  let result =
    Decoding.auto<DecodedTemplateConfigItem>(json, DefaultJsonOptions())

  match result with
  | Ok item ->
    Assert.Equal("template-1", item.id)
    Assert.Equal("React Template", item.name)
    Assert.Equal("./templates/react", item.path |> UMX.untag)
    Assert.Equal("react", item.shortName)
    Assert.Equal(Some "A React template", item.description)
  | Error error ->
    Assert.True(false, $"Expected Ok but got Error: {error.message}")

[<Fact>]
let ``TemplateConfigurationDecoder should decode correctly``() =
  let json =
    """
  {
    "name": "Perla Templates",
    "group": "web",
    "templates": [
      {
        "id": "template-1",
        "name": "React Template",
        "path": "./templates/react",
        "shortName": "react",
        "description": "A React template"
      }
    ],
    "author": "Perla Team",
    "license": "MIT",
    "description": "Official Perla templates",
    "repositoryUrl": "https://github.com/perla/templates"
  }
  """

  let result =
    Decoding.auto<DecodedTemplateConfiguration>(json, DefaultJsonOptions())

  match result with
  | Ok config ->
    Assert.Equal("Perla Templates", config.name)
    Assert.Equal("web", config.group)
    Assert.Equal(1, config.templates |> Seq.length)
    Assert.Equal(Some "Perla Team", config.author)
    Assert.Equal(Some "MIT", config.license)
    Assert.Equal(Some "Official Perla templates", config.description)

    Assert.Equal(
      Some "https://github.com/perla/templates",
      config.repositoryUrl
    )
  | Error error ->
    Assert.True(false, $"Expected Ok but got Error: {error.message}")

// Tests for internal TestDecoders
[<Fact>]
let ``TestDecoders.TestStats should decode correctly``() =
  let json =
    """
  {
    "suites": 5,
    "tests": 20,
    "passes": 18,
    "pending": 1,
    "failures": 1,
    "start": "2023-01-01T00:00:00Z",
    "end": "2023-01-01T00:01:00Z"
  }
  """

  let result = Decoding.auto<TestStats>(json, DefaultJsonOptions())

  match result with
  | Ok stats ->
    Assert.Equal(5, stats.suites)
    Assert.Equal(20, stats.tests)
    Assert.Equal(18, stats.passes)
    Assert.Equal(1, stats.pending)
    Assert.Equal(1, stats.failures)
    Assert.Equal(DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc), stats.start)

    Assert.Equal(
      Some(DateTime(2023, 1, 1, 0, 1, 0, DateTimeKind.Utc)),
      stats.``end``
    )
  | Error error ->
    Assert.True(false, $"Expected Ok but got Error: {error.message}")

[<Fact>]
let ``TestDecoders.Test should decode correctly``() =
  let json =
    """
  {
    "body": "test body",
    "duration": 100.5,
    "fullTitle": "Suite Test",
    "id": "test-1",
    "pending": false,
    "speed": "fast",
    "state": "passed",
    "title": "Test",
    "type": "test"
  }
  """

  let result = Decoding.auto(json, DefaultJsonOptions())

  match result with
  | Ok test ->
    Assert.Equal("test body", test.body)
    Assert.Equal(Some 100.5, test.duration)
    Assert.Equal("Suite Test", test.fullTitle)
    Assert.Equal("test-1", test.id)
    Assert.False(test.pending)
    Assert.Equal(Some "fast", test.speed)
    Assert.Equal(Some "passed", test.state)
    Assert.Equal("Test", test.title)
    Assert.Equal("test", test.``type``)
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``TestDecoders.Suite should decode correctly``() =
  let json =
    """
  {
    "id": "suite-1",
    "title": "Test Suite",
    "fullTitle": "Test Suite",
    "root": true,
    "parent": "parent-suite",
    "pending": false,
    "tests": [
      {
        "body": "test body",
        "fullTitle": "Suite Test",
        "id": "test-1",
        "pending": false,
        "title": "Test",
        "type": "test"
      }
    ]
  }
  """

  let result = Decoding.auto(json, DefaultJsonOptions())

  match result with
  | Ok suite ->
    Assert.Equal("suite-1", suite.id)
    Assert.Equal("Test Suite", suite.title)
    Assert.Equal("Test Suite", suite.fullTitle)
    Assert.True(suite.root)
    Assert.Equal(Some "parent-suite", suite.parent)
    Assert.False(suite.pending)
    Assert.Equal(1, suite.tests.Length)
    Assert.Equal("test-1", suite.tests.[0].id)
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

// Tests for internal ConfigEncoders
[<Fact>]
let ``ConfigEncoders.Browser should encode correctly``() =
  let browser = Browser.Chrome
  let encoded = Encoders.Browser browser

  // Just check that it encodes to a JToken without pattern matching
  Assert.NotNull(encoded)
  Assert.True(encoded.ToString().Contains("chrome"))

[<Fact>]
let ``ConfigEncoders.BrowserMode should encode correctly``() =
  let browserMode = BrowserMode.Parallel
  let encoded = Encoders.BrowserMode browserMode

  // Just check that it encodes to a JToken without pattern matching
  Assert.NotNull(encoded)
  Assert.True(encoded.ToString().Contains("parallel"))

[<Fact>]
let ``ConfigEncoders.TestConfig should encode correctly``() =
  let testConfig = {
    browsers = [ Browser.Chrome; Browser.Firefox ]
    includes = [ "src/**/*.test.js" ]
    excludes = [ "node_modules/**" ]
    watch = true
    headless = false
    browserMode = BrowserMode.Parallel
    fable = None
  }

  let encoded = Json.ToText(testConfig)

  // Just check that it encodes to a JToken without pattern matching
  Assert.NotNull(encoded)
  let jsonString = encoded.ToString()
  Assert.True(jsonString.Contains("browsers"))
  Assert.True(jsonString.Contains("includes"))
  Assert.True(jsonString.Contains("excludes"))
  Assert.True(jsonString.Contains("watch"))
  Assert.True(jsonString.Contains("headless"))
  Assert.True(jsonString.Contains("browserMode"))

[<Fact>]
let ``ConfigDecoders.PerlaDecoder should decode complete config correctly``() =
  let json =
    """
  {
    "index": "./index.html",
    "provider": "jspm",
    "useLocalPkgs": true,
    "plugins": ["@perla/plugin-example"],
    "build": {
      "includes": ["src/**/*.fs"],
      "excludes": ["node_modules/**"],
      "outDir": "./dist",
      "emitEnvFile": true
    },
    "devServer": {
      "port": 8080,
      "host": "localhost",
      "liveReload": true,
      "useSSL": false,
      "proxy": {
        "/api": "http://localhost:3000"
      }
    },
    "fable": {
      "project": "./src/App.fsproj",
      "extension": ".fs",
      "sourceMaps": true,
      "outDir": "./dist"
    },
    "esbuild": {
      "esBuildPath": "./node_modules/.bin/esbuild",
      "version": "0.14.0",
      "ecmaVersion": "es2020",
      "minify": true,
      "injects": ["./src/inject.js"],
      "externals": ["react"],
      "fileLoaders": {
        ".svg": "file"
      },
      "jsxAutomatic": true,
      "jsxImportSource": "preact"
    },
    "testing": {
      "browsers": ["chrome", "firefox"],
      "includes": ["src/**/*.test.js"],
      "excludes": ["node_modules/**"],
      "watch": true,
      "headless": false,
      "browserMode": "parallel",
      "fable": {
        "project": "./src/Tests.fsproj",
        "extension": ".fs"
      }
    },
    "mountDirectories": {
      "/assets": "./assets"
    },
    "enableEnv": true,
    "envPath": "/.env",
    "paths": {
      "preact": "https://esm.sh/preact@10.11.0"
    },
    "dependencies": {
      "preact": "^10.11.0",
      "preact/hooks": "^1.0.0"
    }
  }
  """

  let result = Decoding.auto<DecodedPerlaConfig>(json, DefaultJsonOptions())

  match result with
  | Ok config ->
    Assert.Equal("./index.html", UMX.untag config.index.Value)
    Assert.Equal(DownloadProvider.JspmIo, config.provider.Value)
    Assert.Equal(true, config.useLocalPkgs.Value)
    Assert.True(config.plugins.IsSome)
    Assert.Equal(1, config.plugins.Value.Length)
    Assert.Equal("@perla/plugin-example", config.plugins.Value[0])
    Assert.True(config.build.IsSome)
    Assert.True(config.devServer.IsSome)
    Assert.True(config.fable.IsSome)
    Assert.True(config.esbuild.IsSome)
    Assert.True(config.testing.IsSome)
    Assert.True(config.mountDirectories.IsSome)
    Assert.Equal(true, config.enableEnv.Value)
    Assert.Equal("/.env", UMX.untag config.envPath.Value)
    Assert.True(config.paths.IsSome)
    Assert.True(config.dependencies.IsSome)
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")

[<Fact>]
let ``ConfigDecoders.PerlaDecoder should handle minimal config correctly``() =
  let json =
    """
  {
    "index": "./index.html"
  }
  """

  let result =
    Decoding.auto<DecodedPerlaConfig>(
      json,
      DefaultJsonOptions(),
      DefaultJsonDocumentOptions()
    )

  match result with
  | Ok config ->
    Assert.Equal("./index.html", UMX.untag config.index.Value)
    Assert.Equal(None, config.provider)
    Assert.Equal(None, config.useLocalPkgs)
    Assert.Equal(None, config.plugins)
    Assert.Equal(None, config.build)
    Assert.Equal(None, config.devServer)
    Assert.Equal(None, config.fable)
    Assert.Equal(None, config.esbuild)
    Assert.Equal(None, config.testing)
    Assert.Equal(None, config.mountDirectories)
    Assert.Equal(None, config.enableEnv)
    Assert.Equal(None, config.envPath)
    Assert.Equal(None, config.paths)
    Assert.Equal(None, config.dependencies)
  | Error error -> Assert.True(false, $"Expected Ok but got Error: {error}")
