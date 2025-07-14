module Perla.Tests.Scaffolding

open System
open System.IO
open Microsoft.Extensions.Logging
open Xunit
open Perla
open Perla.Units
open Perla.Scaffolding
open Perla.Json
open Perla.Logger
open FSharp.UMX

// Test helpers
module TestHelpers =
  let createTempDir() =
    let tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
    Directory.CreateDirectory(tempPath) |> ignore
    UMX.tag<SystemPath> tempPath

  let createTempFile
    (path: string<SystemPath>)
    (filename: string)
    (content: string)
    =
    let fullPath = Path.Combine(UMX.untag path, filename)
    File.WriteAllText(fullPath, content)
    fullPath

  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.AddPerlaLogger() |> ignore)

    loggerFactory.CreateLogger("ScaffoldingTests")

  let createSampleTemplateConfig() = {
    name = "Sample Template"
    description = Some "A sample template for testing"
    author = Some "Test Author"
    license = Some "MIT"
    repositoryUrl = Some "https://github.com/test/sample"
    group = "web"
    templates = [
      {
        id = "basic"
        name = "Basic Template"
        shortname = "basic"
        description = Some "Basic web template"
        path = UMX.tag<SystemPath> "basic"
      }
      {
        id = "advanced"
        name = "Advanced Template"
        shortname = "adv"
        description = Some "Advanced web template"
        path = UMX.tag<SystemPath> "advanced"
      }
    ]
  }

[<Fact>]
let ``ScaffoldConfiguration literal should have correct value``() =
  Assert.Equal("TemplateConfiguration", Scaffolding.ScaffoldConfiguration)

[<Fact>]
let ``TemplateScriptKind should be a discriminated union with Template and Repository cases``
  ()
  =
  // Test that the discriminated union cases exist by examining the type
  let templateScriptKindType = typeof<TemplateScriptKind>
  Assert.True(templateScriptKindType.IsValueType)

  let cases =
    Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(templateScriptKindType)

  let templateCase = cases |> Array.tryFind(fun case -> case.Name = "Template")

  let repositoryCase =
    cases |> Array.tryFind(fun case -> case.Name = "Repository")

  Assert.True(templateCase.IsSome, "Template case should exist")
  Assert.True(repositoryCase.IsSome, "Repository case should exist")

// Integration test to verify the basic structure works
[<Fact>]
let ``TemplateServiceArgs should contain required dependencies``() =
  // Test that the type structure is correct
  let argsType = typeof<TemplateServiceArgs>
  let properties = argsType.GetProperties()

  let fsManagerProp =
    properties |> Array.tryFind(fun p -> p.Name = "PerlaFsManager")

  let databaseProp = properties |> Array.tryFind(fun p -> p.Name = "Database")

  Assert.True(fsManagerProp.IsSome, "Should have PerlaFsManager property")
  Assert.True(databaseProp.IsSome, "Should have Database property")

[<Fact>]
let ``Create function should accept TemplateServiceArgs``() =
  // This test verifies that the Create function signature exists and accepts the correct args
  // We can't easily test the actual implementation without complex mocking
  // but we can verify the function signature compiles
  let createFunction: TemplateServiceArgs -> TemplateService =
    Scaffolding.Create

  Assert.NotNull(createFunction)

// Test the constants and types that are exposed
[<Fact>]
let ``ScaffoldConfiguration should be accessible from module``() =
  let config = Scaffolding.ScaffoldConfiguration
  Assert.Equal("TemplateConfiguration", config)

[<Fact>]
let ``TemplateScriptKind cases should be properly structured``() =
  // Verify the discriminated union cases exist and have expected structure
  let scriptKindType = typeof<TemplateScriptKind>

  Assert.True(
    scriptKindType.IsValueType,
    "TemplateScriptKind should be a struct"
  )

  let cases =
    Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(scriptKindType)

  Assert.Equal(2, cases.Length)

  let caseNames = cases |> Array.map(_.Name) |> Set.ofArray
  Assert.True(caseNames.Contains("Template"))
  Assert.True(caseNames.Contains("Repository"))

[<Fact>]
let ``TemplateService interface should have required methods``() =
  let serviceType = typeof<TemplateService>
  Assert.True(serviceType.IsInterface, "TemplateService should be an interface")

  let methods = serviceType.GetMethods() |> Array.map(_.Name) |> Set.ofArray

  Assert.True(methods.Contains("ListRepositories"))
  Assert.True(methods.Contains("ListTemplateItems"))
  Assert.True(methods.Contains("Add"))
  Assert.True(methods.Contains("FindOne"))
  Assert.True(methods.Contains("FindTemplateItems"))
  Assert.True(methods.Contains("Update"))
  Assert.True(methods.Contains("Delete"))
  Assert.True(methods.Contains("GetTemplateScriptContent"))

[<Fact>]
let ``TemplateConfigItem from sample should have correct structure``() =
  let config = TestHelpers.createSampleTemplateConfig()

  Assert.Equal("Sample Template", config.name)
  Assert.Equal(Some "A sample template for testing", config.description)
  Assert.Equal(Some "Test Author", config.author)
  Assert.Equal(Some "MIT", config.license)
  Assert.Equal("web", config.group)
  Assert.Equal(2, config.templates |> Seq.length)

  let basicTemplate = config.templates |> Seq.find(fun t -> t.id = "basic")
  Assert.Equal("Basic Template", basicTemplate.name)
  Assert.Equal("basic", basicTemplate.shortname)
  Assert.Equal(Some "Basic web template", basicTemplate.description)

[<Fact>]
let ``DecodedTemplateConfigItem should have proper type structure``() =
  let templateConfigType = typeof<DecodedTemplateConfigItem>

  let properties =
    templateConfigType.GetProperties()
    |> Array.map(fun p -> p.Name)
    |> Set.ofArray

  Assert.True(properties.Contains("id"))
  Assert.True(properties.Contains("name"))
  Assert.True(properties.Contains("shortName"))
  Assert.True(properties.Contains("description"))
  Assert.True(properties.Contains("path"))
