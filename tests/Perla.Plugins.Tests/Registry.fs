module Perla.Plugins.Tests.Registry


open System
open System.Collections.Generic
open System.Threading.Tasks

open Xunit

open FSharp.Compiler.Interactive.Shell
open IcedTasks

open Perla.Plugins
open Perla.Plugins.Registry

let pluginFactory(amount: int) = [
  for i in 1..amount do
    {
      name = sprintf "test%d" i
      shouldProcessFile =
        if i % 2 = 0 then ValueSome(fun _ -> true) else ValueNone
      transform =
        ValueSome(fun file ->
          ValueTask.FromResult(
            {
              file with
                  content = $"--- %i{i}\n%s{file.content}"
            }
          ))
    }
]

module PluginManagerRegistryTests =
  open System.IO

  [<Fact>]
  let ``GetRunnablePlugins should return only those who have a "should process file" function``
    ()
    =
    taskUnit {
      use stdout = new StringWriter()
      use stderr = new StringWriter()

      let manager = PluginManager.Create(stdout, stderr)

      // Add plugins from the factory
      let plugins = pluginFactory 10

      for plugin in plugins do
        manager.AddPlugin(plugin) |> ignore

      let runnables =
        manager.GetRunnablePlugins(
          [
            "test1"
            "test2"
            "test3"
            "test4"
            "test5"
            "test6"
            "test7"
            "test8"
            "test9"
            "test10"
          ]
        )

      Assert.Equal(5, runnables.Length)
    }

  [<Fact>]
  let ``GetAllPlugins should bring all of the plugins added to manager``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    // Add plugins from the factory
    let plugins = pluginFactory 10

    for plugin in plugins do
      manager.AddPlugin(plugin) |> ignore

    let allPlugins = manager.GetAllPlugins()

    Assert.Equal(10, allPlugins.Length)
  }

  [<Fact>]
  let ``LoadFromCode should give error if the plugin is already there``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    let plugin = (pluginFactory 1)[0]
    manager.AddPlugin(plugin) |> ignore

    let result = manager.LoadFromCode(plugin)

    match result with
    | Error(PluginLoadError.AlreadyLoaded _) -> Assert.True(true)
    | Error err -> Assert.Fail $"Expected AlreadyLoaded error, but got %A{err}"
    | Ok() -> Assert.Fail $"Expected error, but got success"
  }

  [<Fact>]
  let ``LoadFromCode should be successful if the plugin is not in the cache``
    ()
    =
    taskUnit {
      use stdout = new StringWriter()
      use stderr = new StringWriter()

      let manager = PluginManager.Create(stdout, stderr)

      let plugin = {
        name = "test11"
        shouldProcessFile = ValueNone
        transform = ValueNone
      }

      let result = manager.LoadFromCode(plugin)

      match result with
      | Ok() -> Assert.True true
      | Error result -> Assert.Fail $"Expected success, but got %A{result}"
    }

