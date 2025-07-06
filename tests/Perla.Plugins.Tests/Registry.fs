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

module Runnables =
  type RunnableContainer =
    static let plugins =
      lazy
        (Dictionary<string, PluginInfo>(
          [
            for p in pluginFactory 10 do
              KeyValuePair(p.name, p)
          ]
        ))

    static member PluginCache = plugins

  [<Fact>]
  let ``GetRunnables should return only those who have a "should process file" function``
    ()
    =
    let runnables =
      PluginRegistry.GetRunnablePlugins<RunnableContainer>(
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

  [<Fact>]
  let ``GetPluginList should bring all of the plugins in cache``() =
    let plugins = PluginRegistry.GetPluginList<RunnableContainer>()

    Assert.Equal(10, plugins.Length)

  [<Fact>]
  let ``LoadFromCode should give error if the plugin is already there``() =
    let plugin = PluginRegistry.GetPluginList<RunnableContainer>()[0]
    let result = PluginRegistry.LoadFromCode<RunnableContainer>(plugin)

    match result with
    | Error(PluginLoadError.AlreadyLoaded _) -> Assert.True(true)
    | Error err -> Assert.Fail $"Expected error, but got %A{err}"
    | Ok() -> Assert.Fail $"Expected error, but got %A{result}"

  [<Fact>]
  let ``LoadFromCode should be successful if the plugin is not in the cache``
    ()
    =
    let plugin = {
      name = "test11"
      shouldProcessFile = ValueNone
      transform = ValueNone
    }

    let result = PluginRegistry.LoadFromCode<RunnableContainer>(plugin)

    match result with
    | Ok() -> Assert.True true
    | Error result -> Assert.Fail $"Expected success, but got %A{result}"

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
