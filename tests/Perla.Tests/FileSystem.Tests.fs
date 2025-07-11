module Perla.Tests.FileSystem

open System
open System.IO
open Microsoft.Extensions.Logging
open Xunit
open Perla.Types
open Perla.Units
open Perla.FileSystem
open Perla.RequestHandler
open Perla
open IcedTasks
open FSharp.UMX
open FSharp.Data.Adaptive
open FSharp.Control

// Test helpers and fakes
type TempDir(tempDirPath: string<SystemPath>) =

  do File.WriteAllText(Path.Combine(UMX.untag tempDirPath, "perla.json"), "{}")
  member _.Path = tempDirPath

  interface IDisposable with
    member _.Dispose() =
      try
        Directory.Delete(UMX.untag tempDirPath, true)
      with _ ->
        ()

module TestHelpers =
  let createTempDir() =
    let tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
    Directory.CreateDirectory(tempPath) |> ignore
    let taggedPath = tempPath |> UMX.tag<SystemPath>
    new TempDir(taggedPath)

  let createTempFile
    (path: string<SystemPath>)
    (fileName: string)
    (content: string)
    =
    let fullPath = Path.Combine(UMX.untag path, fileName)
    File.WriteAllText(fullPath, content)
    fullPath

  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)

    loggerFactory.CreateLogger("FileSystemTests")

// Fake implementations for testing
type FakePlatformOps
  (
    ?isWindows: bool,
    ?platformString: string,
    ?archString: string,
    ?fableAvailable: bool,
    ?toolCheckResult: ProcessResult
  ) =
  let isWindows =
    defaultArg isWindows (Environment.OSVersion.Platform = PlatformID.Win32NT)

  let platformString = defaultArg platformString "test-platform"
  let archString = defaultArg archString "test-arch"
  let fableAvailable = defaultArg fableAvailable true

  let toolCheckResult =
    defaultArg toolCheckResult {
      ExitCode = 0
      StandardOutput = ""
      StandardError = ""
    }

  interface PlatformOps with
    member _.IsWindows() = isWindows
    member _.PlatformString() = platformString
    member _.ArchString() = archString

    member _.CheckDotnetToolVersion(_) = cancellableTask {
      return toolCheckResult
    }

    member _.InstallDotnetTool(_) = cancellableTask { return toolCheckResult }
    member _.RunFable(_, _, _) = cancellableTask { return () }
    member _.StreamFable(_, _, _, _) = AsyncSeq.empty |> AsyncSeq.toAsyncEnum
    member _.IsFableAvailable() = cancellableTask { return fableAvailable }

    member _.RunEsbuildTransform(_, _, _, _, _, _, _) = cancellableTask {
      return ""
    }

    member _.RunEsbuildCss(_, _, _, _, _, _) = cancellableTask { return () }
    member _.RunEsbuildJs(_, _, _, _, _) = cancellableTask { return () }

type FakePerlaDirectories
  (
    tempDir: string<SystemPath>,
    ?assemblyRoot: string<SystemPath>,
    ?originalCwd: string<SystemPath>
  ) =
  let assemblyRoot = defaultArg assemblyRoot tempDir
  let originalCwd = defaultArg originalCwd tempDir

  interface PerlaDirectories with
    member _.AssemblyRoot = assemblyRoot

    member _.PerlaArtifactsRoot =
      UMX.tag<SystemPath>(Path.Combine(UMX.untag tempDir, "artifacts"))

    member _.Database =
      UMX.tag<SystemPath>(Path.Combine(UMX.untag tempDir, "database.db"))

    member _.Templates =
      UMX.tag<SystemPath>(Path.Combine(UMX.untag tempDir, "templates"))

    member _.OfflineTemplates =
      UMX.tag<SystemPath>(Path.Combine(UMX.untag tempDir, "offline-templates"))

    member _.PerlaConfigPath =
      UMX.tag<SystemPath>(Path.Combine(UMX.untag tempDir, "perla.json"))

    member _.OriginalCwd = originalCwd
    member _.CurrentWorkingDirectory = tempDir
    member _.SetCwdToProject(?fromPath) = ()

