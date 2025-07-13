namespace Perla.Tests

open System
open Xunit
open FSharp.UMX

open Perla.Types
open Perla.Units
open Perla.VirtualFs
open Perla.SuaveService.LiveReload

open Suave.EventSource

module SuaveServiceTests =

  module LiveReloadTests =

    let createTestFileChangedEvent
      (changeType: ChangeKind)
      (serverPath: string)
      (name: string)
      : FileChangedEvent =
      {
        changeType = changeType
        serverPath = UMX.tag<ServerUrl> serverPath
        userPath = UMX.tag<UserPath> "/"
        name = UMX.tag<SystemPath> name
        path = UMX.tag<SystemPath> "/path/to/file"
        oldName = None
        oldPath = None
      }

    let createTestTextFile (content: string) (mimetype: string) : FileContent = {
      filename = "test.css"
      mimetype = mimetype
      content = content
      source = UMX.tag<SystemPath> "/test/test.css"
    }

    // Mock VFS that can be configured per test
    let createMockVfs(resolveFunc: string<ServerUrl> -> FileKind option) =
      { new VirtualFileSystem with
          member _.Resolve(path) = resolveFunc path
          member _.Load(_) = async { return () }

          member _.ToDisk(?location) = async {
            return UMX.tag<SystemPath> "/tmp"
          }

          member _.FileChanges = failwith "Not implemented for tests"
          member _.Dispose() = ()
      }

    [<Fact>]
    let ``createReloadMessage should create proper reload message``() =
      // Arrange
      let event = createTestFileChangedEvent Changed "/app.js" "app.js"

      // Act
      let message = createReloadMessage event

      // Assert
      Assert.NotNull(message)
      // The message should contain the event data
      let data = message.data
      Assert.Contains("app.js", data)
      Assert.Contains("name", data)

      // Should be reload type
      Assert.Equal("reload", message.``type``.Value)

    [<Fact>]
    let ``createHmrMessage should create proper HMR message for CSS``() =
      // Arrange
      let event = createTestFileChangedEvent Changed "/styles.css" "styles.css"
      let cssFile = createTestTextFile "body { color: red; }" "text/css"

      // Act
      let message = createHmrMessage event cssFile

      // Assert
      Assert.NotNull(message)

      // Should be replace-css type
      Assert.Equal("replace-css", message.``type``.Value)

      // The message should contain HMR data including content
      let data = message.data
      Assert.Contains("styles.css", data)
      Assert.Contains("body { color: red; }", data)
      Assert.Contains("content", data)
      Assert.Contains("localPath", data)

    [<Fact>]
    let ``createLiveReloadMessage should return HMR message for CSS files``() =
      // Arrange
      let event = createTestFileChangedEvent Changed "/styles.css" "styles.css"
      let cssFile = createTestTextFile "body { color: blue; }" "text/css"

      // Create a mock VFS that returns the CSS file
      let mockVfs =
        createMockVfs(fun path ->
          if UMX.untag path = "/styles.css" then
            Some(TextFile cssFile)
          else
            None)

      // Act
      let message = createLiveReloadMessage mockVfs event

      // Assert
      Assert.Equal("replace-css", message.``type``.Value)
      let data = message.data
      Assert.Contains("body { color: blue; }", data)

    [<Fact>]
    let ``createLiveReloadMessage should return reload message for non-CSS files``
      ()
      =
      // Arrange
      let event = createTestFileChangedEvent Changed "/app.js" "app.js"

      let jsFile =
        createTestTextFile "console.log('hello');" "application/javascript"

      // Create a mock VFS that returns the JS file
      let mockVfs =
        createMockVfs(fun path ->
          if UMX.untag path = "/app.js" then
            Some(TextFile jsFile)
          else
            None)

      // Act
      let message = createLiveReloadMessage mockVfs event

      // Assert
      Assert.Equal("reload", message.``type``.Value)

    [<Fact>]
    let ``createLiveReloadMessage should return reload message for created files``
      ()
      =
      // Arrange
      let event =
        createTestFileChangedEvent Created "/new-file.css" "new-file.css"

      // Create a mock VFS (doesn't matter what it returns for created files)
      let mockVfs = createMockVfs(fun _ -> None)

      // Act
      let message = createLiveReloadMessage mockVfs event

      // Assert
      Assert.Equal("reload", message.``type``.Value)

    [<Fact>]
    let ``createLiveReloadMessage should return reload message for deleted files``
      ()
      =
      // Arrange
      let event =
        createTestFileChangedEvent
          Deleted
          "/deleted-file.css"
          "deleted-file.css"

      // Create a mock VFS
      let mockVfs = createMockVfs(fun _ -> None)

      // Act
      let message = createLiveReloadMessage mockVfs event

      // Assert
      Assert.Equal("reload", message.``type``.Value)

    [<Fact>]
    let ``createLiveReloadMessage should return reload message for renamed files``
      ()
      =
      // Arrange
      let event =
        createTestFileChangedEvent
          Renamed
          "/renamed-file.css"
          "renamed-file.css"

      // Create a mock VFS
      let mockVfs = createMockVfs(fun _ -> None)

      // Act
      let message = createLiveReloadMessage mockVfs event

      // Assert
      Assert.Equal("reload", message.``type``.Value)

    [<Fact>]
    let ``createLiveReloadMessage should return reload message when file not found in VFS``
      ()
      =
      // Arrange
      let event =
        createTestFileChangedEvent
          Changed
          "/missing-file.css"
          "missing-file.css"

      // Create a mock VFS that returns None for all files
      let mockVfs = createMockVfs(fun _ -> None)

      // Act
      let message = createLiveReloadMessage mockVfs event

      // Assert
      Assert.Equal("reload", message.``type``.Value)
