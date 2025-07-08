namespace Perla.Handlers

open IcedTasks
open FsToolkit.ErrorHandling
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

type BuildOptions = { enablePreview: bool }

type SetupOptions = {
  installTemplates: bool
  skipPrompts: bool
}

type ListTemplatesOptions = { format: ListFormat }

type AddPackageOptions = {
  package: string
  version: string option
}

type RemovePackageOptions = { package: string }

type ListPackagesOptions = { format: ListFormat }

type InstallOptions = {
  offline: bool
  source: Perla.PkgManager.DownloadProvider voption
}

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

type TestingOptions = {
  browsers: Browser seq option
  files: string seq option
  skip: string seq option
  watch: bool option
  headless: bool option
  browserMode: BrowserMode option
}

type DescribeOptions = { properties: string[]; current: bool }


module Handlers =

  val runSetup: options: SetupOptions -> CancellableTask<int>

  val runNew: options: ProjectOptions -> CancellableTask<int>

  val runTemplate: options: TemplateRepositoryOptions -> CancellableTask<int>

  val runBuild: options: BuildOptions -> CancellableTask<int>

  val runServe: options: ServeOptions -> CancellableTask<int>

  val runTesting: options: TestingOptions -> CancellableTask<int>

  val runInstall: options: InstallOptions -> CancellableTask<int>

  val runAddPackage: options: AddPackageOptions -> CancellableTask<int>

  val runRemovePackage: options: RemovePackageOptions -> CancellableTask<int>

  val runListPackages: options: ListPackagesOptions -> CancellableTask<int>

  val runDescribePerla: options: DescribeOptions -> CancellableTask<int>