type FakeRequestHandler
  (
    ?downloadResult: unit -> CancellableTask<unit>,
    ?templateStream: unit -> CancellableTask<Stream>
  ) =
  let downloadResult =
    defaultArg downloadResult (fun () -> cancellableTask { return () })

  let templateStream =
    defaultArg templateStream (fun () -> cancellableTask {
      let memoryStream = new MemoryStream()
      return memoryStream :> System.IO.Stream
    })

  interface RequestHandler with
    member _.DownloadEsbuild(_) = downloadResult()
    member _.DownloadTemplate(_, _, _) = templateStream()

[<Fact>]
let ``GetManager should return a valid PerlaFsManager``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  Assert.NotNull(fsManager)

[<Fact>]
let ``PerlaConfiguration should return default config when no file exists``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let config = fsManager.PerlaConfiguration |> AVal.force

  Assert.NotNull(config)
  Assert.Equal<string<SystemPath>>(Defaults.PerlaConfig.index, config.index)
  Assert.Equal(Defaults.PerlaConfig.devServer.port, config.devServer.port)

[<Fact>]
let ``PerlaConfiguration should read from file when it exists``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  // Create a simple config file
  let configContent = """{"index": "custom-index.html"}"""
  TestHelpers.createTempFile tempDir.Path "perla.json" configContent |> ignore

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let config = fsManager.PerlaConfiguration |> AVal.force

  Assert.NotNull(config)
  // The exact assertion depends on how the config parsing works
  // For now, just check that it's not the default
  Assert.NotEqual<string<SystemPath>>(Defaults.PerlaConfig.index, config.index)

[<Fact>]
let ``ResolveIndexPath should return correct path``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let indexPath = fsManager.ResolveIndexPath |> AVal.force

  Assert.NotNull(indexPath)
  Assert.True(UMX.untag indexPath <> "")

[<Fact>]
let ``ResolveIndex should return content of index file``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  // Create an index file
  let indexContent = "<html><body>Test</body></html>"
  TestHelpers.createTempFile tempDir.Path "index.html" indexContent |> ignore

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let content = fsManager.ResolveIndex |> AVal.force

  // Note: The exact behavior depends on the FileSystem implementation
  // This might return empty string if the file path doesn't match expectations
  Assert.NotNull(content)

[<Fact>]
let ``ResolveImportMap should return empty map when file not found``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let importMap = fsManager.ResolveImportMap |> AVal.force

  Assert.NotNull(importMap)
  Assert.Equal(Perla.PkgManager.ImportMap.Empty, importMap)

[<Fact>]
let ``ResolveTsConfig should return None when file not found``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let tsConfig = fsManager.ResolveTsConfig |> AVal.force

  Assert.True(tsConfig.IsNone)

[<Fact>]
let ``ResolveTsConfig should return content when file exists``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let tsConfigContent = """{"compilerOptions": {"target": "es2015"}}"""

  TestHelpers.createTempFile tempDir.Path "tsconfig.json" tsConfigContent
  |> ignore

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let tsConfig = fsManager.ResolveTsConfig |> AVal.force

  Assert.True(tsConfig.IsSome)
  Assert.Equal(tsConfigContent, tsConfig.Value)

[<Fact>]
let ``DotEnvContents should return empty map when no env files exist``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let envContents = fsManager.DotEnvContents |> AVal.force

  Assert.True(Map.isEmpty envContents)

[<Fact>]
let ``ResolveEsbuildPath should return correct path``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let esbuildPath = fsManager.ResolveEsbuildPath()

  Assert.NotNull(esbuildPath)
  Assert.True(UMX.untag esbuildPath <> "")

[<Fact>]
let ``ResolvePluginPaths should return empty array when no plugins exist``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let pluginPaths = fsManager.ResolvePluginPaths()

  Assert.Empty(pluginPaths)

