namespace Perla.Handlers

open IcedTasks
open FsToolkit.ErrorHandling
open Perla
open Perla.Types
open Perla.Extensibility


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
  source: PkgManager.DownloadProvider voption
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
  skipPrompts: bool
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

  val runNew:
    container: AppContainer -> options: ProjectOptions -> CancellableTask<int>

  val runTemplate:
    container: AppContainer ->
    options: TemplateRepositoryOptions ->
      CancellableTask<int>

  val runBuild:
    container: AppContainer -> options: BuildOptions -> CancellableTask<int>

  val runServe:
    container: AppContainer -> options: ServeOptions -> CancellableTask<int>

  val runTesting:
    container: AppContainer -> options: TestingOptions -> CancellableTask<int>

  val runInstall:
    container: AppContainer -> options: InstallOptions -> CancellableTask<int>

  val runAddPackage:
    container: AppContainer ->
    options: AddPackageOptions ->
      CancellableTask<int>

  val runRemovePackage:
    container: AppContainer ->
    options: RemovePackageOptions ->
      CancellableTask<int>

  val runListPackages:
    container: AppContainer ->
    options: ListPackagesOptions ->
      CancellableTask<int>

  val runDescribePerla:
    container: AppContainer -> options: DescribeOptions -> CancellableTask<int>
