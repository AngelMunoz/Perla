module Perla.Configuration

open Perla.Types
open FSharp.Data.Adaptive

type DevServerField =
  | Port of int
  | Host of string
  | LiveReload of bool
  | UseSSL of bool
  | MinifySources of bool

[<RequireQualifiedAccess>]
type TestingField =
  | Browsers of Browser seq
  | Includes of string seq
  | Excludes of string seq
  | Watch of bool
  | Headless of bool
  | BrowserMode of BrowserMode


module internal ConfigExtraction =

  [<RequireQualifiedAccess>]
  module FromFields =
    val GetServerFields:
      DevServerConfig * DevServerField seq option -> DevServerField seq

    val GetMinify: DevServerField seq -> bool

    val GetDevServerOptions:
      DevServerConfig * DevServerField seq -> DevServerConfig

    val GetTesting: TestConfig * TestingField seq option -> TestConfig

  val FromEnv: config: PerlaConfig -> PerlaConfig

  val FromCli:
    serverOptions: DevServerField seq option ->
    testingOptions: TestingField seq option ->
    config: PerlaConfig ->
      PerlaConfig

[<Class>]
type ConfigurationManager =
  new: PerlaConfig aval -> ConfigurationManager

  member UpdateFromCliArgs:
    ?serverOptions: seq<DevServerField> * ?testingOptions: seq<TestingField> ->
      unit

  member PerlaConfig: PerlaConfig aval with get
