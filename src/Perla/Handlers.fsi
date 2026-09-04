namespace Perla.Handlers

open IcedTasks
open Perla
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

type DependencyOptions = { packages: string Set }

type ListPackagesOptions = { format: ListFormat }

type InstallOptions = {
  offline: bool option
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


[<RequireQualifiedAccess>]
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

  val runRestore:
    container: AppContainer -> options: InstallOptions -> CancellableTask<int>

  val runAddPackage:
    container: AppContainer ->
    options: DependencyOptions ->
      CancellableTask<int>

  val runRemovePackage:
    container: AppContainer ->
    options: DependencyOptions ->
      CancellableTask<int>

  val runListPackages:
    container: AppContainer ->
    options: ListPackagesOptions ->
      CancellableTask<int>

  val runDescribePerla:
    container: AppContainer -> options: DescribeOptions -> CancellableTask<int>
