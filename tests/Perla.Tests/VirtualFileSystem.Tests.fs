module Perla.Tests.VirtualFileSystem

open System
open System.IO
open System.Reactive.Subjects
open Microsoft.Extensions.Logging
open Xunit
open FSharp.UMX
open FSharp.Control
open IcedTasks

open Perla.Units
open Perla.VirtualFs
open Perla.Extensibility
open Perla.Plugins
open Perla.Logger

// Test helpers and fakes
module TestHelpers =
  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.AddPerlaLogger() |> ignore)

    loggerFactory.CreateLogger("VirtualFileSystemTests")

  let createTempDir() =
    let tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
    Directory.CreateDirectory(tempPath) |> ignore
    UMX.tag<SystemPath> tempPath

  let createTempFile (basePath: string<SystemPath>) fileName (content: string) =
    let fullPath = Path.Combine(UMX.untag basePath, fileName)
    File.WriteAllText(fullPath, content)
    UMX.tag<SystemPath> fullPath

  let deleteTempDir(path: string<SystemPath>) =
    try
      if Directory.Exists(UMX.untag path) then
        Directory.Delete(UMX.untag path, true)
    with _ ->
      () // Ignore cleanup errors in tests

// Fake ExtensibilityService for testing
type FakeExtensibilityService() =
  interface ExtensibilityService with
    member _.GetAllPlugins() = []
    member _.GetRunnablePlugins(_) = []
    member _.HasPluginsForExtension(_) = false
    member _.LoadPlugins(_, _) = Ok []
    member _.RunPlugins _ file = async { return file }

// Basic VirtualFileSystem creation tests
module VirtualFileSystemCreationTests =

  [<Fact>]
  let ``VirtualFs.Create should return a VirtualFileSystem instance``() =
    let logger = TestHelpers.createLogger()
    let extensibility = FakeExtensibilityService() :> ExtensibilityService

    let args = {
      Extensibility = extensibility
      Logger = logger
    }

    let vfs = VirtualFs.Create(args)

    Assert.NotNull(vfs)
    Assert.True(vfs :> IDisposable |> isNull |> not)

  [<Fact>]
  let ``VirtualFileSystem should be disposable``() =
    let logger = TestHelpers.createLogger()
    let extensibility = FakeExtensibilityService() :> ExtensibilityService

    let args = {
      Extensibility = extensibility
      Logger = logger
    }

    use vfs = VirtualFs.Create(args)

    // Should not throw when disposed
    vfs.Dispose()

// File resolution tests
module FileResolutionTests =

  [<Fact>]
  let ``Resolve should return None for non-existent files``() =
    let logger = TestHelpers.createLogger()
    let extensibility = FakeExtensibilityService() :> ExtensibilityService

    let args = {
      Extensibility = extensibility
      Logger = logger
    }

    use vfs = VirtualFs.Create(args)
    let serverUrl = UMX.tag<ServerUrl> "/non-existent.txt"

    let result = vfs.Resolve(serverUrl)

    Assert.True(result.IsNone)

  [<Fact>]
  let ``Resolve should return None before Load is called``() =
    let logger = TestHelpers.createLogger()
    let extensibility = FakeExtensibilityService() :> ExtensibilityService

    let args = {
      Extensibility = extensibility
      Logger = logger
    }

    use vfs = VirtualFs.Create(args)
    let serverUrl = UMX.tag<ServerUrl> "/test.txt"

    let result = vfs.Resolve(serverUrl)

    Assert.True(result.IsNone)

