namespace Medusa.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Microsoft.Extensions.Logging
open Medusa

module Logger =
  let lf =
    LoggerFactory.Create(fun builder ->
      builder
        .AddConsole()
        .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug)
      |> ignore)


[<TestClass>]
type ProviderTests() =

  let logger = Logger.lf.CreateLogger<ProviderTests>()

  [<TestMethod>]
  member this.``extractFromUri should extract package from JSPM URL``() =
    // Arrange
    let uri = Uri("https://ga.jspm.io/npm:react@18.2.0/index.js")

    // Act
    let result = Provider.extractFromUri uri

    // Assert
    match result with
    | Ok package -> Assert.AreEqual<string>("react@18.2.0", package)
    | Error _ -> Assert.Fail("Expected successful extraction")

  [<TestMethod>]
  member this.``extractFromUri should extract package from ESM.sh URL``() =
    // Arrange
    let uri = Uri("https://esm.sh/*react@18.2.0/index.js")

    // Act
    let result = Provider.extractFromUri uri

    // Assert
    match result with
    | Ok package -> Assert.AreEqual<string>("react@18.2.0", package)
    | Error _ -> Assert.Fail("Expected successful extraction")

  [<TestMethod>]
  member this.``extractFromUri should extract package from JSDelivr URL``() =
    // Arrange
    let uri = Uri("https://cdn.jsdelivr.net/npm/lodash@4.17.21/index.js")

    // Act
    let result = Provider.extractFromUri uri

    // Assert
    match result with
    | Ok package -> Assert.AreEqual<string>("lodash@4.17.21", package)
    | Error _ -> Assert.Fail("Expected successful extraction")

  [<TestMethod>]
  member this.``extractFromUri should extract package from Unpkg URL``() =
    // Arrange
    let uri = Uri("https://unpkg.com/vue@3.3.4/dist/vue.esm-browser.js")

    // Act
    let result = Provider.extractFromUri uri

    // Assert
    match result with
    | Ok package -> Assert.AreEqual<string>("vue@3.3.4", package)
    | Error _ -> Assert.Fail("Expected successful extraction")

  [<TestMethod>]
  member this.``extractFromUri should extract scoped package from JSPM URL``() =
    // Arrange
    let uri =
      Uri(
        "https://ga.jspm.io/npm:@lit/reactive-element@1.6.1/reactive-element.js"
      )

    // Act
    let result = Provider.extractFromUri uri

    // Assert
    match result with
    | Ok package ->
      Assert.AreEqual<string>("@lit/reactive-element@1.6.1", package)
    | Error _ -> Assert.Fail("Expected successful extraction")

  [<TestMethod>]
  member this.``extractFromUri should return error for unsupported host``() =
    // Arrange
    let uri = Uri("https://example.com/some-package@1.0.0/index.js")

    // Act
    let result = Provider.extractFromUri uri

    // Assert
    match result with
    | Ok _ -> Assert.Fail("Expected error for unsupported host")
    | Error(error: Provider.ExtractionError) ->
      Assert.AreEqual<string>("example.com", error.host)
      Assert.AreEqual<Uri>(uri, error.url)

  [<TestMethod>]
  member this.``extractFilePath should extract file path from JSPM URL``() =
    // Arrange
    let uri = Uri("https://ga.jspm.io/npm:react@18.2.0/index.js")

    // Act
    let result = Provider.extractFilePath logger uri

    // Assert
    Assert.AreEqual<string>("index.js", result)

  [<TestMethod>]
  member this.``extractFilePath should extract nested file path from JSDelivr URL``
    ()
    =
    // Arrange
    let uri = Uri("https://cdn.jsdelivr.net/npm/lodash@4.17.21/fp/add.js")

    // Act
    let result = Provider.extractFilePath logger uri

    // Assert
    Assert.AreEqual<string>("fp/add.js", result)

  [<TestMethod>]
  member this.``extractFilePath should extract file path from scoped package``
    ()
    =
    // Arrange
    let uri = Uri("https://ga.jspm.io/npm:@babel/core@7.22.0/lib/index.js")

    // Act
    let result = Provider.extractFilePath logger uri

    // Assert
    Assert.AreEqual<string>("lib/index.js", result)

  [<TestMethod>]
  member this.``extractFilePath should return original URL for unsupported provider``
    ()
    =
    // Arrange
    let uri = Uri("https://example.com/some-package@1.0.0/index.js")

    // Act
    let result = Provider.extractFilePath logger uri

    // Assert
    Assert.AreEqual<string>(uri.ToString(), result)
