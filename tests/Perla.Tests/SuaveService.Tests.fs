namespace Perla.Tests

open Xunit
open FSharp.UMX
open Microsoft.Extensions.Logging

open Perla.Units
open Perla.VirtualFs
open Perla.SuaveService.LiveReload
open Perla.SuaveService
open Perla.Logger

open Suave.EventSource

module SuaveServiceTests =

  // Test helpers
  module TestHelpers =
    let createLogger() =
      let loggerFactory =
        LoggerFactory.Create(fun builder -> builder.AddPerlaLogger() |> ignore)

      loggerFactory.CreateLogger("SuaveServiceTests")

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

    // Fake VFS for testing - follows project's testing patterns
    type FakeVirtualFileSystem
      (resolveFunc: string<ServerUrl> -> FileKind option) =
      interface VirtualFileSystem with
        member _.Resolve(path) = resolveFunc path
        member _.Load _ = async { return () }
        member _.ToDisk(?location) = async { return UMX.tag<SystemPath> "/tmp" }
        member _.FileChanges = failwith "Not implemented for tests"
        member _.Dispose() = ()

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
    let ``createHmrMessages should create proper HMR messages for CSS``() =
      // Arrange
      let event = createTestFileChangedEvent Changed "/styles.css" "styles.css"
      let cssFile = createTestTextFile "body { color: red; }" "text/css"

      // Act
      let messages = createHmrMessages event cssFile

      // Assert
      Assert.Equal(2, messages.Length)
      let styleMsg = messages |> List.find(fun m -> m.data.Contains("content"))
      let linkMsg = messages |> List.find(fun m -> m.data.Contains("href"))
      Assert.NotNull(styleMsg)
      Assert.NotNull(linkMsg)
      Assert.Equal("replace-css", styleMsg.``type``.Value)
      Assert.Equal("replace-css", linkMsg.``type``.Value)
      Assert.Contains("styles.css", styleMsg.data)
      Assert.Contains("body { color: red; }", styleMsg.data)
      Assert.Contains("styles.css", linkMsg.data)

    [<Fact>]
    let ``createLiveReloadMessages should return HMR messages for CSS files``
      ()
      =
      // Arrange
      let event = createTestFileChangedEvent Changed "/styles.css" "styles.css"
      let cssFile = createTestTextFile "body { color: blue; }" "text/css"

      // Create a fake VFS that returns the CSS file
      let fakeVfs =
        new FakeVirtualFileSystem(fun path ->
          if UMX.untag path = "/styles.css" then
            Some(TextFile cssFile)
          else
            None)
        :> VirtualFileSystem

      // Act
      let messages = createLiveReloadMessages fakeVfs event

      // Assert
      Assert.Equal(2, messages.Length)
      let styleMsg = messages |> List.find(fun m -> m.data.Contains("content"))
      let linkMsg = messages |> List.find(fun m -> m.data.Contains("href"))
      Assert.NotNull(styleMsg)
      Assert.NotNull(linkMsg)
      Assert.Equal("replace-css", styleMsg.``type``.Value)
      Assert.Equal("replace-css", linkMsg.``type``.Value)
      Assert.Contains("body { color: blue; }", styleMsg.data)

    [<Fact>]
    let ``createLiveReloadMessages should return reload message for non-CSS files``
      ()
      =
      // Arrange
      let event = createTestFileChangedEvent Changed "/app.js" "app.js"

      let jsFile =
        createTestTextFile "console.log('hello');" "application/javascript"

      // Create a fake VFS that returns the JS file
      let fakeVfs =
        new FakeVirtualFileSystem(fun path ->
          if UMX.untag path = "/app.js" then
            Some(TextFile jsFile)
          else
            None)
        :> VirtualFileSystem

      // Act
      let messages = createLiveReloadMessages fakeVfs event

      // Assert
      Assert.Single(messages) |> ignore
      Assert.Equal("reload", messages.Head.``type``.Value)

    [<Fact>]
    let ``createLiveReloadMessages should return reload message for created files``
      ()
      =
      // Arrange
      let event =
        createTestFileChangedEvent Created "/new-file.css" "new-file.css"

      let fakeVfs =
        new FakeVirtualFileSystem(fun _ -> None) :> VirtualFileSystem

      // Act
      let messages = createLiveReloadMessages fakeVfs event

      // Assert
      Assert.Single(messages) |> ignore
      Assert.Equal("reload", messages.Head.``type``.Value)

    [<Fact>]
    let ``createLiveReloadMessages should return reload message for deleted files``
      ()
      =
      // Arrange
      let event =
        createTestFileChangedEvent
          Deleted
          "/deleted-file.css"
          "deleted-file.css"

      let fakeVfs =
        new FakeVirtualFileSystem(fun _ -> None) :> VirtualFileSystem

      // Act
      let messages = createLiveReloadMessages fakeVfs event

      // Assert
      Assert.Single(messages) |> ignore
      Assert.Equal("reload", messages.Head.``type``.Value)

    [<Fact>]
    let ``createLiveReloadMessages should return reload message for renamed files``
      ()
      =
      // Arrange
      let event =
        createTestFileChangedEvent
          Renamed
          "/renamed-file.css"
          "renamed-file.css"

      let fakeVfs =
        new FakeVirtualFileSystem(fun _ -> None) :> VirtualFileSystem

      // Act
      let messages = createLiveReloadMessages fakeVfs event

      // Assert
      Assert.Single(messages) |> ignore
      Assert.Equal("reload", messages.Head.``type``.Value)

    [<Fact>]
    let ``createLiveReloadMessages should return reload message when file not found in VFS``
      ()
      =
      // Arrange
      let event =
        createTestFileChangedEvent
          Changed
          "/missing-file.css"
          "missing-file.css"

      let fakeVfs =
        new FakeVirtualFileSystem(fun _ -> None) :> VirtualFileSystem

      // Act
      let messages = createLiveReloadMessages fakeVfs event

      // Assert
      Assert.Single(messages) |> ignore
      Assert.Equal("reload", messages.Head.``type``.Value)