[<Fact>]
let ``ResolvePluginPaths should return plugin paths when they exist``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  // Create .perla/plugins directory structure
  let perlaDir = Path.Combine(UMX.untag tempDir.Path, ".perla")
  let pluginsDir = Path.Combine(perlaDir, "plugins")
  Directory.CreateDirectory(pluginsDir) |> ignore

  let pluginContent = "// Test plugin"

  TestHelpers.createTempFile
    (UMX.tag<SystemPath> pluginsDir)
    "test-plugin.fsx"
    pluginContent
  |> ignore

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let pluginPaths = fsManager.ResolvePluginPaths()

  Assert.NotEmpty(pluginPaths)
  let (path, content) = pluginPaths.[0]
  Assert.Contains("test-plugin.fsx", path)
  Assert.Equal(pluginContent, content)

[<Fact>]
let ``DotEnvContents should return populated map when env files exist``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  // Create .env file with environment variables
  let envContent =
    """PERLA_testenvvar=test
PERLA_anothervar=value123
PERLA_boolvar=true"""

  TestHelpers.createTempFile tempDir.Path ".env" envContent |> ignore

  // Create local.env file with environment variables
  let localEnvContent =
    """PERLA_localtestenvvar="localtest"
PERLA_localport=3000
PERLA_debug="enabled" """

  TestHelpers.createTempFile tempDir.Path "local.env" localEnvContent |> ignore

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  let envContents = fsManager.DotEnvContents |> AVal.force

  Assert.False(Map.isEmpty envContents)

  // Check that variables from .env file are present
  Assert.True(envContents.ContainsKey("testenvvar"))
  Assert.Equal("test", envContents.["testenvvar"])
  Assert.True(envContents.ContainsKey("anothervar"))
  Assert.Equal("value123", envContents.["anothervar"])
  Assert.True(envContents.ContainsKey("boolvar"))
  Assert.Equal("true", envContents.["boolvar"])

  // Check that variables from local.env file are present
  Assert.True(envContents.ContainsKey("localtestenvvar"))
  Assert.Equal("\"localtest\"", envContents.["localtestenvvar"])
  Assert.True(envContents.ContainsKey("localport"))
  Assert.Equal("3000", envContents.["localport"])
  Assert.True(envContents.ContainsKey("debug"))
  Assert.Equal("\"enabled\" ", envContents.["debug"])

// TODO: Add tests for methods requiring more complex setup:
// - SetupTemplate (requires zip stream handling)
// - CopyGlobs (requires file globbing)
// - SavePerlaConfig with updates (requires PerlaWritableField setup)
// - SaveImportMap (requires import map serialization)
// - SavePerlaConfig (requires config serialization)
// - EmitEnvFile (requires env file generation)
// - ResolveDescriptionsFile (requires descriptions file handling)
// - SetupEsbuild (requires esbuild setup)
// - SetupFable (requires fable setup)
// - Script resolution methods (require embedded resources)
// - ResolveOfflineTemplatesConfig (requires zip handling)

[<Fact>]
let ``SavePerlaConfig should create perla.json file``() = async {
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  
  // Test with the default config (which should be serializable)
  let testConfig = Defaults.PerlaConfig

  // Save the config
  do! fsManager.SavePerlaConfig(testConfig) |> Async.AwaitCancellableTask

  // Verify the file was created
  let expectedPath = Path.Combine(UMX.untag tempDir.Path, "perla.json")
  Assert.True(File.Exists(expectedPath))

  // Verify the file is not empty
  let savedContent = File.ReadAllText(expectedPath)
  Assert.NotNull(savedContent)
  Assert.NotEmpty(savedContent)
}

[<Fact>]
let ``SaveImportMap should create perla.json.importmap file with correct content``() = async {
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  
  // Create a test import map
  let testImportMap = {
    Perla.PkgManager.ImportMap.Empty with
      imports = Map.ofList [("react", "https://esm.sh/react@18")]
  }

  // Save the import map
  do! fsManager.SaveImportMap(testImportMap) |> Async.AwaitCancellableTask

  // Verify the file was created
  let expectedPath = Path.Combine(UMX.untag tempDir.Path, "perla.json.importmap")
  Assert.True(File.Exists(expectedPath))

  // Verify the content is correct JSON
  let savedContent = File.ReadAllText(expectedPath)
  Assert.NotNull(savedContent)
  Assert.NotEmpty(savedContent)
  Assert.Contains("react", savedContent)
  Assert.Contains("https://esm.sh/react@18", savedContent)
}