module PluginManagerTests =
  open System.IO

  let createTestPlugin name shouldProcess = {
    name = name
    shouldProcessFile =
      if shouldProcess then
        ValueSome(fun ext -> ext = ".test")
      else
        ValueNone
    transform =
      ValueSome(fun file ->
        ValueTask.FromResult(
          {
            file with
                content = $"[{name}] {file.content}"
          }
        ))
  }

  let createPluginScript name =
    $"""
open System.Threading.Tasks

type FileTransform = {{
  content: string
  extension: string
}}

type FilePredicate = string -> bool

type TransformAction = FileTransform -> ValueTask<FileTransform>

type PluginInfo = {{
  name: string
  shouldProcessFile: FilePredicate voption
  transform: TransformAction voption
}}

let Plugin = {{
  name = "{name}"
  shouldProcessFile = ValueSome(fun ext -> ext = ".test")
  transform = ValueSome(fun file ->
    ValueTask.FromResult({{ file with content = "[{name}] " + file.content }}))
}}
"""

  [<Fact>]
  let ``PluginManager.Create should return a working plugin manager``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    Assert.NotNull(manager)
  }

  [<Fact>]
  let ``AddPlugin should successfully add a new plugin``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let plugin = createTestPlugin "test-plugin" true

    let result = manager.AddPlugin(plugin)

    match result with
    | Ok() -> Assert.True(true)
    | Error err -> Assert.Fail($"Expected success, but got {err}")
  }

  [<Fact>]
  let ``AddPlugin should return error if plugin already exists``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let plugin = createTestPlugin "test-plugin" true

    let result1 = manager.AddPlugin(plugin)
    let result2 = manager.AddPlugin(plugin)

    match result1, result2 with
    | Ok(), Error(AlreadyLoaded _) -> Assert.True(true)
    | _, _ ->
      Assert.Fail(
        $"Expected Ok() then AlreadyLoaded error, but got {result1}, {result2}"
      )
  }

  [<Fact>]
  let ``GetPlugin should return plugin if it exists``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let plugin = createTestPlugin "test-plugin" true

    manager.AddPlugin(plugin) |> ignore
    let retrieved = manager.GetPlugin("test-plugin")

    match retrieved with
    | Some p -> Assert.Equal("test-plugin", p.name)
    | None -> Assert.Fail("Expected to find plugin")
  }

  [<Fact>]
  let ``GetPlugin should return None if plugin doesn't exist``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let retrieved = manager.GetPlugin("non-existent")

    match retrieved with
    | None -> Assert.True(true)
    | Some _ -> Assert.Fail("Expected None for non-existent plugin")
  }

  [<Fact>]
  let ``HasPlugin should return true if plugin exists``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let plugin = createTestPlugin "test-plugin" true

    manager.AddPlugin(plugin) |> ignore
    let exists = manager.HasPlugin("test-plugin")

    Assert.True(exists)
  }

  [<Fact>]
  let ``HasPlugin should return false if plugin doesn't exist``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let exists = manager.HasPlugin("non-existent")

    Assert.False(exists)
  }

  [<Fact>]
  let ``GetAllPlugins should return all added plugins``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let plugin1 = createTestPlugin "plugin1" true
    let plugin2 = createTestPlugin "plugin2" false

    manager.AddPlugin(plugin1) |> ignore
    manager.AddPlugin(plugin2) |> ignore

    let allPlugins = manager.GetAllPlugins()

    Assert.Equal(2, allPlugins.Length)
    Assert.Contains(allPlugins, fun p -> p.name = "plugin1")
    Assert.Contains(allPlugins, fun p -> p.name = "plugin2")
  }

  [<Fact>]
  let ``GetRunnablePlugins should return only plugins with transform functions``
    ()
    =
    taskUnit {
      use stdout = new StringWriter()
      use stderr = new StringWriter()

      let manager = PluginManager.Create(stdout, stderr)
      let runnablePlugin = createTestPlugin "runnable" true

      let nonRunnablePlugin = {
        createTestPlugin "non-runnable" false with
            transform = ValueNone
      }

      manager.AddPlugin(runnablePlugin) |> ignore
      manager.AddPlugin(nonRunnablePlugin) |> ignore

      let runnables = manager.GetRunnablePlugins([ "runnable"; "non-runnable" ])

      Assert.Equal(1, runnables.Length)
      Assert.Equal("runnable", runnables.[0].plugin.name)
    }

  [<Fact>]
  let ``LoadFromCode should be equivalent to AddPlugin``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let plugin = createTestPlugin "test-plugin" true

    let result = manager.LoadFromCode(plugin)

    match result with
    | Ok() -> Assert.True(manager.HasPlugin("test-plugin"))
    | Error err -> Assert.Fail($"Expected success, but got {err}")
  }

  [<Fact>]
  let ``HasPluginsForExtension should return true if plugins exist for extension``
    ()
    =
    taskUnit {
      use stdout = new StringWriter()
      use stderr = new StringWriter()

      let manager = PluginManager.Create(stdout, stderr)
      let plugin = createTestPlugin "test-plugin" true // shouldProcessFile checks for ".test"

      manager.AddPlugin(plugin) |> ignore

      let hasTestPlugins = manager.HasPluginsForExtension(".test")
      let hasOtherPlugins = manager.HasPluginsForExtension(".other")

      Assert.True(hasTestPlugins)
      Assert.False(hasOtherPlugins)
    }

  [<Fact>]
  let ``RunPlugins should execute plugins in order``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let plugin1 = createTestPlugin "plugin1" true
    let plugin2 = createTestPlugin "plugin2" true

    manager.AddPlugin(plugin1) |> ignore
    manager.AddPlugin(plugin2) |> ignore

    let inputFile = {
      extension = ".test"
      content = "original"
    }

    let! result = manager.RunPlugins ([ "plugin1"; "plugin2" ]) inputFile

    // Plugins should be applied in order: plugin1 then plugin2
    Assert.Equal("[plugin2] [plugin1] original", result.content)
  }

  [<Fact>]
  let ``CreateSession should create a new FSI session``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    let result = manager.CreateSession("test-session")

    match result with
    | Ok session -> Assert.NotNull(session)
    | Error err -> Assert.Fail($"Expected success, but got {err}")
  }

  [<Fact>]
  let ``CreateSession should return error if session already exists``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    let result1 = manager.CreateSession("test-session")
    let result2 = manager.CreateSession("test-session")

    match result1, result2 with
    | Ok _, Error SessionExists -> Assert.True(true)
    | _, _ ->
      Assert.Fail(
        $"Expected Ok() then SessionExists error, but got {result1}, {result2}"
      )
  }

  [<Fact>]
  let ``GetSession should return session if it exists``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    manager.CreateSession("test-session") |> ignore
    let retrieved = manager.GetSession("test-session")

    match retrieved with
    | Some session -> Assert.NotNull(session)
    | None -> Assert.Fail("Expected to find session")
  }

  [<Fact>]
  let ``GetSession should return None if session doesn't exist``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let retrieved = manager.GetSession("non-existent")

    match retrieved with
    | None -> Assert.True(true)
    | Some _ -> Assert.Fail("Expected None for non-existent session")
  }

  [<Fact>]
  let ``HasSession should return true if session exists``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    manager.CreateSession("test-session") |> ignore
    let exists = manager.HasSession("test-session")

    Assert.True(exists)
  }

  [<Fact>]
  let ``HasSession should return false if session doesn't exist``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let exists = manager.HasSession("non-existent")

    Assert.False(exists)
  }

  [<Fact>]
  let ``RemoveSession should remove existing session``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    manager.CreateSession("test-session") |> ignore
    let removed = manager.RemoveSession("test-session")
    let exists = manager.HasSession("test-session")

    Assert.True(removed)
    Assert.False(exists)
  }

  [<Fact>]
  let ``RemoveSession should return false for non-existent session``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let removed = manager.RemoveSession("non-existent")

    Assert.False(removed)
  }

  [<Fact>]
  let ``LoadFromText should return error if session already exists``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let script = "let x = 42"

    manager.CreateSession("script-session") |> ignore

    let result = manager.LoadFromText("script-session", script)

    match result with
    | Error SessionExists -> Assert.True(true)
    | _ -> Assert.Fail($"Expected SessionExists error, but got {result}")
  }

  [<Fact>]
  let ``LoadFromText should return error for invalid script``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let invalidScript = "let x = = = invalid syntax"

    let result = manager.LoadFromText("script-session", invalidScript)

    match result with
    | Error(EvaluationFailed _) -> Assert.True(true)
    | Error BoundValueMissing -> Assert.True(true) // Also acceptable since invalid syntax might not define Plugin
    | _ ->
      Assert.Fail(
        $"Expected EvaluationFailed or BoundValueMissing error, but got {result}"
      )
  }

  [<Fact>]
  let ``LoadFromText should return error if no Plugin is defined``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)
    let scriptWithoutPlugin = "let x = 42"

    let result = manager.LoadFromText("script-session", scriptWithoutPlugin)

    match result with
    | Error BoundValueMissing -> Assert.True(true)
    | _ -> Assert.Fail($"Expected BoundValueMissing error, but got {result}")
  }

  [<Fact>]
  let ``Integration test - basic workflow without scripts``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()

    let manager = PluginManager.Create(stdout, stderr)

    // Add regular plugins
    let plugin1 = createTestPlugin "plugin1" true
    let plugin2 = createTestPlugin "plugin2" true
    manager.AddPlugin(plugin1) |> ignore
    manager.AddPlugin(plugin2) |> ignore

    // Verify both plugins are available
    Assert.True(manager.HasPlugin("plugin1"))
    Assert.True(manager.HasPlugin("plugin2"))
    Assert.Equal(2, manager.GetAllPlugins().Length)

    // Run plugins in sequence
    let inputFile = {
      extension = ".test"
      content = "input"
    }

    let! result = manager.RunPlugins ([ "plugin1"; "plugin2" ]) inputFile

    // Both plugins should have processed the file
    Assert.Equal("[plugin2] [plugin1] input", result.content)

    // Verify extension detection
    Assert.True(manager.HasPluginsForExtension(".test"))
    Assert.False(manager.HasPluginsForExtension(".other"))
  }