module MimeTypesTests =

  [<Fact>]
  let ``tryGetContentType returns correct MIME type for known extensions``() =
    Assert.Equal(Some "text/html", MimeTypes.tryGetContentType "index.html")
    Assert.Equal(Some "text/css", MimeTypes.tryGetContentType "styles.css")

    Assert.Equal(
      Some "application/javascript",
      MimeTypes.tryGetContentType "app.js"
    )

    Assert.Equal(
      Some "application/json",
      MimeTypes.tryGetContentType "data.json"
    )

    Assert.Equal(Some "image/png", MimeTypes.tryGetContentType "image.png")
    Assert.Equal(Some "image/jpeg", MimeTypes.tryGetContentType "photo.jpg")

  [<Fact>]
  let ``tryGetContentType returns None for unknown extensions``() =
    Assert.Equal(None, MimeTypes.tryGetContentType "unknown.xyz")
    Assert.Equal(None, MimeTypes.tryGetContentType "file.unknownext")

  [<Fact>]
  let ``getContentType returns content type for known extensions``() =
    Assert.Equal("text/html", MimeTypes.getContentType "index.html")
    Assert.Equal("text/css", MimeTypes.getContentType "styles.css")
    Assert.Equal("application/javascript", MimeTypes.getContentType "app.js")

  [<Fact>]
  let ``getContentType returns default for unknown extensions``() =
    let result = MimeTypes.getContentType "unknown.xyz"
    Assert.NotNull(result)

module VirtualFilesTests =

  [<Fact>]
  let ``processCssAsJs should wrap CSS in JS style injection``() =
    let css = "body { color: red; }"
    let url = "/styles.css"
    let js = VirtualFiles.processCssAsJs css url
    Assert.Contains("document.createElement('style')", js)
    Assert.Contains(css, js)
    Assert.Contains(url, js)

  [<Fact>]
  let ``processJsonAsJs should wrap JSON in JS export default``() =
    let json = "{\"foo\":42}"
    let js = VirtualFiles.processJsonAsJs json
    Assert.StartsWith("export default", js)
    Assert.Contains(json, js)

  [<Fact>]
  let ``determineFileProcessing should process CSS as JS``() =
    let css = "body { color: blue; }"
    let bytes = System.Text.Encoding.UTF8.GetBytes css

    let result =
      VirtualFiles.determineFileProcessing
        "text/css"
        VirtualFiles.RequestedAs.JS
        bytes
        "/styles.css"

    Assert.Equal("application/javascript", result.ContentType)
    let contentStr = System.Text.Encoding.UTF8.GetString result.Content
    Assert.Contains("document.createElement('style')", contentStr)
    Assert.True(result.ShouldProcess)

  [<Fact>]
  let ``determineFileProcessing should process JSON as JS``() =
    let json = "{\"foo\":42}"
    let bytes = System.Text.Encoding.UTF8.GetBytes json

    let result =
      VirtualFiles.determineFileProcessing
        "application/json"
        VirtualFiles.RequestedAs.JS
        bytes
        "/data.json"

    Assert.Equal("application/javascript", result.ContentType)
    let contentStr = System.Text.Encoding.UTF8.GetString result.Content
    Assert.Contains("export default", contentStr)
    Assert.True(result.ShouldProcess)

  [<Fact>]
  let ``determineFileProcessing should not process JS as JS if not CSS or JSON``
    ()
    =
    let js = "console.log('hi')"
    let bytes = System.Text.Encoding.UTF8.GetBytes js

    let result =
      VirtualFiles.determineFileProcessing
        "application/javascript"
        VirtualFiles.RequestedAs.JS
        bytes
        "/app.js"

    Assert.Equal("application/javascript", result.ContentType)
    Assert.Equivalent(bytes, result.Content)
    Assert.False(result.ShouldProcess)

  [<Fact>]
  let ``determineFileProcessing should not process for RequestedAs.Normal``() =
    let css = "body { color: green; }"
    let bytes = System.Text.Encoding.UTF8.GetBytes css

    let result =
      VirtualFiles.determineFileProcessing
        "text/css"
        VirtualFiles.RequestedAs.Normal
        bytes
        "/styles.css"

    Assert.Equal("text/css", result.ContentType)
    Assert.Equivalent(bytes, result.Content)
    Assert.False(result.ShouldProcess)