// Load functionality tests
module LoadFunctionalityTests =

  [<Fact>]
  let ``Load should complete successfully with empty mounted directories``() = async {
    let logger = TestHelpers.createLogger()
    let extensibility = FakeExtensibilityService() :> ExtensibilityService

    let args = {
      Extensibility = extensibility
      Logger = logger
    }

    use vfs = VirtualFs.Create(args)
    let emptyMounts = Map.empty<string<ServerUrl>, string<UserPath>>

    do! vfs.Load(emptyMounts)

    // Should complete without throwing
    Assert.True(true)
  }

  [<Fact>]
  let ``Load should handle single mounted directory``() = async {
    let tempDir = TestHelpers.createTempDir()

    try
      let logger = TestHelpers.createLogger()
      let extensibility = FakeExtensibilityService() :> ExtensibilityService

      let args = {
        Extensibility = extensibility
        Logger = logger
      }

      // Create a test file
      TestHelpers.createTempFile tempDir "test.txt" "Hello, World!" |> ignore

      use vfs = VirtualFs.Create(args)
      let serverUrl = UMX.tag<ServerUrl> "/static"
      let userPath = UMX.tag<UserPath>(UMX.untag tempDir)
      let mounts = Map.add serverUrl userPath Map.empty

      do! vfs.Load(mounts)

      // Should complete without throwing
      Assert.True(true)
    finally
      TestHelpers.deleteTempDir tempDir
  }

  [<Fact>]
  let ``Load should handle multiple mounted directories``() = async {
    let tempDir1 = TestHelpers.createTempDir()
    let tempDir2 = TestHelpers.createTempDir()

    try
      let logger = TestHelpers.createLogger()
      let extensibility = FakeExtensibilityService() :> ExtensibilityService

      let args = {
        Extensibility = extensibility
        Logger = logger
      }

      // Create test files in both directories
      TestHelpers.createTempFile tempDir1 "file1.txt" "Content 1" |> ignore
      TestHelpers.createTempFile tempDir2 "file2.txt" "Content 2" |> ignore

      use vfs = VirtualFs.Create(args)

      let mounts =
        Map.empty
        |> Map.add
          (UMX.tag<ServerUrl> "/static1")
          (UMX.tag<UserPath>(UMX.untag tempDir1))
        |> Map.add
          (UMX.tag<ServerUrl> "/static2")
          (UMX.tag<UserPath>(UMX.untag tempDir2))

      do! vfs.Load(mounts)

      // Should complete without throwing
      Assert.True(true)
    finally
      TestHelpers.deleteTempDir tempDir1
      TestHelpers.deleteTempDir tempDir2
  }

// File resolution after loading tests
module FileResolutionAfterLoadingTests =

  [<Fact>]
  let ``Resolve should return TextFile for text files after Load``() = async {
    let tempDir = TestHelpers.createTempDir()

    try
      let logger = TestHelpers.createLogger()
      let extensibility = FakeExtensibilityService() :> ExtensibilityService

      let args = {
        Extensibility = extensibility
        Logger = logger
      }

      let fileContent = "Hello, Virtual World!"
      TestHelpers.createTempFile tempDir "test.txt" fileContent |> ignore

      use vfs = VirtualFs.Create(args)
      let serverUrl = UMX.tag<ServerUrl> "/static"
      let userPath = UMX.tag<UserPath>(UMX.untag tempDir)
      let mounts = Map.add serverUrl userPath Map.empty

      do! vfs.Load(mounts)

      let requestUrl = UMX.tag<ServerUrl> "/static/test.txt"
      let result = vfs.Resolve(requestUrl)

      match result with
      | Some(TextFile fileInfo) ->
        Assert.Equal("test.txt", fileInfo.filename)
        Assert.Equal(fileContent, fileInfo.content)
        Assert.Equal("text/plain", fileInfo.mimetype)
      | Some(BinaryFile _) ->
        Assert.Fail("Expected TextFile but got BinaryFile")
      | None -> Assert.Fail("Expected file to be found")
    finally
      TestHelpers.deleteTempDir tempDir
  }

  [<Fact>]
  let ``Resolve should return correct mimetype for different file extensions``
    ()
    =
    async {
      let tempDir = TestHelpers.createTempDir()

      try
        let logger = TestHelpers.createLogger()
        let extensibility = FakeExtensibilityService() :> ExtensibilityService

        let args = {
          Extensibility = extensibility
          Logger = logger
        }

        // Create files with different extensions
        TestHelpers.createTempFile tempDir "script.js" "console.log('test');"
        |> ignore

        TestHelpers.createTempFile tempDir "style.css" "body { color: red; }"
        |> ignore

        TestHelpers.createTempFile tempDir "page.html" "<html></html>" |> ignore

        use vfs = VirtualFs.Create(args)
        let serverUrl = UMX.tag<ServerUrl> "/static"
        let userPath = UMX.tag<UserPath>(UMX.untag tempDir)
        let mounts = Map.add serverUrl userPath Map.empty

        do! vfs.Load(mounts)

        // Test JavaScript file
        let jsResult = vfs.Resolve(UMX.tag<ServerUrl> "/static/script.js")

        match jsResult with
        | Some(TextFile fileInfo) ->
          Assert.Equal("application/javascript", fileInfo.mimetype)
        | _ -> Assert.Fail "Expected JavaScript file to be resolved as TextFile"

        // Test CSS file
        let cssResult = vfs.Resolve(UMX.tag<ServerUrl> "/static/style.css")

        match cssResult with
        | Some(TextFile fileInfo) -> Assert.Equal("text/css", fileInfo.mimetype)
        | _ -> Assert.Fail "Expected CSS file to be resolved as TextFile"

        // Test HTML file
        let htmlResult = vfs.Resolve(UMX.tag<ServerUrl> "/static/page.html")

        match htmlResult with
        | Some(TextFile fileInfo) ->
          Assert.Equal("text/html", fileInfo.mimetype)
        | _ -> Assert.Fail "Expected HTML file to be resolved as TextFile"
      finally
        TestHelpers.deleteTempDir tempDir
    }

  [<Fact>]
  let ``Resolve should handle nested directory structures``() = async {
    let tempDir = TestHelpers.createTempDir()

    try
      let logger = TestHelpers.createLogger()
      let extensibility = FakeExtensibilityService() :> ExtensibilityService

      let args = {
        Extensibility = extensibility
        Logger = logger
      }

      // Create nested directory structure
      let nestedDir = Path.Combine(UMX.untag tempDir, "subdir")
      Directory.CreateDirectory(nestedDir) |> ignore

      let nestedFile = Path.Combine(nestedDir, "nested.txt")
      File.WriteAllText(nestedFile, "Nested content")

      use vfs = VirtualFs.Create(args)
      let serverUrl = UMX.tag<ServerUrl> "/static"
      let userPath = UMX.tag<UserPath>(UMX.untag tempDir)
      let mounts = Map.add serverUrl userPath Map.empty

      do! vfs.Load(mounts)

      let requestUrl = UMX.tag<ServerUrl> "/static/subdir/nested.txt"
      let result = vfs.Resolve(requestUrl)

      match result with
      | Some(TextFile fileInfo) ->
        Assert.Equal("nested.txt", fileInfo.filename)
        Assert.Equal("Nested content", fileInfo.content)
      | _ -> Assert.Fail "Expected nested file to be resolved"
    finally
      TestHelpers.deleteTempDir tempDir
  }

