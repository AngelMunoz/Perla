module Perla.Tests.Extensibility

open System
open System.IO
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Xunit
open IcedTasks

open Perla.Plugins
open Perla.Plugins.Registry
open Perla.Extensibility
open Perla.Logger

// Test helpers
module TestHelpers =
  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.AddPerlaLogger() |> ignore)

    loggerFactory.CreateLogger<ExtensibilityService>()

  let createSimplePlugin name extension = plugin name {
    should_process_file(fun ext -> ext = extension)

    with_transform(fun file -> {
      file with
          content = $"[{name}] {file.content}"
    })
  }

// Basic ExtensibilityService tests
module ExtensibilityServiceBasicTests =

  [<Fact>]
  let ``ExtensibilityService.Create should return a service instance``() =
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    Assert.NotNull(service)

  [<Fact>]
  let ``GetAllPlugins should return empty list initially``() =
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let plugins = service.GetAllPlugins()
    Assert.Empty(plugins)

  [<Fact>]
  let ``GetRunnablePlugins should return empty list initially``() =
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let runnablePlugins = service.GetRunnablePlugins([])
    Assert.Empty(runnablePlugins)

  [<Fact>]
  let ``HasPluginsForExtension should return false initially``() =
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    Assert.False(service.HasPluginsForExtension(".test"))
    Assert.False(service.HasPluginsForExtension(".js"))
    Assert.False(service.HasPluginsForExtension(".css"))

// Plugin loading tests - testing the LoadPlugins API
module PluginLoadingTests =

  [<Fact>]
  let ``LoadPlugins should handle empty plugin array``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let result = service.LoadPlugins([||])

    match result with
    | Ok plugins -> Assert.Empty(plugins)
    | Error err ->
      Assert.Fail($"Expected success with empty list, but got error: {err}")
  }

  [<Fact>]
  let ``LoadPlugins should load esbuild plugin when provided``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let esbuildPlugin = TestHelpers.createSimplePlugin "esbuild-plugin" ".js"
    let result = service.LoadPlugins([||], esbuildPlugin)

    match result with
    | Ok plugins ->
      Assert.Single(plugins) |> ignore
      Assert.Equal("esbuild-plugin", plugins.[0].name)
    | Error err -> Assert.Fail($"Expected success, but got error: {err}")
  }

  [<Fact>]
  let ``LoadPlugins should return error for invalid script``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let invalidScript = "let x = 42" // No Plugin defined
    let pluginFiles = [| ("invalid-script", invalidScript) |]

    let result = service.LoadPlugins(pluginFiles)

    match result with
    | Ok _ -> Assert.Fail("Expected error for invalid script")
    | Error _ -> Assert.True(true) // Expected
  }

// Plugin retrieval tests - testing the getter APIs
module PluginRetrievalTests =

  [<Fact>]
  let ``GetAllPlugins should return loaded esbuild plugin``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let esbuildPlugin = TestHelpers.createSimplePlugin "test-plugin" ".js"
    let _ = service.LoadPlugins([||], esbuildPlugin)
    let plugins = service.GetAllPlugins()

    Assert.Single(plugins) |> ignore
    Assert.Equal("test-plugin", plugins.[0].name)
  }

  [<Fact>]
  let ``GetRunnablePlugins should return plugins in specified order``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let plugin1 = TestHelpers.createSimplePlugin "plugin1" ".js"
    let plugin2 = TestHelpers.createSimplePlugin "plugin2" ".js"

    // Load plugins by loading them as esbuild plugins (since we can't easily test script loading)
    let _ = service.LoadPlugins([||], plugin1)
    let _ = service.LoadPlugins([||], plugin2)

    let runnablePlugins = service.GetRunnablePlugins([ "plugin2"; "plugin1" ])

    Assert.Equal(2, runnablePlugins.Length)
    Assert.Equal("plugin2", runnablePlugins.[0].plugin.name)
    Assert.Equal("plugin1", runnablePlugins.[1].plugin.name)
  }

  [<Fact>]
  let ``HasPluginsForExtension should return true when plugins exist for extension``
    ()
    =
    taskUnit {
      let logger = TestHelpers.createLogger()
      let service = ExtensibilityService.Create(logger)

      let jsPlugin = TestHelpers.createSimplePlugin "js-plugin" ".js"
      let _ = service.LoadPlugins([||], jsPlugin)

      Assert.True(service.HasPluginsForExtension(".js"))
      Assert.False(service.HasPluginsForExtension(".css"))
    }

