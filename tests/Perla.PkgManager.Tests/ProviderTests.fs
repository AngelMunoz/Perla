module Perla.PkgManager.Tests.ProviderTests

open System
open Xunit
open Microsoft.Extensions.Logging
open Serilog

open Perla.Logger
open Perla.PkgManager

module Logger =
  let lf =
    LoggerFactory.Create(fun builder ->
      builder.AddPerlaLogger().SetMinimumLevel(LogLevel.Debug) |> ignore)

let logger = Logger.lf.CreateLogger("ProviderTests")


[<Fact>]
let ``extractFromUri should extract package from JSPM URL``() =
  // Arrange
  let uri = Uri("https://ga.jspm.io/npm:react@18.2.0/index.js")

  // Act
  let result = ProviderOps.extractFromUri uri

  // Assert
  match result with
  | Ok package -> Assert.Equal<string>("react@18.2.0", package)
  | Error _ -> Assert.Fail("Expected successful extraction")

[<Fact>]
let ``extractFromUri should extract package from ESM.sh URL``() =
  // Arrange
  let uri = Uri("https://esm.sh/*react@18.2.0/index.js")

  // Act
  let result = ProviderOps.extractFromUri uri

  // Assert
  match result with
  | Ok package -> Assert.Equal<string>("react@18.2.0", package)
  | Error _ -> Assert.Fail("Expected successful extraction")

[<Fact>]
let ``extractFromUri should extract package from JSDelivr URL``() =
  // Arrange
  let uri = Uri("https://cdn.jsdelivr.net/npm/lodash@4.17.21/index.js")

  // Act
  let result = ProviderOps.extractFromUri uri

  // Assert
  match result with
  | Ok package -> Assert.Equal<string>("lodash@4.17.21", package)
  | Error _ -> Assert.Fail("Expected successful extraction")

[<Fact>]
let ``extractFromUri should extract package from Unpkg URL``() =
  // Arrange
  let uri = Uri("https://unpkg.com/vue@3.3.4/dist/vue.esm-browser.js")

  // Act
  let result = ProviderOps.extractFromUri uri

  // Assert
  match result with
  | Ok package -> Assert.Equal<string>("vue@3.3.4", package)
  | Error _ -> Assert.Fail("Expected successful extraction")

[<Fact>]
let ``extractFromUri should extract scoped package from JSPM URL``() =
  // Arrange
  let uri =
    Uri(
      "https://ga.jspm.io/npm:@lit/reactive-element@1.6.1/reactive-element.js"
    )

  // Act
  let result = ProviderOps.extractFromUri uri

  // Assert
  match result with
  | Ok package -> Assert.Equal<string>("@lit/reactive-element@1.6.1", package)
  | Error _ -> Assert.Fail("Expected successful extraction")

[<Fact>]
let ``extractFromUri should return error for unsupported host``() =
  // Arrange
  let uri = Uri("https://example.com/some-package@1.0.0/index.js")

  // Act
  let result = ProviderOps.extractFromUri uri

  // Assert
  match result with
  | Ok _ -> Assert.Fail("Expected error for unsupported host")
  | Error(error: ProviderOps.ExtractionError) ->
    Assert.Equal<string>("example.com", error.host)
    Assert.Equal<Uri>(uri, error.url)

[<Fact>]
let ``extractFilePath should extract file path from JSPM URL``() =
  // Arrange
  let uri = Uri("https://ga.jspm.io/npm:react@18.2.0/index.js")

  // Act
  let result = ProviderOps.extractFilePath logger uri

  // Assert
  Assert.Equal<string>("index.js", result)

[<Fact>]
let ``extractFilePath should extract nested file path from JSDelivr URL``() =
  // Arrange
  let uri = Uri("https://cdn.jsdelivr.net/npm/lodash@4.17.21/fp/add.js")

  // Act
  let result = ProviderOps.extractFilePath logger uri

  // Assert
  Assert.Equal<string>("fp/add.js", result)

[<Fact>]
let ``extractFilePath should extract file path from scoped package``() =
  // Arrange
  let uri = Uri("https://ga.jspm.io/npm:@babel/core@7.22.0/lib/index.js")

  // Act
  let result = ProviderOps.extractFilePath logger uri

  // Assert
  Assert.Equal<string>("lib/index.js", result)

[<Fact>]
let ``extractFilePath should return original URL for unsupported provider``() =
  // Arrange
  let uri = Uri("https://example.com/some-package@1.0.0/index.js")

  // Act
  let result = ProviderOps.extractFilePath logger uri

  // Assert
  Assert.Equal<string>(uri.ToString(), result)
