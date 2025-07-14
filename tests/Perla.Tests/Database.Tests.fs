module Perla.Tests.Database

open System
open System.IO
open Microsoft.Extensions.Logging
open Xunit
open LiteDB
open FSharp.UMX

open Perla.Types
open Perla.Units
open Perla.Json
open Perla.Database
open Perla
open Perla.Logger

// Test helpers
module TestHelpers =
  let createLogger() =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.AddPerlaLogger() |> ignore)

    loggerFactory.CreateLogger<PerlaDatabase>()

  let createTempDatabaseConnection() =
    let tempDbPath = Path.GetTempFileName()
    let mutable disposed = false

    let cleanup() =
      if not disposed then
        disposed <- true

        if File.Exists(tempDbPath) then
          try
            File.Delete(tempDbPath)
          with _ ->
            () // Ignore cleanup errors

    let getConnection() =
      if disposed then
        failwith "Database connection has been disposed"

      new LiteDatabase(tempDbPath) :> ILiteDatabase

    getConnection, cleanup

  let createInMemoryConnection() =
    let db, cleanup = createTempDatabaseConnection()
    db

  let createTempConnection() =
    fun () -> new LiteDatabase(":temp:") :> ILiteDatabase

  type FakePerlaDirectories(tempPath: string<SystemPath>) =
    interface PerlaDirectories with
      member _.AssemblyRoot = tempPath
      member _.PerlaArtifactsRoot = tempPath
      member _.Database = tempPath
      member _.Templates = tempPath
      member _.OfflineTemplates = tempPath
      member _.PerlaConfigPath = tempPath
      member _.OriginalCwd = tempPath
      member _.CurrentWorkingDirectory = tempPath
      member _.SetCwdToProject(?fromPath) = ()

  let createFakeDirectories() =
    let tempPath = Path.GetTempPath() |> UMX.tag<SystemPath>
    FakePerlaDirectories(tempPath) :> PerlaDirectories

  let createDatabaseArgs() =
    let getConnection, cleanup = createTempDatabaseConnection()

    let args = {
      Logger = createLogger()
      Directories = createFakeDirectories()
      GetConnection = getConnection
    }

    args, cleanup

  let createSampleTemplateConfig() = {
    name = "Sample Template"
    group = "web"
    templates = [|
      {
        id = "basic"
        name = "Basic Template"
        path = UMX.tag<SystemPath> "templates/basic"
        shortname = "basic"
        description = Some "A basic web template"
      }
      {
        id = "advanced"
        name = "Advanced Template"
        path = UMX.tag<SystemPath> "templates/advanced"
        shortname = "advanced"
        description = Some "An advanced web template"
      }
    |]
    author = Some "Test Author"
    license = Some "MIT"
    description = Some "Sample template configuration"
    repositoryUrl = Some "https://github.com/test/templates"
  }

// CheckRepository Tests
module CheckRepositoryTests =
  open TestHelpers

  [<Fact>]
  let ``IsSetupPresent should return false when no setup check exists``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    let result = database.Checks.IsSetupPresent()

    cleanup()
    Assert.False(result)

  [<Fact>]
  let ``SaveSetup should create and save setup check``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    let checkId = database.Checks.SaveSetup()

    Assert.NotEqual(ObjectId.Empty, checkId)
    Assert.True(database.Checks.IsSetupPresent())
    cleanup()

  [<Fact>]
  let ``SaveSetup should return existing check ID when called multiple times``
    ()
    =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    let firstId = database.Checks.SaveSetup()
    let secondId = database.Checks.SaveSetup()

    Assert.Equal(firstId, secondId)
    cleanup()

  [<Fact>]
  let ``IsEsbuildBinPresent should return false when no esbuild check exists``
    ()
    =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)
    let version = UMX.tag<Semver> "0.19.0"

    let result = database.Checks.IsEsbuildBinPresent(version)

    cleanup()
    Assert.False(result)

  [<Fact>]
  let ``SaveEsbuildBinPresent should create and save esbuild check``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)
    let version = UMX.tag<Semver> "0.19.0"

    let checkId = database.Checks.SaveEsbuildBinPresent(version)

    Assert.NotEqual(ObjectId.Empty, checkId)
    Assert.True(database.Checks.IsEsbuildBinPresent(version))
    cleanup()

  [<Fact>]
  let ``SaveEsbuildBinPresent should handle different versions independently``
    ()
    =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)
    let version1 = UMX.tag<Semver> "0.19.0"
    let version2 = UMX.tag<Semver> "0.20.0"

    let checkId1 = database.Checks.SaveEsbuildBinPresent(version1)
    let checkId2 = database.Checks.SaveEsbuildBinPresent(version2)

    Assert.NotEqual(checkId1, checkId2)
    Assert.True(database.Checks.IsEsbuildBinPresent(version1))
    Assert.True(database.Checks.IsEsbuildBinPresent(version2))
    cleanup()

  [<Fact>]
  let ``AreTemplatesPresent should return false when no templates check exists``
    ()
    =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    let result = database.Checks.AreTemplatesPresent()

    cleanup()
    Assert.False(result)

  [<Fact>]
  let ``SaveTemplatesPresent should create and save templates check``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    let checkId = database.Checks.SaveTemplatesPresent()

    Assert.NotEqual(ObjectId.Empty, checkId)
    Assert.True(database.Checks.AreTemplatesPresent())
    cleanup()