module PluginBuilderTests =
  open System.IO

  [<Fact>]
  let ``Plugin builder should create a plugin with transform function``() = taskUnit {
    let testPlugin = plugin "json-to-text" {
      should_process_file(fun ext -> ext = ".json")

      with_transform(fun file -> {
        file with
            content = "transformed"
            extension = ".txt"
      })
    }

    Assert.Equal("json-to-text", testPlugin.name)

    match testPlugin.shouldProcessFile with
    | ValueSome shouldProcess ->
      Assert.True(shouldProcess ".json")
      Assert.False(shouldProcess ".txt")
    | ValueNone -> Assert.Fail("Expected shouldProcessFile to be Some")

    match testPlugin.transform with
    | ValueSome transform ->
      let inputFile = {
        content = "original"
        extension = ".json"
      }

      let! result = transform inputFile
      Assert.Equal("transformed", result.content)
      Assert.Equal(".txt", result.extension)
    | ValueNone -> Assert.Fail("Expected transform to be Some")
  }

  [<Fact>]
  let ``Plugin builder should work with async transform``() = taskUnit {
    let testPlugin = plugin "async-transform" {
      should_process_file(fun ext -> ext = ".md")

      with_transform(fun file -> task {
        do! Task.Delay(10) // Simulate async work

        return {
          file with
              content = $"# {file.content}"
        }
      })
    }

    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    let! addResult = task {
      let result = manager.AddPlugin(testPlugin)
      return result
    }

    match addResult with
    | Ok() -> Assert.True(true)
    | Error err -> Assert.Fail($"Expected success, but got {err}")

    let inputFile = {
      content = "Hello World"
      extension = ".md"
    }

    let! result = manager.RunPlugins [ "async-transform" ] inputFile

    Assert.Equal("# Hello World", result.content)
  }

  [<Fact>]
  let ``Plugin builder should work with ValueTask transform``() = taskUnit {
    let testPlugin = plugin "valuetask-transform" {
      should_process_file(fun ext -> ext = ".css")

      with_transform(fun file -> vTask {
        return {
          file with
              content = $"/* Processed */\n{file.content}"
        }
      })
    }

    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    manager.AddPlugin(testPlugin) |> ignore

    let inputFile = {
      content = "body { color: red; }"
      extension = ".css"
    }

    let! result = manager.RunPlugins [ "valuetask-transform" ] inputFile

    Assert.Equal("/* Processed */\nbody { color: red; }", result.content)
  }

  [<Fact>]
  let ``Plugin builder should work with Async transform``() = taskUnit {
    let testPlugin = plugin "async-fs-transform" {
      should_process_file(fun ext -> ext = ".js")

      with_transform(fun file -> async {
        do! Async.Sleep(5) // Simulate async work

        return {
          file with
              content = $"// Auto-generated\n{file.content}"
        }
      })
    }

    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    manager.AddPlugin(testPlugin) |> ignore

    let inputFile = {
      content = "console.log('hello');"
      extension = ".js"
    }

    let! result = manager.RunPlugins [ "async-fs-transform" ] inputFile

    Assert.Equal("// Auto-generated\nconsole.log('hello');", result.content)
  }

  [<Fact>]
  let ``Plugin builder should create plugin without shouldProcessFile``() = taskUnit {
    let testPlugin = plugin "always-transform" {
      with_transform(fun file -> {
        file with
            content = $"PROCESSED: {file.content}"
      })
    }

    Assert.Equal("always-transform", testPlugin.name)

    match testPlugin.shouldProcessFile with
    | ValueNone -> Assert.True(true) // Expected
    | ValueSome _ -> Assert.Fail("Expected shouldProcessFile to be None")

    match testPlugin.transform with
    | ValueSome transform ->
      let inputFile = { content = "test"; extension = ".any" }
      let! result = transform inputFile
      Assert.Equal("PROCESSED: test", result.content)
    | ValueNone -> Assert.Fail("Expected transform to be Some")
  }

  [<Fact>]
  let ``Plugin builder should create plugin without transform``() = taskUnit {
    let testPlugin = plugin "filter-only" {
      should_process_file(fun ext -> ext = ".special")
    }

    Assert.Equal("filter-only", testPlugin.name)

    match testPlugin.shouldProcessFile with
    | ValueSome shouldProcess ->
      Assert.True(shouldProcess ".special")
      Assert.False(shouldProcess ".other")
    | ValueNone -> Assert.Fail("Expected shouldProcessFile to be Some")

    match testPlugin.transform with
    | ValueNone -> Assert.True(true) // Expected
    | ValueSome _ -> Assert.Fail("Expected transform to be None")
  }

