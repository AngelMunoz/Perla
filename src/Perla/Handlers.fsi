namespace Perla.Handlers

open System.Threading
open System.Threading.Tasks

open FSharp.UMX

open Perla.Units
open Perla.Types
open Perla.PackageManager.Types

[<Struct; RequireQualifiedAccess>]
type ListFormat =
  | HumanReadable
  | TextOnly

type ServeOptions = {
  port: int option
  host: string option
  ssl: bool option
}

type BuildOptions = {
  enablePreview: bool
  enablePreloads: bool
  rebuildImportMap: bool
}

type SetupOptions = {
  installTemplates: bool
  skipPrompts: bool
}

type SearchOptions = { package: string; page: int }

type ShowPackageOptions = { package: string }

type ListTemplatesOptions = { format: ListFormat }

type AddPackageOptions = {
  package: string
  version: string option
  source: Perla.PkgManager.DownloadProvider option
  alias: string option
}

type RemovePackageOptions = {
  package: string
  alias: string option
}

type ListPackagesOptions = { format: ListFormat }

[<RequireQualifiedAccess; Struct>]
type RunTemplateOperation =
  | Add
  | Update
  | Remove
  | List of ListFormat

type TemplateRepositoryOptions = {
  fullRepositoryName: string option
  operation: RunTemplateOperation
}

type ProjectOptions = {
  projectName: string
  byId: string option
  byShortName: string option
}

type RestoreOptions = {
  source: Perla.PkgManager.DownloadProvider option
}

type TestingOptions = {
  browsers: Browser seq option
  files: string seq option
  skip: string seq option
  watch: bool option
  headless: bool option
  browserMode: BrowserMode option
}

type DescribeOptions = {
  properties: string[] option
  current: bool
}

[<Struct>]
type PathOperation =
  | AddOrUpdate of
    addImport: string<BareImport> *
    addPath: string<ResolutionUrl>
  | Remove of removeImport: string

type PathsOptions = { operation: PathOperation }

module Handlers =

  val runSetup:
    options: SetupOptions * cancellationToken: CancellationToken -> Task<int>

  val runNew:
    options: ProjectOptions * cancellationToken: CancellationToken -> Task<int>

  val runTemplate:
    options: TemplateRepositoryOptions * cancellationToken: CancellationToken ->
      Task<int>

  val runBuild:
    options: BuildOptions * cancellationToken: CancellationToken -> Task<int>

  val runServe:
    options: ServeOptions * cancellationToken: CancellationToken -> Task<int>

  val runTesting:
    options: TestingOptions * cancellationToken: CancellationToken -> Task<int>

  val runSearchPackage:
    options: SearchOptions * cancellationToken: CancellationToken -> Task<int>

  val runShowPackage:
    options: ShowPackageOptions * cancellationToken: CancellationToken ->
      Task<int>

  val runAddResolution: options: PathsOptions -> Task<int>

  val runAddPackage:
    options: AddPackageOptions * cancellationToken: CancellationToken ->
      Task<int>

  val runRemovePackage:
    options: RemovePackageOptions * cancellationToken: CancellationToken ->
      Task<int>

  val runListPackages: options: ListPackagesOptions -> Task<int>

  val runRestoreImportMap:
    options: RestoreOptions * cancellationToken: CancellationToken -> Task<int>

  val runDescribePerla: options: DescribeOptions -> Task<int>