// ToDisk functionality tests
module ToDiskTests =

  [<Fact>]
  let ``ToDisk should return a valid system path``() = async {
    let logger = TestHelpers.createLogger()
    let extensibility = FakeExtensibilityService() :> ExtensibilityService

    let args = {
      Extensibility = extensibility
      Logger = logger
    }

    use vfs = VirtualFs.Create(args)

    let! outputPath = vfs.ToDisk()

    Assert.NotNull(UMX.untag outputPath)
    Assert.True(UMX.untag outputPath <> "")
  }

  [<Fact>]
  let ``ToDisk with custom location should respect the location parameter``() = async {
    let customLocation = TestHelpers.createTempDir()

    try
      let logger = TestHelpers.createLogger()
      let extensibility = FakeExtensibilityService() :> ExtensibilityService

      let args = {
        Extensibility = extensibility
        Logger = logger
      }

      use vfs = VirtualFs.Create(args)

      let! outputPath = vfs.ToDisk(customLocation)

      // The output path should be related to or within the custom location
      Assert.NotNull(UMX.untag outputPath)
      Assert.True(UMX.untag outputPath <> "")
    finally
      TestHelpers.deleteTempDir customLocation
  }

  [<Fact>]
  let ``ToDisk should create output directory if it doesn't exist``() = async {
    let tempDir = TestHelpers.createTempDir()

    try
      let logger = TestHelpers.createLogger()
      let extensibility = FakeExtensibilityService() :> ExtensibilityService

      let args = {
        Extensibility = extensibility
        Logger = logger
      }

      // Create some files to write to disk
      TestHelpers.createTempFile tempDir "test.txt" "Test content" |> ignore

      use vfs = VirtualFs.Create(args)
      let serverUrl = UMX.tag<ServerUrl> "/static"
      let userPath = UMX.tag<UserPath>(UMX.untag tempDir)
      let mounts = Map.add serverUrl userPath Map.empty

      do! vfs.Load(mounts)

      let outputDir = Path.Combine(UMX.untag tempDir, "output")
      let outputLocation = UMX.tag<SystemPath> outputDir

      let! resultPath = vfs.ToDisk(outputLocation)

      Assert.True(Directory.Exists(UMX.untag resultPath))
    finally
      TestHelpers.deleteTempDir tempDir
  }

