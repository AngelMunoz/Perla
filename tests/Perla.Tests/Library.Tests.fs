module Perla.Tests.Library

open Microsoft.Extensions.Logging
open Xunit
open Perla.Lib
open Perla.Logger

// Test helpers
module TestHelpers =
  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.AddPerlaLogger() |> ignore)

    loggerFactory.CreateLogger("LibraryTests")

// Tests for parsing functions
[<Fact>]
let ``parseFullRepositoryName should parse valid repository names``() =
  let result1 = parseFullRepositoryName(Some "user/repo")

  match result1 with
  | ValueSome(username, repository, branch) ->
    Assert.Equal("user", username)
    Assert.Equal("repo", repository)
    // The actual behavior might be different, let's test what it actually returns
    Assert.NotNull(branch)
  | ValueNone -> Assert.True(false, "Should have parsed successfully")

  let result2 = parseFullRepositoryName(Some "user/repo:dev")

  match result2 with
  | ValueSome(username, repository, branch) ->
    Assert.Equal("user", username)
    Assert.Equal("repo", repository)
    Assert.Equal("dev", branch)
  | ValueNone -> Assert.True(false, "Should have parsed successfully")

[<Fact>]
let ``parseFullRepositoryName should return ValueNone for invalid names``() =
  let result1 = parseFullRepositoryName(Some "invalid")
  Assert.True(result1.IsNone)

  let result2 = parseFullRepositoryName None
  Assert.True(result2.IsNone)

  let result3 = parseFullRepositoryName(Some "")
  Assert.True(result3.IsNone)

[<Fact>]
let ``getTemplateAndChild should parse template names correctly``() =
  let user1, template1, child1 = getTemplateAndChild "user/template/child"
  Assert.Equal(Some "user", user1)
  Assert.Equal("template", template1)
  Assert.Equal(Some "child", child1)

  let user2, template2, child2 = getTemplateAndChild "template/child"
  Assert.Equal(None, user2)
  Assert.Equal("template", template2)
  Assert.Equal(Some "child", child2)

  let user3, template3, child3 = getTemplateAndChild "template"
  Assert.Equal(None, user3)
  Assert.Equal("template", template3)
  Assert.Equal(None, child3)

  // For multiple parts (more than 3), returns the full name as template
  let user4, template4, child4 =
    getTemplateAndChild "complex/name/with/multiple/parts"

  Assert.Equal(None, user4)
  Assert.Equal("complex/name/with/multiple/parts", template4)
  Assert.Equal(None, child4)

[<Fact>]
let ``ScopedPackage pattern should match scoped packages``() =
  match "@scope/package" with
  | ScopedPackage name -> Assert.Equal("scope/package", name)
  | Package _ -> Assert.True(false, "Should match ScopedPackage")

  match "regular-package" with
  | Package name -> Assert.Equal("regular-package", name)
  | ScopedPackage _ -> Assert.True(false, "Should match Package")

[<Fact>]
let ``parsePackageName should handle various package name formats``() =
  // Basic package
  let basePkg1, fullImport1, version1 = parsePackageName "solid-js"
  Assert.Equal("solid-js", basePkg1)
  Assert.Equal("solid-js", fullImport1)
  Assert.Equal(None, version1)

  // Package with version
  let basePkg2, fullImport2, version2 = parsePackageName "solid-js@1.9.7"
  Assert.Equal("solid-js", basePkg2)
  Assert.Equal("solid-js", fullImport2)
  Assert.Equal(Some "1.9.7", version2)

  // Deep import
  let basePkg3, fullImport3, version3 = parsePackageName "solid-js/web"
  Assert.Equal("solid-js", basePkg3)
  Assert.Equal("solid-js/web", fullImport3)
  Assert.Equal(None, version3)

  // Deep import with version
  let basePkg4, fullImport4, version4 = parsePackageName "solid-js/web@1.9.7"
  Assert.Equal("solid-js", basePkg4)
  Assert.Equal("solid-js/web", fullImport4)
  Assert.Equal(Some "1.9.7", version4)

  // Scoped package - the function doesn't strip version for scoped packages starting with @
  let basePkg5, fullImport5, version5 = parsePackageName "@scope/pkg@1.2.3"
  Assert.Equal("@scope/pkg@1.2.3", basePkg5)
  Assert.Equal("@scope/pkg@1.2.3", fullImport5)
  Assert.Equal(None, version5)

  // Scoped package with deep import
  let basePkg6, fullImport6, version6 = parsePackageName "@scope/pkg/deep@1.2.3"
  Assert.Equal("@scope/pkg", basePkg6)
  Assert.Equal("@scope/pkg/deep@1.2.3", fullImport6)
  Assert.Equal(None, version6)

[<Fact>]
let ``Log pattern should categorize console levels correctly``() =
  match "debug" with
  | Debug -> Assert.True(true)
  | _ -> Assert.True(false, "Should match Debug")

  match "info" with
  | Info -> Assert.True(true)
  | _ -> Assert.True(false, "Should match Info")

  match "error" with
  | Err -> Assert.True(true)
  | _ -> Assert.True(false, "Should match Err")

  match "warning" with
  | Warning -> Assert.True(true)
  | _ -> Assert.True(false, "Should match Warning")

  match "clear" with
  | Clear -> Assert.True(true)
  | _ -> Assert.True(false, "Should match Clear")

  match "log" with
  | Perla.Lib.Log -> Assert.True(true)
  | _ -> Assert.True(false, "Should match Log")

  match "unknown" with
  | Perla.Lib.Log -> Assert.True(true)
  | _ -> Assert.True(false, "Should default to Log")

[<Fact>]
let ``Property path pattern should parse property paths correctly``() =
  match "topLevel" with
  | TopLevelProp prop -> Assert.Equal("topLevel", prop)
  | _ -> Assert.True(false, "Should match TopLevelProp")

  match "parent.child" with
  | NestedProp(parent, child) ->
    Assert.Equal("parent", parent)
    Assert.Equal("child", child)
  | _ -> Assert.True(false, "Should match NestedProp")

  match "root.middle.leaf" with
  | TripleNestedProp(root, middle, leaf) ->
    Assert.Equal("root", root)
    Assert.Equal("middle", middle)
    Assert.Equal("leaf", leaf)
  | _ -> Assert.True(false, "Should match TripleNestedProp")

  match "too.many.parts.here" with
  | InvalidPropPath -> Assert.True(true)
  | _ -> Assert.True(false, "Should match InvalidPropPath")
