module Perla.Tests.Library

open Microsoft.Extensions.Logging
open Xunit
open Perla // Ensure extensions are available
open Perla.Logger

// Test helpers
module TestHelpers =
  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.AddPerlaLogger() |> ignore)

    loggerFactory.CreateLogger("LibraryTests")

// Tests for parsing functions
[<Fact>]
let ``FullRepositoryName active pattern should parse valid repository names``
  ()
  =
  match Some "user/repo" with
  | FullRepositoryName(username, repository, branch) ->
    Assert.Equal("user", username)
    Assert.Equal("repo", repository)
    Assert.NotNull(branch)
  | _ -> Assert.True(false, "Should have parsed successfully")

  match Some "user/repo:dev" with
  | FullRepositoryName(username, repository, branch) ->
    Assert.Equal("user", username)
    Assert.Equal("repo", repository)
    Assert.Equal("dev", branch)
  | _ -> Assert.True(false, "Should have parsed successfully")

[<Fact>]
let ``FullRepositoryName active pattern should return ValueNone for invalid names``
  ()
  =
  let test v =
    match v with
    | FullRepositoryName _ -> Assert.True(false, "Should not match")
    | _ -> Assert.True(true)

  test(Some "invalid")
  test None
  test(Some "")

[<Fact>]
let ``TemplateAndChild active pattern should parse template names correctly``
  ()
  =
  match "user/template/child" with
  | TemplateAndChild(Some user, template, Some child) ->
    Assert.Equal("user", user)
    Assert.Equal("template", template)
    Assert.Equal("child", child)
  | _ -> Assert.True(false)

  match "template/child" with
  | TemplateAndChild(None, template, Some child) ->
    Assert.Equal("template", template)
    Assert.Equal("child", child)
  | _ -> Assert.True(false)

  match "template" with
  | TemplateAndChild(None, template, None) -> Assert.Equal("template", template)
  | _ -> Assert.True(false)

  match "complex/name/with/multiple/parts" with
  | TemplateAndChild _ -> Assert.True(false, "Should not match for >3 parts")
  | _ -> Assert.True(true)

[<Fact>]
let ``ScopedPackage pattern should match scoped packages``() =
  match "@scope/package" with
  | ScopedPackage name -> Assert.Equal("scope/package", name)
  | Package _ -> Assert.True(false, "Should match ScopedPackage")

  match "regular-package" with
  | Package name -> Assert.Equal("regular-package", name)
  | ScopedPackage _ -> Assert.True(false, "Should match Package")

[<Fact>]
let ``ParsedPackageName active pattern should handle various package name formats``
  ()
  =
  // Basic package
  match "solid-js" with
  | ParsedPackageName(basePkg1, fullImport1, version1) ->
    Assert.Equal("solid-js", basePkg1)
    Assert.Equal("solid-js", fullImport1)
    Assert.Equal(None, version1)
  | _ -> Assert.True(false)

  // Package with version
  match "solid-js@1.9.7" with
  | ParsedPackageName(basePkg2, fullImport2, version2) ->
    Assert.Equal("solid-js", basePkg2)
    Assert.Equal("solid-js", fullImport2)
    Assert.Equal(Some "1.9.7", version2)
  | _ -> Assert.True(false)

  // Deep import
  match "solid-js/web" with
  | ParsedPackageName(basePkg3, fullImport3, version3) ->
    Assert.Equal("solid-js", basePkg3)
    Assert.Equal("solid-js/web", fullImport3)
    Assert.Equal(None, version3)
  | _ -> Assert.True(false)

  // Deep import with version
  match "solid-js/web@1.9.7" with
  | ParsedPackageName(basePkg4, fullImport4, version4) ->
    Assert.Equal("solid-js", basePkg4)
    Assert.Equal("solid-js/web", fullImport4)
    Assert.Equal(Some "1.9.7", version4)
  | _ -> Assert.True(false)

  // Scoped package - the function doesn't strip version for scoped packages starting with @
  match "@scope/pkg@1.2.3" with
  | ParsedPackageName(basePkg5, fullImport5, version5) ->
    Assert.Equal("@scope/pkg@1.2.3", basePkg5)
    Assert.Equal("@scope/pkg@1.2.3", fullImport5)
    Assert.Equal(None, version5)
  | _ -> Assert.True(false)

  // Scoped package with deep import
  match "@scope/pkg/deep@1.2.3" with
  | ParsedPackageName(basePkg6, fullImport6, version6) ->
    Assert.Equal("@scope/pkg", basePkg6)
    Assert.Equal("@scope/pkg/deep@1.2.3", fullImport6)
    Assert.Equal(None, version6)
  | _ -> Assert.True(false)

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
  | Extensions.Log -> Assert.True(true)
  | _ -> Assert.True(false, "Should match Log")

  match "unknown" with
  | Extensions.Log -> Assert.True(true)
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

[<Fact>]
let ``InstallString active pattern should produce correct install strings``() =
  // Basic package, no version
  match ("solid-js", None) with
  | InstallString s -> Assert.Equal("solid-js", s)
  | _ -> Assert.True(false)

  // Package with version
  match ("solid-js", Some "1.9.7") with
  | InstallString s -> Assert.Equal("solid-js@1.9.7", s)
  | _ -> Assert.True(false)

  // Deep import, no version
  match ("solid-js/web", None) with
  | InstallString s -> Assert.Equal("solid-js/web", s)
  | _ -> Assert.True(false)

  // Deep import with version
  match ("solid-js/web", Some "1.9.7") with
  | InstallString s -> Assert.Equal("solid-js@1.9.7/web", s)
  | _ -> Assert.True(false)

  // Scoped package, no version
  match ("@scope/pkg", None) with
  | InstallString s -> Assert.Equal("@scope/pkg", s)
  | _ -> Assert.True(false)

  // Scoped package with version
  match ("@scope/pkg", Some "2.0.0") with
  | InstallString s -> Assert.Equal("@scope/pkg@2.0.0", s)
  | _ -> Assert.True(false)

  // Scoped package with deep import and version
  match ("@scope/pkg/deep", Some "2.0.0") with
  | InstallString s -> Assert.Equal("@scope/pkg@2.0.0/deep", s)
  | _ -> Assert.True(false)
