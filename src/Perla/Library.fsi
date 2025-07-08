namespace Perla

open System.Text.RegularExpressions

open Spectre.Console
open Spectre.Console.Rendering
open FSharp.UMX
open Perla.Units
open Perla.Types

[<AutoOpen>]
module Lib =

  [<return: Struct>]
  val internal (|ParseRegex|_|):
    regex: Regex -> str: string -> string list voption

  val internal parseFullRepositoryName:
    value: string option -> (string * string * string) voption

  val internal getTemplateAndChild:
    templateName: string -> string option * string * string option

  val internal dependencyTable: deps: PkgDependency Set * title: string -> Table

  val internal (|ScopedPackage|Package|):
    package: string -> Choice<string, string>

  val internal parsePackageName: name: string -> string * string * string option

  val internal (|Log|Debug|Info|Err|Warning|Clear|):
    string -> Choice<unit, unit, unit, unit, unit, unit>

  val internal (|TopLevelProp|NestedProp|TripleNestedProp|InvalidPropPath|):
    string -> Choice<string, string * string, string * string * string, unit>

  type FableConfig with

    member Item: string -> IRenderable option with get
    member ToTree: unit -> Tree

  type DevServerConfig with

    member Item: string -> IRenderable option with get
    member ToTree: unit -> Tree

  type EsbuildConfig with

    member Item: string -> IRenderable option with get
    member ToTree: unit -> Tree

  type BuildConfig with

    member Item: string -> IRenderable option with get
    member ToTree: unit -> Tree

  type TestConfig with

    member Item: string -> IRenderable option with get
    member Item: (string * string) -> IRenderable option with get
    member ToTree: unit -> Tree

  type PerlaConfig with

    member Item: string -> IRenderable option with get
    member Item: (string * string) -> IRenderable option with get
    member Item: (string * string * string) -> IRenderable option with get