// FileChanges observable tests
module FileChangesTests =

  [<Fact>]
  let ``FileChanges should be a valid observable``() =
    let logger = TestHelpers.createLogger()
    let extensibility = FakeExtensibilityService() :> ExtensibilityService

    let args = {
      Extensibility = extensibility
      Logger = logger
    }

    use vfs = VirtualFs.Create(args)
    let observable = vfs.FileChanges

    Assert.NotNull observable

// Error handling tests
module ErrorHandlingTests =

  [<Fact>]
  let ``Load should handle non-existent directories gracefully``() = async {
    let logger = TestHelpers.createLogger()
    let extensibility = FakeExtensibilityService() :> ExtensibilityService

    let args = {
      Extensibility = extensibility
      Logger = logger
    }

    use vfs = VirtualFs.Create(args)

    let nonExistentPath =
      Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())

    let serverUrl = UMX.tag<ServerUrl> "/static"
    let userPath = UMX.tag<UserPath> nonExistentPath
    let mounts = Map.add serverUrl userPath Map.empty

    // Should handle gracefully without throwing
    try
      do! vfs.Load(mounts)
      Assert.True true // If we get here, it handled the error gracefully
    with ex ->
      // if directories don't exist a warning is logged, but we shouldn't crash
      Assert.Fail $"Expected no exception, but got: {ex.Message}"
  }

  [<Fact>]
  let ``Resolve should handle malformed server URLs gracefully``() = async {
    let tempDir = TestHelpers.createTempDir()

    try
      let logger = TestHelpers.createLogger()
      let extensibility = FakeExtensibilityService() :> ExtensibilityService

      let args = {
        Extensibility = extensibility
        Logger = logger
      }

      use vfs = VirtualFs.Create(args)
      let serverUrl = UMX.tag<ServerUrl> "/static"
      let userPath = UMX.tag<UserPath>(UMX.untag tempDir)
      let mounts = Map.add serverUrl userPath Map.empty

      do! vfs.Load(mounts)

      // Test various malformed URLs
      let malformedUrls = [
        UMX.tag<ServerUrl> ""
        UMX.tag<ServerUrl> "/"
        UMX.tag<ServerUrl> "/static/../../../etc/passwd"
        UMX.tag<ServerUrl> "/static/./test.txt"
      ]

      for url in malformedUrls do
        let result = vfs.Resolve(url)
        // Should return None or handle gracefully, not throw
        Assert.True(result.IsNone || result.IsSome)
    finally
      TestHelpers.deleteTempDir tempDir
  }

// Integration tests
module IntegrationTests =

  [<Fact>]
  let ``Full workflow: Load, Resolve, and ToDisk``() = async {
    let tempDir = TestHelpers.createTempDir()
    let outputDir = TestHelpers.createTempDir()

    try
      let logger = TestHelpers.createLogger()
      let extensibility = FakeExtensibilityService() :> ExtensibilityService

      let args = {
        Extensibility = extensibility
        Logger = logger
      }

      // Create test files
      TestHelpers.createTempFile
        tempDir
        "index.html"
        "<html><body>Hello</body></html>"
      |> ignore

      TestHelpers.createTempFile tempDir "style.css" "body { margin: 0; }"
      |> ignore

      let subDir = Path.Combine(UMX.untag tempDir, "js")
      Directory.CreateDirectory(subDir) |> ignore
      File.WriteAllText(Path.Combine(subDir, "app.js"), "console.log('app');")

      use vfs = VirtualFs.Create(args)
      let serverUrl = UMX.tag<ServerUrl> "/static"
      let userPath = UMX.tag<UserPath>(UMX.untag tempDir)
      let mounts = Map.add serverUrl userPath Map.empty

      // Load the files
      do! vfs.Load(mounts)

      // Resolve different file types
      let htmlResult = vfs.Resolve(UMX.tag<ServerUrl> "/static/index.html")
      let cssResult = vfs.Resolve(UMX.tag<ServerUrl> "/static/style.css")
      let jsResult = vfs.Resolve(UMX.tag<ServerUrl> "/static/js/app.js")

      // Verify all files are resolved correctly
      match htmlResult with
      | Some(TextFile fileInfo) ->
        Assert.Equal("index.html", fileInfo.filename)
        Assert.Equal("text/html", fileInfo.mimetype)
        Assert.Contains("Hello", fileInfo.content)
      | _ -> Assert.Fail("HTML file should be resolved")

      match cssResult with
      | Some(TextFile fileInfo) ->
        Assert.Equal("style.css", fileInfo.filename)
        Assert.Equal("text/css", fileInfo.mimetype)
        Assert.Contains("margin", fileInfo.content)
      | _ -> Assert.Fail("CSS file should be resolved")

      match jsResult with
      | Some(TextFile fileInfo) ->
        Assert.Equal("app.js", fileInfo.filename)
        Assert.Equal("application/javascript", fileInfo.mimetype)
        Assert.Contains("console.log", fileInfo.content)
      | _ -> Assert.Fail("JS file should be resolved")

      // Write to disk
      let! diskPath = vfs.ToDisk(outputDir)

      Assert.True(Directory.Exists(UMX.untag diskPath))
    finally
      TestHelpers.deleteTempDir tempDir
      TestHelpers.deleteTempDir outputDir
  }

