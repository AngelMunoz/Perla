namespace Perla.Handlers

open IcedTasks
open FSharp.UMX

open Perla.Units
open Perla.Types


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

  val runSetup: options: SetupOptions -> CancellableTask<int>

  val runNew: options: ProjectOptions -> CancellableTask<int>

  val runTemplate: options: TemplateRepositoryOptions -> CancellableTask<int>

  val runBuild: options: BuildOptions -> CancellableTask<int>

  val runServe: options: ServeOptions -> CancellableTask<int>

  val runTesting: options: TestingOptions -> CancellableTask<int>

  val runAddPackage: options: AddPackageOptions -> CancellableTask<int>

  val runRemovePackage: options: RemovePackageOptions -> CancellableTask<int>

  val runListPackages: options: ListPackagesOptions -> CancellableTask<int>

  val runDescribePerla: options: DescribeOptions -> CancellableTask<int>