// Plugin execution tests - testing the RunPlugins API
module PluginExecutionTests =

  [<Fact>]
  let ``RunPlugins should handle empty plugin list``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let inputFile = {
      content = "unchanged"
      extension = ".txt"
    }

    let! result = service.RunPlugins [] inputFile

    Assert.Equal("unchanged", result.content)
    Assert.Equal(".txt", result.extension)
  }

  [<Fact>]
  let ``RunPlugins should execute loaded plugin``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let jsPlugin = TestHelpers.createSimplePlugin "js-plugin" ".js"
    let _ = service.LoadPlugins([||], jsPlugin)

    let inputFile = {
      content = "original content"
      extension = ".js"
    }

    let! result = service.RunPlugins [ "js-plugin" ] inputFile

    Assert.Equal("[js-plugin] original content", result.content)
    Assert.Equal(".js", result.extension)
  }

  [<Fact>]
  let ``RunPlugins should handle non-existent plugins gracefully``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let inputFile = {
      content = "test content"
      extension = ".js"
    }

    // Try to run a plugin that doesn't exist
    let! result = service.RunPlugins [ "non-existent-plugin" ] inputFile

    // Should return unchanged since plugin doesn't exist
    Assert.Equal("test content", result.content)
    Assert.Equal(".js", result.extension)
  }

  [<Fact>]
  let ``RunPlugins should only process files that match plugin extensions``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let jsPlugin = TestHelpers.createSimplePlugin "js-plugin" ".js"
    let _ = service.LoadPlugins([||], jsPlugin)

    let inputFile = {
      content = "should not change"
      extension = ".css" // Different extension
    }

    let! result = service.RunPlugins [ "js-plugin" ] inputFile

    // Should remain unchanged since extension doesn't match
    Assert.Equal("should not change", result.content)
    Assert.Equal(".css", result.extension)
  }

// Integration tests - testing the full ExtensibilityService workflow
module IntegrationTests =

  [<Fact>]
  let ``Full workflow with esbuild plugin``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    // Create an esbuild-like plugin
    let esbuildPlugin = plugin "esbuild" {
      should_process_file(fun ext -> ext = ".ts")

      with_transform(fun file -> {
        file with
            content = $"// Compiled from TypeScript\n{file.content}"
            extension = ".js"
      })
    }

    let result = service.LoadPlugins([||], esbuildPlugin)

    match result with
    | Ok plugins ->
      Assert.Single(plugins) |> ignore
      Assert.Equal("esbuild", plugins.[0].name)

      // Test that HasPluginsForExtension works
      Assert.True(service.HasPluginsForExtension(".ts"))
      Assert.False(service.HasPluginsForExtension(".css"))

      // Test plugin execution
      let inputFile = {
        content = "const x: number = 42;"
        extension = ".ts"
      }

      let! transformedFile = service.RunPlugins [ "esbuild" ] inputFile

      Assert.Equal(
        "// Compiled from TypeScript\nconst x: number = 42;",
        transformedFile.content
      )

      Assert.Equal(".js", transformedFile.extension)

    | Error err -> Assert.Fail($"Expected success, but got error: {err}")
  }

  [<Fact>]
  let ``Service should handle multiple plugins``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let plugin1 = TestHelpers.createSimplePlugin "plugin1" ".txt"
    let plugin2 = TestHelpers.createSimplePlugin "plugin2" ".txt"

    // Load plugins separately (simulating how they might be loaded in practice)
    let _ = service.LoadPlugins([||], plugin1)
    let _ = service.LoadPlugins([||], plugin2)

    let allPlugins = service.GetAllPlugins()
    Assert.Equal(2, allPlugins.Length)

    let inputFile = {
      content = "original"
      extension = ".txt"
    }

    let! result = service.RunPlugins [ "plugin1"; "plugin2" ] inputFile

    Assert.Equal("[plugin2] [plugin1] original", result.content)
    Assert.Equal(".txt", result.extension)
  }

// Error handling tests - testing ExtensibilityService error scenarios
module ErrorHandlingTests =

  [<Fact>]
  let ``LoadPlugins should handle script compilation errors``() = taskUnit {
    let logger = TestHelpers.createLogger()
    let service = ExtensibilityService.Create(logger)

    let invalidScript = "this is not valid F# code !!!"
    let pluginFiles = [| ("invalid", invalidScript) |]

    let result = service.LoadPlugins(pluginFiles)

    match result with
    | Ok _ -> Assert.Fail("Expected error for invalid script")
    | Error err -> Assert.True(true) // Expected error
  }