// TemplateRepository Tests
module TemplateRepositoryTests =
  open TestHelpers

  [<Fact>]
  let ``ListRepositories should return empty list when no repositories exist``
    ()
    =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    let repositories = database.Templates.ListRepositories()

    cleanup()
    Assert.Empty(repositories)

  [<Fact>]
  let ``ListTemplateItems should return empty list when no template items exist``
    ()
    =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    let templateItems = database.Templates.ListTemplateItems()

    cleanup()
    Assert.Empty(templateItems)

  [<Fact>]
  let ``Add should create repository and template items``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)
    let config = createSampleTemplateConfig()
    let path = UMX.tag<SystemPath> "/tmp/templates"
    let username = "testuser"
    let repository = UMX.tag<Repository> "test-repo"
    let branch = UMX.tag<Branch> "main"

    let repositoryId =
      database.Templates.Add(path, config, username, repository, branch)

    Assert.NotEqual(ObjectId.Empty, repositoryId)

    let repositories = database.Templates.ListRepositories()
    Assert.Single(repositories) |> ignore

    let savedRepo = repositories[0]
    Assert.Equal("testuser", savedRepo.Username)
    Assert.Equal("test-repo", savedRepo.Repository)
    Assert.Equal("main", savedRepo.Branch)
    Assert.Equal("Sample Template", savedRepo.Name)
    Assert.Equal("web", savedRepo.Group)

    let templateItems = database.Templates.ListTemplateItems()
    Assert.Equal(2, templateItems.Length)
    cleanup()

  [<Fact>]
  let ``FindOne should return repository when searching by ID``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)
    let config = createSampleTemplateConfig()
    let path = UMX.tag<SystemPath> "/tmp/templates"
    let username = "testuser"
    let repository = UMX.tag<Repository> "test-repo"
    let branch = UMX.tag<Branch> "main"

    let repositoryId =
      database.Templates.Add(path, config, username, repository, branch)

    let found = database.Templates.FindOne(TemplateSearchKind.Id repositoryId)

    match found with
    | Some repo ->
      Assert.Equal(repositoryId, repo._id)
      Assert.Equal("testuser", repo.Username)
    | None -> Assert.Fail("Repository should be found")

    cleanup()

  [<Fact>]
  let ``FindOne should return repository when searching by username``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)
    let config = createSampleTemplateConfig()
    let path = UMX.tag<SystemPath> "/tmp/templates"
    let username = "testuser"
    let repository = UMX.tag<Repository> "test-repo"
    let branch = UMX.tag<Branch> "main"

    database.Templates.Add(path, config, username, repository, branch) |> ignore

    let found =
      database.Templates.FindOne(TemplateSearchKind.Username "testuser")

    match found with
    | Some repo -> Assert.Equal("testuser", repo.Username)
    | None -> Assert.Fail("Repository should be found")

    cleanup()

  [<Fact>]
  let ``FindOne should return None when repository doesn't exist``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    let found =
      database.Templates.FindOne(TemplateSearchKind.Username "nonexistent")

    Assert.Null(found)
    cleanup()

  [<Fact>]
  let ``FindTemplateItems should return items when searching by name``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)
    let config = createSampleTemplateConfig()
    let path = UMX.tag<SystemPath> "/tmp/templates"
    let username = "testuser"
    let repository = UMX.tag<Repository> "test-repo"
    let branch = UMX.tag<Branch> "main"

    database.Templates.Add(path, config, username, repository, branch) |> ignore

    let items =
      database.Templates.FindTemplateItems(
        QuickAccessSearch.Name "Basic Template"
      )

    Assert.Single(items) |> ignore
    Assert.Equal("Basic Template", items[0].Name)
    cleanup()

