module Perla.Configuration

open FSharp.Data.Adaptive
open Perla.Types

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
    let GetServerFields
      (config: DevServerConfig, serverOptions: DevServerField seq option)
      =
      let getDefaults() = seq {
        Port config.port
        Host config.host
        LiveReload config.liveReload
        UseSSL config.useSSL
      }

      let options = serverOptions |> Option.defaultWith getDefaults

      if Seq.isEmpty options then getDefaults() else options

    let GetMinify(serverOptions: DevServerField seq) =
      serverOptions
      |> Seq.tryPick(fun opt ->
        match opt with
        | MinifySources minify -> Some minify
        | _ -> None)
      |> Option.defaultWith(fun _ -> true)

    let GetDevServerOptions
      (config: DevServerConfig, serverOptions: DevServerField seq)
      =
      serverOptions
      |> Seq.fold
        (fun current next ->
          match next with
          | Port port -> { current with port = port }
          | Host host -> { current with host = host }
          | LiveReload liveReload -> { current with liveReload = liveReload }
          | UseSSL useSSL -> { current with useSSL = useSSL }
          | _ -> current)
        config

    let GetTesting
      (testing: TestConfig, testingOptions: TestingField seq option)
      =
      defaultArg testingOptions Seq.empty
      |> Seq.fold
        (fun current next ->
          match next with
          | TestingField.Browsers value -> { current with browsers = value }
          | TestingField.Includes value -> { current with includes = value }
          | TestingField.Excludes value -> { current with excludes = value }
          | TestingField.Watch value -> { current with watch = value }
          | TestingField.Headless value -> { current with headless = value }
          | TestingField.BrowserMode value ->
              { current with browserMode = value })
        testing

  // will enable in the future
  let FromEnv(config: PerlaConfig) : PerlaConfig = config

  let FromCli
    (serverOptions: DevServerField seq option)
    (testingOptions: TestingField seq option)
    (config: PerlaConfig)
    : PerlaConfig =

    let serverOptions =
      FromFields.GetServerFields(config.devServer, serverOptions)

    let devServer =
      FromFields.GetDevServerOptions(config.devServer, serverOptions)

    let testing = FromFields.GetTesting(config.testing, testingOptions)

    let esbuild = {
      config.esbuild with
          minify = FromFields.GetMinify serverOptions
    }

    {
      config with
          devServer = devServer
          esbuild = esbuild
          testing = testing
    }


type ConfigurationManager(config: PerlaConfig aval) =


  let fromCli = cval(config |> AVal.force)


  member _.UpdateFromCliArgs
    (?serverOptions: DevServerField seq, ?testingOptions: TestingField seq)
    =
    let wilUpdate =
      config
      |> AVal.force
      |> ConfigExtraction.FromCli serverOptions testingOptions

    transact(fun () -> fromCli.Value <- wilUpdate)


  member _.PerlaConfig =
    config
    |> AVal.map2
      (fun cliConfig currentConfig -> {
        currentConfig with
            devServer = cliConfig.devServer
            esbuild = cliConfig.esbuild
            testing = cliConfig.testing
      })
      fromCli