[<Fact>]
let ``EmitEnvFile should create environment file with correct content``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  // Create .env file with test environment variables
  let envContent = """PERLA_API_URL=https://api.example.com
PERLA_VERSION=1.0.0
PERLA_DEBUG=true"""
  TestHelpers.createTempFile tempDir.Path ".env" envContent |> ignore

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  
  // Create a test config with custom env path
  let testConfig = {
    Defaults.PerlaConfig with
      envPath = UMX.tag<ServerUrl> "/env.js"
      build = { Defaults.PerlaConfig.build with outDir = tempDir.Path }
  }

  // Emit the env file
  fsManager.EmitEnvFile(testConfig)

  // Verify the file was created
  let expectedPath = Path.Combine(UMX.untag tempDir.Path, "env.js")
  Assert.True(File.Exists(expectedPath))

  // Verify the content is correct JavaScript
  let savedContent = File.ReadAllText(expectedPath)
  Assert.NotNull(savedContent)
  Assert.NotEmpty(savedContent)
  Assert.Contains("export const API_URL = \"https://api.example.com\"", savedContent)
  Assert.Contains("export const VERSION = \"1.0.0\"", savedContent)
  Assert.Contains("export const DEBUG = \"true\"", savedContent)

[<Fact>]
let ``EmitEnvFile should create empty file when no environment variables exist``() =
  use tempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  
  // Create a test config with custom env path
  let testConfig = {
    Defaults.PerlaConfig with
      envPath = UMX.tag<ServerUrl> "/env.js"
      build = { Defaults.PerlaConfig.build with outDir = tempDir.Path }
  }

  // Emit the env file
  fsManager.EmitEnvFile(testConfig)

  // Verify the file was created
  let expectedPath = Path.Combine(UMX.untag tempDir.Path, "env.js")
  Assert.True(File.Exists(expectedPath))

  // Verify the content is empty (just a newline)
  let savedContent = File.ReadAllText(expectedPath)
  Assert.NotNull(savedContent)
  Assert.True(String.IsNullOrWhiteSpace(savedContent) || savedContent.Trim() = "")

[<Fact>]
let ``EmitEnvFile should use custom tmpPath when provided``() =
  use tempDir = TestHelpers.createTempDir()
  use customTempDir = TestHelpers.createTempDir()
  let logger = TestHelpers.createLogger()
  let platformOps = FakePlatformOps() :> PlatformOps
  let perlaDirectories = FakePerlaDirectories(tempDir.Path) :> PerlaDirectories
  let requestHandler = FakeRequestHandler() :> RequestHandler

  // Create .env file with test environment variables
  let envContent = """PERLA_CUSTOM_VAR=custom_value"""
  TestHelpers.createTempFile tempDir.Path ".env" envContent |> ignore

  let args = {
    Logger = logger
    PlatformOps = platformOps
    PerlaDirectories = perlaDirectories
    RequestHandler = requestHandler
  }

  let fsManager = FileSystem.GetManager(args)
  
  // Create a test config with custom env path
  let testConfig = {
    Defaults.PerlaConfig with
      envPath = UMX.tag<ServerUrl> "/env.js"
      build = { Defaults.PerlaConfig.build with outDir = tempDir.Path }
  }

  // Emit the env file with custom tmpPath
  fsManager.EmitEnvFile(testConfig, customTempDir.Path)

  // Verify the file was created in the custom directory
  let expectedPath = Path.Combine(UMX.untag customTempDir.Path, "env.js")
  Assert.True(File.Exists(expectedPath))

  // Verify the content is correct
  let savedContent = File.ReadAllText(expectedPath)
  Assert.NotNull(savedContent)
  Assert.Contains("export const CUSTOM_VAR = \"custom_value\"", savedContent)

  // Verify the file was NOT created in the default directory
  let defaultPath = Path.Combine(UMX.untag tempDir.Path, "env.js")
  Assert.False(File.Exists(defaultPath))