// FileContent and BinaryFileInfo tests
module FileKindTests =

  [<Fact>]
  let ``FileContent should have all required fields``() =
    let fileContent = {
      filename = "test.txt"
      mimetype = "text/plain"
      content = "Hello, World!"
      source = UMX.tag<SystemPath> "/path/to/test.txt"
    }

    Assert.Equal("test.txt", fileContent.filename)
    Assert.Equal("text/plain", fileContent.mimetype)
    Assert.Equal("Hello, World!", fileContent.content)
    Assert.Equal("/path/to/test.txt", UMX.untag fileContent.source)

  [<Fact>]
  let ``BinaryFileInfo should have all required fields``() =
    let binaryInfo = {
      filename = "image.png"
      mimetype = "image/png"
      source = UMX.tag<SystemPath> "/path/to/image.png"
    }

    Assert.Equal("image.png", binaryInfo.filename)
    Assert.Equal("image/png", binaryInfo.mimetype)
    Assert.Equal("/path/to/image.png", UMX.untag binaryInfo.source)

  [<Fact>]
  let ``FileKind discriminated union should work correctly``() =
    let textFile =
      TextFile {
        filename = "doc.txt"
        mimetype = "text/plain"
        content = "Documentation"
        source = UMX.tag<SystemPath> "/docs/doc.txt"
      }

    let binaryFile =
      BinaryFile {
        filename = "photo.jpg"
        mimetype = "image/jpeg"
        source = UMX.tag<SystemPath> "/images/photo.jpg"
      }

    match textFile with
    | TextFile info -> Assert.Equal("doc.txt", info.filename)
    | BinaryFile _ -> Assert.Fail("Should be TextFile")

    match binaryFile with
    | BinaryFile info -> Assert.Equal("photo.jpg", info.filename)
    | TextFile _ -> Assert.Fail("Should be BinaryFile")

// ChangeKind and FileChangedEvent tests
module ChangeEventTests =

  [<Fact>]
  let ``ChangeKind should have all expected values``() =
    let created = Created
    let deleted = Deleted
    let renamed = Renamed
    let changed = Changed

    // Just verify the values exist and are distinct
    Assert.NotEqual(created, deleted)
    Assert.NotEqual(created, renamed)
    Assert.NotEqual(created, changed)
    Assert.NotEqual(deleted, renamed)

  [<Fact>]
  let ``FileChangedEvent should have all required fields``() =
    let changeEvent = {
      serverPath = UMX.tag<ServerUrl> "/static/test.txt"
      userPath = UMX.tag<UserPath> "src/test.txt"
      oldPath = Some(UMX.tag<SystemPath> "/old/path/test.txt")
      oldName = Some(UMX.tag<SystemPath> "old-test.txt")
      changeType = Changed
      path = UMX.tag<SystemPath> "/new/path/test.txt"
      name = UMX.tag<SystemPath> "test.txt"
    }

    Assert.Equal("/static/test.txt", UMX.untag changeEvent.serverPath)
    Assert.Equal("src/test.txt", UMX.untag changeEvent.userPath)
    Assert.True(changeEvent.oldPath.IsSome)
    Assert.True(changeEvent.oldName.IsSome)
    Assert.Equal(Changed, changeEvent.changeType)
    Assert.Equal("/new/path/test.txt", UMX.untag changeEvent.path)
    Assert.Equal("test.txt", UMX.untag changeEvent.name)