// Integration Tests
module IntegrationTests =
  open TestHelpers

  [<Fact>]
  let ``Database Create should return valid PerlaDatabase instance``() =
    let args, cleanup = createDatabaseArgs()

    let database = Database.Create(args)

    Assert.NotNull(database)
    Assert.NotNull(database.Checks)
    Assert.NotNull(database.Templates)
    cleanup()

  [<Fact>]
  let ``Database should work with temporary disk connection``() =
    let args, cleanup = createDatabaseArgs()

    let database = Database.Create(args)

    Assert.NotNull(database)
    Assert.NotNull(database.Checks)
    Assert.NotNull(database.Templates)
    cleanup()

  [<Fact>]
  let ``Multiple database instances should be independent``() =
    let args1, cleanup1 = createDatabaseArgs()
    let args2, cleanup2 = createDatabaseArgs()

    let database1 = Database.Create(args1)
    let database2 = Database.Create(args2)

    Assert.NotNull(database1)
    Assert.NotNull(database2)
    Assert.NotSame(database1, database2)
    cleanup1()
    cleanup2()

  [<Fact>]
  let ``Full workflow integration test``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)

    // Test CheckRepository workflow
    Assert.False(database.Checks.IsSetupPresent())
    let setupId = database.Checks.SaveSetup()
    Assert.True(database.Checks.IsSetupPresent())

    // Test TemplateRepository workflow
    let config = createSampleTemplateConfig()
    let path = UMX.tag<SystemPath> "/integration/templates"
    let username = "integration"
    let repository = UMX.tag<Repository> "test"
    let branch = UMX.tag<Branch> "main"

    let repoId =
      database.Templates.Add(path, config, username, repository, branch)

    let repositories = database.Templates.ListRepositories()
    Assert.Single(repositories) |> ignore

    let templateItems = database.Templates.ListTemplateItems()
    Assert.Equal(2, templateItems.Length)

    // Test search functionality
    let foundByName =
      database.Templates.FindTemplateItems(
        QuickAccessSearch.Name "Basic Template"
      )

    Assert.Single(foundByName) |> ignore

    let foundByGroup =
      database.Templates.FindTemplateItems(
        QuickAccessSearch.Group(UMX.tag<TemplateGroup> "web.basic")
      )

    Assert.Single(foundByGroup) |> ignore

    // Test repository search
    let foundRepo =
      database.Templates.FindOne(TemplateSearchKind.Username "integration")

    Assert.True(foundRepo.IsSome)

    // Test cleanup
    let deleteResult = database.Templates.Delete(TemplateSearchKind.Id repoId)
    Assert.True(deleteResult)

    let emptyRepos = database.Templates.ListRepositories()
    Assert.Empty(emptyRepos)
    cleanup()

  [<Fact>]
  let ``ToFullName and ToFullNameWithBranch should work correctly``() =
    let args, cleanup = createDatabaseArgs()
    let database = Database.Create(args)
    let config = createSampleTemplateConfig()
    let path = UMX.tag<SystemPath> "/tmp/templates"
    let username = "testuser"
    let repository = UMX.tag<Repository> "test-repo"
    let branch = UMX.tag<Branch> "develop"

    let repositoryId =
      database.Templates.Add(path, config, username, repository, branch)

    let foundRepo =
      database.Templates.FindOne(TemplateSearchKind.Id repositoryId)

    match foundRepo with
    | Some repo ->
      Assert.Equal("testuser/test-repo", repo.ToFullName())
      Assert.Equal("testuser/test-repo/develop", repo.ToFullNameWithBranch())
    | None -> Assert.Fail("Repository should be found")

    cleanup()