module PluginExecutionTests =
  open System.IO

  [<Fact>]
  let ``Complex plugin chain should execute in correct order``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    // Create a chain of plugins that each add a prefix
    let plugin1 = plugin "prefix-a" {
      should_process_file(fun ext -> ext = ".txt")

      with_transform(fun file -> {
        file with
            content = $"A[{file.content}]"
      })
    }

    let plugin2 = plugin "prefix-b" {
      should_process_file(fun ext -> ext = ".txt")

      with_transform(fun file -> {
        file with
            content = $"B[{file.content}]"
      })
    }

    let plugin3 = plugin "prefix-c" {
      should_process_file(fun ext -> ext = ".txt")

      with_transform(fun file -> {
        file with
            content = $"C[{file.content}]"
      })
    }

    // Add plugins to manager
    manager.AddPlugin(plugin1) |> ignore
    manager.AddPlugin(plugin2) |> ignore
    manager.AddPlugin(plugin3) |> ignore

    let inputFile = {
      content = "original"
      extension = ".txt"
    }

    let! result =
      manager.RunPlugins [ "prefix-a"; "prefix-b"; "prefix-c" ] inputFile

    // Should be applied in order: A, then B, then C
    Assert.Equal("C[B[A[original]]]", result.content)
  }

  [<Fact>]
  let ``Plugin chain should skip plugins that don't match extension``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    let cssPlugin = plugin "css-processor" {
      should_process_file(fun ext -> ext = ".css")

      with_transform(fun file -> {
        file with
            content = $"/* CSS */\n{file.content}"
      })
    }

    let jsPlugin = plugin "js-processor" {
      should_process_file(fun ext -> ext = ".js")

      with_transform(fun file -> {
        file with
            content = $"// JS\n{file.content}"
      })
    }

    let htmlPlugin = plugin "html-processor" {
      should_process_file(fun ext -> ext = ".html")

      with_transform(fun file -> {
        file with
            content = $"<!-- HTML -->\n{file.content}"
      })
    }

    manager.AddPlugin(cssPlugin) |> ignore
    manager.AddPlugin(jsPlugin) |> ignore
    manager.AddPlugin(htmlPlugin) |> ignore

    // Test with .js file - only js-processor should run
    let jsFile = {
      content = "console.log('test');"
      extension = ".js"
    }

    let! jsResult =
      manager.RunPlugins
        [ "css-processor"; "js-processor"; "html-processor" ]
        jsFile

    Assert.Equal("// JS\nconsole.log('test');", jsResult.content)

    // Test with .css file - only css-processor should run
    let cssFile = {
      content = "body { color: red; }"
      extension = ".css"
    }

    let! cssResult =
      manager.RunPlugins
        [ "css-processor"; "js-processor"; "html-processor" ]
        cssFile

    Assert.Equal("/* CSS */\nbody { color: red; }", cssResult.content)
  }

  [<Fact>]
  let ``Plugin should be able to change file extension``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    let markdownToHtml = plugin "markdown-to-html" {
      should_process_file(fun ext -> ext = ".md")

      with_transform(fun file -> {
        content = $"<h1>{file.content}</h1>"
        extension = ".html"
      })
    }

    let htmlMinifier = plugin "html-minifier" {
      should_process_file(fun ext -> ext = ".html")

      with_transform(fun file -> {
        file with
            content = file.content.Replace("\n", "").Replace("  ", " ")
      })
    }

    manager.AddPlugin(markdownToHtml) |> ignore
    manager.AddPlugin(htmlMinifier) |> ignore

    let inputFile = {
      content = "Hello World"
      extension = ".md"
    }

    let! result =
      manager.RunPlugins [ "markdown-to-html"; "html-minifier" ] inputFile

    Assert.Equal("<h1>Hello World</h1>", result.content)
    Assert.Equal(".html", result.extension)
  }

  [<Fact>]
  let ``Plugin execution should handle empty plugin list``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    let inputFile = {
      content = "unchanged"
      extension = ".txt"
    }

    let! result = manager.RunPlugins [] inputFile

    Assert.Equal("unchanged", result.content)
    Assert.Equal(".txt", result.extension)
  }

  [<Fact>]
  let ``Plugin execution should handle non-existent plugins gracefully``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    let realPlugin = plugin "real-plugin" {
      should_process_file(fun ext -> ext = ".txt")

      with_transform(fun file -> {
        file with
            content = $"processed: {file.content}"
      })
    }

    manager.AddPlugin(realPlugin) |> ignore

    let inputFile = { content = "test"; extension = ".txt" }
    // Include a non-existent plugin in the list
    let! result =
      manager.RunPlugins
        [ "non-existent"; "real-plugin"; "another-missing" ]
        inputFile

    // Should only process with the real plugin
    Assert.Equal("processed: test", result.content)
  }

  [<Fact>]
  let ``Mixed sync and async plugins should work together``() = taskUnit {
    use stdout = new StringWriter()
    use stderr = new StringWriter()
    let manager = PluginManager.Create(stdout, stderr)

    let syncPlugin = plugin "sync-plugin" {
      should_process_file(fun ext -> ext = ".txt")

      with_transform(fun file -> {
        file with
            content = $"SYNC[{file.content}]"
      })
    }

    let asyncPlugin = plugin "async-plugin" {
      should_process_file(fun ext -> ext = ".txt")

      with_transform(fun file -> task {
        do! Task.Delay(1)

        return {
          file with
              content = $"ASYNC[{file.content}]"
        }
      })
    }

    let valueTaskPlugin = plugin "valuetask-plugin" {
      should_process_file(fun ext -> ext = ".txt")

      with_transform(fun file -> vTask {
        return {
          file with
              content = $"VTASK[{file.content}]"
        }
      })
    }

    manager.AddPlugin(syncPlugin) |> ignore
    manager.AddPlugin(asyncPlugin) |> ignore
    manager.AddPlugin(valueTaskPlugin) |> ignore

    let inputFile = {
      content = "original"
      extension = ".txt"
    }

    let! result =
      manager.RunPlugins
        [ "sync-plugin"; "async-plugin"; "valuetask-plugin" ]
        inputFile

    Assert.Equal("VTASK[ASYNC[SYNC[original]]]", result.content)
  }
