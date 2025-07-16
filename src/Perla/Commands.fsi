namespace Perla.Commands

open System.CommandLine
open FSharp.SystemCommandLine

open Perla
open Perla.Handlers
open Perla.Types


[<Class; Sealed>]
type PerlaOptions =
  static member PackageSource: ActionInput<Perla.PkgManager.DownloadProvider voption>
  static member Browsers: ActionInput<Browser Set>
  static member DisplayMode: ActionInput<ListFormat>

[<Class; Sealed>]
type PerlaArguments =
  static member Properties: ActionInput<string array>

[<RequireQualifiedAccess>]
module SharedInputs =
  val source: ActionInput<Perla.PkgManager.DownloadProvider voption>

[<RequireQualifiedAccess>]
module DescribeInputs =
  val perlaProperties: ActionInput<string[]>
  val describeCurrent: ActionInput<bool>

[<RequireQualifiedAccess>]
module BuildInputs =
  val preview: ActionInput<bool option>

[<RequireQualifiedAccess>]
module SetupInputs =
  val installTemplates: ActionInput<bool option>
  val skipPrompts: ActionInput<bool option>

[<RequireQualifiedAccess>]
module PackageInputs =
  val packages: ActionInput<string Set>
  val version: ActionInput<string option>
  val alias: ActionInput<string option>
  val showAsNpm: ActionInput<bool option>

[<RequireQualifiedAccess>]
module TemplateInputs =
  val repositoryName: ActionInput<string option>
  val addTemplate: ActionInput<bool option>
  val updateTemplate: ActionInput<bool option>
  val removeTemplate: ActionInput<bool option>
  val displayMode: ActionInput<ListFormat>

[<RequireQualifiedAccess>]
module ProjectInputs =
  val projectName: ActionInput<string>
  val byId: ActionInput<string option>
  val byShortName: ActionInput<string option>

[<RequireQualifiedAccess>]
module TestingInputs =
  val browsers: ActionInput<Browser Set>
  val files: ActionInput<string array>
  val skips: ActionInput<string array>
  val headless: ActionInput<bool option>
  val watch: ActionInput<bool option>
  val sequential: ActionInput<bool option>

[<RequireQualifiedAccess>]
module ServeInputs =
  val port: ActionInput<int option>
  val host: ActionInput<string option>
  val ssl: ActionInput<bool option>

[<RequireQualifiedAccess>]
module Commands =
  val Build: container: AppContainer -> Command
  val Serve: container: AppContainer -> Command
  val RemovePackage: container: AppContainer -> Command
  val Install: container: AppContainer -> Command
  val AddPackage: container: AppContainer -> Command
  val ListPackages: container: AppContainer -> Command
  val Template: container: AppContainer -> Command
  val NewProject: container: AppContainer -> Command
  val Test: container: AppContainer -> Command
  val Describe: container: AppContainer -> Command
