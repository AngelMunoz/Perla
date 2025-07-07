namespace Perla.Commands

open System.CommandLine
open FSharp.SystemCommandLine

open Perla.Handlers
open Perla.Types


[<Class; Sealed>]
type PerlaOptions =
  static member PackageSource: Option<Perla.PkgManager.DownloadProvider voption>
  static member Browsers: Option<Browser array>
  static member DisplayMode: Option<ListFormat>

[<Class; Sealed>]
type PerlaArguments =
  static member Properties: Argument<string array>

  static member ArgStringMaybe:
    name: string * ?description: string -> Argument<string option>

[<RequireQualifiedAccess>]
module SharedInputs =
  val source: HandlerInput<Perla.PkgManager.DownloadProvider voption>


[<RequireQualifiedAccess>]
module DescribeInputs =
  val perlaProperties: HandlerInput<string array option>
  val describeCurrent: HandlerInput<bool>

[<RequireQualifiedAccess>]
module BuildInputs =
  val enablePreloads: HandlerInput<bool option>
  val rebuildImportMap: HandlerInput<bool option>
  val preview: HandlerInput<bool option>

[<RequireQualifiedAccess>]
module SetupInputs =
  val installTemplates: HandlerInput<bool option>
  val skipPrompts: HandlerInput<bool option>

[<RequireQualifiedAccess>]
module PackageInputs =
  val package: HandlerInput<string>
  val alias: HandlerInput<string option>
  val version: HandlerInput<string option>
  val currentPage: HandlerInput<int option>
  val showAsNpm: HandlerInput<bool option>

  val import: HandlerInput<string>
  val resolution: HandlerInput<string option>
  val addOrUpdate: HandlerInput<bool option>
  val removeResolution: HandlerInput<bool>

[<RequireQualifiedAccess>]
module TemplateInputs =
  val repositoryName: HandlerInput<string option>
  val addTemplate: HandlerInput<bool option>
  val updateTemplate: HandlerInput<bool option>
  val removeTemplate: HandlerInput<bool option>
  val displayMode: HandlerInput<ListFormat>

[<RequireQualifiedAccess>]
module ProjectInputs =
  val projectName: HandlerInput<string>
  val byId: HandlerInput<string option>
  val byShortName: HandlerInput<string option>

[<RequireQualifiedAccess>]
module TestingInputs =
  val browsers: HandlerInput<Browser array>
  val files: HandlerInput<string array>
  val skips: HandlerInput<string array>
  val headless: HandlerInput<bool option>
  val watch: HandlerInput<bool option>
  val sequential: HandlerInput<bool option>

[<RequireQualifiedAccess>]
module ServeInputs =
  val port: HandlerInput<int option>
  val host: HandlerInput<string option>
  val ssl: HandlerInput<bool option>

[<RequireQualifiedAccess>]
module Commands =
  val Setup: Command
  val Template: Command
  val Describe: Command

  val Build: Command
  val Serve: Command
  val Test: Command

  val AddPackage: Command
  val RemovePackage: Command
  val ListPackages: Command

  val NewProject: Command
