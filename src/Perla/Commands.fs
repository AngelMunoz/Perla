namespace Perla.Commands


open System.CommandLine
open System.CommandLine.Parsing
open Microsoft.Extensions.Logging

open IcedTasks

open Perla
open Perla.Types
open Perla.Handlers
open Perla.Warmup

open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input



[<Class; Sealed>]
type PerlaOptions =

  static member PackageSource =
    option<PkgManager.DownloadProvider voption> "--source"
    |> alias "-s"
    |> desc "The source to download packages from. Defaults to jspm.io"
    |> acceptOnlyFromAmong [ "jspm.io"; "unpkg"; "jsdelivr" ]
    |> defaultValue ValueNone
    |> customParser(fun result ->
      match result.Tokens |> Seq.tryHead with
      | Some token ->
        PkgManager.DownloadProvider.fromString token.Value |> ValueSome
      | None -> ValueNone)

  static member Browsers =
    option<Browser Set> "--browsers"
    |> alias "-b"
    |> desc "Version of the package to install"
    |> arity ArgumentArity.ZeroOrMore
    |> allowMultipleArgumentsPerToken
    |> acceptOnlyFromAmong [ "chromium"; "firefox"; "webkit"; "edge"; "chrome" ]
    |> defaultValue Set.empty
    |> customParser(fun result ->
      result.Tokens
      |> Seq.map(fun token -> token.Value |> Browser.FromString)
      |> Set.ofSeq)

  static member DisplayMode =
    option<ListFormat> "--list-format"
    |> desc "The chosen format to display the existing templates"
    |> acceptOnlyFromAmong [ "table"; "text" ]
    |> defaultValue ListFormat.HumanReadable
    |> customParser(fun result ->
      match result.Tokens |> Seq.tryHead with
      | Some token ->
        match token.Value with
        | "table" -> ListFormat.HumanReadable
        | "text" -> ListFormat.TextOnly
        | _ -> ListFormat.HumanReadable
      | None -> ListFormat.HumanReadable)

[<Class; Sealed>]
type PerlaArguments =
  static member Properties =
    argument<string array> "properties"
    |> desc "A property, properties or json path-like string names to describe"
    |> arity ArgumentArity.ZeroOrMore
    |> customParser(fun result ->
      result.Tokens |> Seq.map _.Value |> Seq.distinct |> Seq.toArray)

type GlobalOptions = {
  ci: bool
  skipPrompts: bool
  previewCommand: bool
  setup: bool
  logLevel: LogLevel
}

module GlobalOptions =

  let setup: ActionInput<bool option> =
    optionMaybe<bool> "--setup"
    |> alias "-s"
    |> description
      "Run the setup command to install templates and other dependencies"
    |> defaultValue(Some true)

  let ci: ActionInput<bool option> =
    optionMaybe<bool> "--ci"
    |> description
      "Run the command in CI mode, which disables interactive prompts"
    |> defaultValue None

  let skipPrompts: ActionInput<bool option> =
    optionMaybe<bool> "--skip"
    |> description "Skip interactive prompts and use defaults"
    |> defaultValue None

  let previewCommand: ActionInput<bool option> =
    optionMaybe<bool> "--preview-command"
    |> description "Allows running a command before its official release"
    |> defaultValue None

  let logLevel: ActionInput<LogLevel> =
    let inline parser(result: ArgumentResult) =
      match result.Tokens |> Seq.tryHead with
      | Some token ->
        match token.Value with
        | "Trace" -> LogLevel.Trace
        | "Debug" -> LogLevel.Debug
        | "Information" -> LogLevel.Information
        | "Warning" -> LogLevel.Warning
        | "Error" -> LogLevel.Error
        | "Critical" -> LogLevel.Critical
        | _ -> LogLevel.Information
      | None -> LogLevel.Information

    Option<LogLevel>(
      "--log-level",
      "-l",
      Description = "The log level to use for the command",
      Required = false,
      CustomParser = parser,
      DefaultValueFactory = (fun _ -> LogLevel.Information)
    )
      .AcceptOnlyFromAmong(
        "Trace",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Critical"
      )
    |> Input.ofOption

  let bind parseResult =
    let ci = ci.GetValue parseResult |> Option.defaultValue false

    let skipPrompts =
      skipPrompts.GetValue parseResult |> Option.defaultValue false

    let previewCommand =
      previewCommand.GetValue parseResult |> Option.defaultValue false

    let setup = setup.GetValue parseResult |> Option.defaultValue true

    {
      ci = ci
      skipPrompts = skipPrompts
      previewCommand = previewCommand
      setup = setup
      logLevel = logLevel.GetValue parseResult
    }



[<RequireQualifiedAccess>]
module SharedInputs =

  let source: ActionInput<Perla.PkgManager.DownloadProvider voption> =
    PerlaOptions.PackageSource

[<RequireQualifiedAccess>]
module DescribeInputs =
  let perlaProperties: ActionInput<string[]> =
    Argument<string[]>(
      "properties"
      , CustomParser =
        fun (result: ArgumentResult) -> [|
          for token in result.Tokens -> token.Value
        |]
      , Description =
        "A property, properties or json path-like string names to describe"
      , Arity = ArgumentArity.ZeroOrMore
    )
    |> ofArgument

  let describeCurrent: ActionInput<bool> =
    option "--current"
    |> alias "-c"
    |> description
      "Take my current perla.json file and print my current configuration"

[<RequireQualifiedAccess>]
module SetupInputs =
  let installTemplates: ActionInput<bool option> =
    optionMaybe "--templates"
    |> alias "-t"
    |> description "Install Default templates (defaults to true)"

  let skipPrompts: ActionInput<bool option> =
    optionMaybe "--skip"
    |> aliases [ "-s"; "-y" ]
    |> description "Skip interactive prompts and use defaults"

[<RequireQualifiedAccess>]
module PackageInputs =

  let offline =
    optionMaybe "--offline"
    |> alias "-o"
    |> description "Install packages without network access"


  let packages: ActionInput<string Set> =
    let parser(result: ArgumentResult) =
      result.Tokens |> Seq.map _.Value |> Set.ofSeq

    Argument<string Set>(
      "packages",
      CustomParser = parser,
      Arity = ArgumentArity.OneOrMore,
      Description = "set of packages to add as dependencies"
    )
    |> Input.ofArgument


  let version: ActionInput<string option> =
    optionMaybe "--version"
    |> alias "-v"
    |> description "Version of the package to install"

  let alias: ActionInput<string option> =
    optionMaybe "--alias"
    |> alias "-a"
    |> description "Alias name for the package"

  let showAsNpm: ActionInput<bool option> =
    optionMaybe "--npm"
    |> aliases [ "--as-package-json"; "-j" ]
    |> description "Show the packages similar to npm's package.json"

[<RequireQualifiedAccess>]
module TemplateInputs =
  let repositoryName: ActionInput<string option> =
    argumentMaybe "TemplateRepositoryName"
    |> description "The User/repository name combination"

  let addTemplate: ActionInput<bool option> =

    optionMaybe "--add"
    |> alias "-a"
    |> description "If it doesn't exist, adds the template repository to Perla"

  let updateTemplate: ActionInput<bool option> =
    optionMaybe "--update"
    |> alias "-u"
    |> description "If it exists, updates the template repository for Perla"

  let removeTemplate: ActionInput<bool option> =

    optionMaybe "--remove"
    |> alias "-r"
    |> description "If it exists, removes the template repository for Perla"

  let displayMode: ActionInput<ListFormat> = PerlaOptions.DisplayMode

[<RequireQualifiedAccess>]
module ProjectInputs =

  let projectName: ActionInput<string> =
    argument "name" |> description "Name of the new project"

  let byId: ActionInput<string option> =
    optionMaybe "--id"
    |> alias "-i"
    |> description
      "fully.qualified.name of the template, e.g. perla.templates.vanilla.js"

  let byShortName: ActionInput<string option> =
    optionMaybe "--template"
    |> alias "-t"
    |> description "shortname of the template, e.g. ff"

  let skipPrompts: ActionInput<bool> =
    option "--skip"
    |> aliases [ "-s"; "-y" ]
    |> description "Skip interactive prompts and use defaults"
    |> defaultValue false

[<RequireQualifiedAccess>]
module BuildInputs =
  let preview: ActionInput<bool option> =
    optionMaybe "--preview"
    |> alias "-p"
    |> description
      "Enable preview mode, which will build the application and start a static server"

[<RequireQualifiedAccess>]
module TestingInputs =
  let browsers: ActionInput<Browser Set> = PerlaOptions.Browsers

  let files: ActionInput<string array> =
    option "--tests"
    |> alias "-t"
    |> defaultValue Array.empty
    |> description
      "Specify a glob of tests to run. e.g '**/featureA/*.test.js' or 'tests/my-test.test.js'"


  let skips: ActionInput<string array> =
    option "--skip"
    |> aliases [ "-s" ]
    |> defaultValue Array.empty
    |> description
      "Specify a glob of tests to skip. e.g '**/featureA/*.test.js' or 'tests/my-test.test.js'"


  let headless: ActionInput<bool option> =
    optionMaybe "--headless"
    |> alias "-hl"
    |> description
      "Turn on or off the Headless mode and open the browser (useful for debugging tests)"

  let watch: ActionInput<bool option> =
    optionMaybe "--watch"
    |> alias "-w"
    |> description "Start the server and keep watching for file changes"

  let sequential: ActionInput<bool option> =
    optionMaybe "--browser-sequential"
    |> alias "-bs"
    |> description
      "Run each browser's test suite in sequence, rather than parallel"

[<RequireQualifiedAccess>]
module ServeInputs =
  let port: ActionInput<int option> =
    optionMaybe "--port"
    |> alias "-p"
    |> description "Port where the application starts"

  let host: ActionInput<string option> =
    optionMaybe "--host"
    |> description "network ip address where the application will run"

  let ssl: ActionInput<bool option> =
    optionMaybe "--ssl" |> description "Run dev server with SSL"

[<RequireQualifiedAccess>]
module Commands =

  let Build(container: AppContainer) =

    let handleCommand(context: ActionContext, enablePreview: bool option) = task {
      let globalOptions = GlobalOptions.bind context.ParseResult

      let proceed() = cancellableTask {
        let options = {
          enablePreview = defaultArg enablePreview false
        }

        return! Handlers.runBuild container options
      }

      if globalOptions.setup then
        let! result =
          Check.Setup
            (container.Logger,
             container.Db,
             container.Configuration.PerlaConfig,
             container.FableService,
             [ Esbuild; Fable ])
            context.CancellationToken

        match result with
        | Continue -> return! proceed () context.CancellationToken
        | Recover value ->
          let recoverArgs: Recover.RecoverArgs = {
            config = container.Configuration.PerlaConfig
            db = container.Db
            pfsm = container.FsManager
            logger = container.Logger
            skipPrompts = globalOptions.skipPrompts
            ci = globalOptions.ci || System.Console.IsOutputRedirected
          }

          let! canContinue =
            Recover.From recoverArgs (Recover value) context.CancellationToken

          match canContinue with
          | Ok() -> return! proceed () context.CancellationToken
          | _ ->
            container.Logger.LogError(
              "Perla setup failed, please run `perla setup` to fix the issue."
            )

            return 1
        | HardExit ->
          container.Logger.LogError(
            "Perla setup failed, please run `perla setup` to fix the issue."
          )

          return 1

      else
        return! proceed () context.CancellationToken
    }


    command "build" {
      description "Builds the SPA application for distribution"
      addAlias "b"

      inputs(context, BuildInputs.preview)

      setAction handleCommand
    }

  let Serve(container: AppContainer) =
    let handleCommand
      (
        context: ActionContext,
        port: int option,
        host: string option,
        ssl: bool option
      ) =
      task {
        let globalOptions = GlobalOptions.bind context.ParseResult

        let proceed() = cancellableTask {
          let options = { port = port; host = host; ssl = ssl }
          return! Handlers.runServe container options context.CancellationToken
        }

        if globalOptions.setup then
          let! result =
            Check.Setup
              (container.Logger,
               container.Db,
               container.Configuration.PerlaConfig,
               container.FableService,
               [ Fable; Esbuild ])
              context.CancellationToken

          match result with
          | Continue -> return! proceed () context.CancellationToken
          | Recover value ->
            let recoverArgs: Recover.RecoverArgs = {
              config = container.Configuration.PerlaConfig
              db = container.Db
              pfsm = container.FsManager
              logger = container.Logger
              skipPrompts = globalOptions.skipPrompts
              ci = globalOptions.ci || System.Console.IsOutputRedirected
            }

            let! canContinue =
              Recover.From recoverArgs (Recover value) context.CancellationToken

            match canContinue with
            | Ok() -> return! proceed () context.CancellationToken
            | _ ->
              container.Logger.LogError(
                "Perla setup failed, please run `perla setup` to fix the issue."
              )

              return 1
          | HardExit ->
            container.Logger.LogError(
              "Perla setup failed, please run `perla setup` to fix the issue."
            )

            return 1

        else
          return! proceed () context.CancellationToken
      }

    let desc =
      "Starts the development server and if fable projects are present it also takes care of it."

    command "serve" {
      description desc
      addAliases [ "s"; "start" ]

      inputs(context, ServeInputs.port, ServeInputs.host, ServeInputs.ssl)

      setAction handleCommand
    }

  let RemovePackage(container: AppContainer) =

    let handleCommand(ctx: ActionContext, package: string Set) =
      let options = { packages = package }
      Handlers.runRemovePackage container options ctx.CancellationToken

    command "remove" {
      description "Removes a package from the project dependencies"

      inputs(context, PackageInputs.packages)
      setAction handleCommand
    }

  let Install(container: AppContainer) =
    let handleCommand
      (
        ctx: ActionContext,
        offline: bool option,
        source: PkgManager.DownloadProvider voption
      ) =
      let options = { offline = offline; source = source }

      Handlers.runInstall container options ctx.CancellationToken

    command "install" {
      description "Installs the project dependencies from the perla.json file"
      inputs(context, PackageInputs.offline, SharedInputs.source)
      setAction handleCommand
    }

  let AddPackage(container: AppContainer) =

    let handleCommand(ctx: ActionContext, packages: string Set) =
      let options = { packages = packages }

      Handlers.runAddPackage container options ctx.CancellationToken

    command "add" {
      description "Adds a package to the project dependencies"

      inputs(context, PackageInputs.packages)

      setAction handleCommand
    }

  let ListPackages(container: AppContainer) =

    let handleCommand(ctx: ActionContext, asNpm: bool option) =
      let args = {
        format =
          asNpm
          |> Option.map(fun asNpm ->
            if asNpm then
              ListFormat.TextOnly
            else
              ListFormat.HumanReadable)
          |> Option.defaultValue ListFormat.HumanReadable
      }

      Handlers.runListPackages container args ctx.CancellationToken

    command "list" {
      addAlias "ls"

      description
        "Lists the current dependencies in a table or an npm style json string"

      inputs(context, PackageInputs.showAsNpm)
      setAction handleCommand
    }

  let Template(container: AppContainer) =

    let handleCommand
      (
        ctx: ActionContext,
        name: string option,
        add: bool option,
        update: bool option,
        remove: bool option,
        format: ListFormat
      ) =
      task {
        let globalOptions = GlobalOptions.bind ctx.ParseResult

        let proceed() = cancellableTask {
          let operation =
            let remove =
              remove
              |> Option.map (function
                | true -> Some RunTemplateOperation.Remove
                | false -> None)
              |> Option.flatten

            let update =
              update
              |> Option.map (function
                | true -> Some RunTemplateOperation.Update
                | false -> None)
              |> Option.flatten

            let add =
              add
              |> Option.map (function
                | true -> Some RunTemplateOperation.Add
                | false -> None)
              |> Option.flatten

            let format = RunTemplateOperation.List format

            remove
            |> Option.orElse update
            |> Option.orElse add
            |> Option.defaultValue format

          let options = {
            fullRepositoryName = name
            operation = operation
          }

          return! Handlers.runTemplate container options ctx.CancellationToken
        }

        if globalOptions.setup then
          let! result =
            Check.Setup
              (container.Logger,
               container.Db,
               container.Configuration.PerlaConfig,
               container.FableService,
               [])
              ctx.CancellationToken

          match result with
          | Continue -> return! proceed () ctx.CancellationToken
          | Recover value ->
            let recoverArgs: Recover.RecoverArgs = {
              config = container.Configuration.PerlaConfig
              db = container.Db
              pfsm = container.FsManager
              logger = container.Logger
              skipPrompts = globalOptions.skipPrompts
              ci = globalOptions.ci || System.Console.IsOutputRedirected
            }

            let! canContinue =
              Recover.From recoverArgs (Recover value) ctx.CancellationToken

            match canContinue with
            | Ok() -> return! proceed () ctx.CancellationToken
            | _ ->
              container.Logger.LogError(
                "Perla setup failed, please run `perla setup` to fix the issue."
              )

              return 1
          | HardExit ->
            container.Logger.LogError(
              "Perla setup failed, please run `perla setup` to fix the issue."
            )

            return 1

        else
          return! proceed () ctx.CancellationToken
      }

    let template = command "templates" {
      addAlias "t"

      description
        "Handles Template Repository operations such as list, add, update, and remove templates"

      inputs(
        context,
        TemplateInputs.repositoryName,
        TemplateInputs.addTemplate,
        TemplateInputs.updateTemplate,
        TemplateInputs.removeTemplate,
        TemplateInputs.displayMode
      )

      setAction handleCommand
    }

    template

  let NewProject(container: AppContainer) =

    let handleCommand
      (
        ctx: ActionContext,
        name: string,
        byId: string option,
        byShortName: string option,
        skipPrompts: bool
      ) =
      task {
        let globalOptions = GlobalOptions.bind ctx.ParseResult

        let proceed() = cancellableTask {
          let options = {
            projectName = name
            byId = byId
            byShortName = byShortName
            skipPrompts = skipPrompts
          }

          return! Handlers.runNew container options ctx.CancellationToken
        }

        if globalOptions.setup then
          let! result =
            Check.Setup
              (container.Logger,
               container.Db,
               container.Configuration.PerlaConfig,
               container.FableService,
               [ Esbuild; Fable ])
              ctx.CancellationToken

          match result with
          | Continue -> return! proceed () ctx.CancellationToken
          | Recover value ->
            let recoverArgs: Recover.RecoverArgs = {
              config = container.Configuration.PerlaConfig
              db = container.Db
              pfsm = container.FsManager
              logger = container.Logger
              skipPrompts = globalOptions.skipPrompts
              ci = globalOptions.ci || System.Console.IsOutputRedirected
            }

            let! canContinue =
              Recover.From recoverArgs (Recover value) ctx.CancellationToken

            match canContinue with
            | Ok() -> return! proceed () ctx.CancellationToken
            | _ ->
              container.Logger.LogError(
                "Perla setup failed, please run `perla setup` to fix the issue."
              )

              return 1
          | HardExit ->
            container.Logger.LogError(
              "Perla setup failed, please run `perla setup` to fix the issue."
            )

            return 1

        else
          return! proceed () ctx.CancellationToken
      }

    command "new" {
      addAliases [ "n"; "create"; "generate" ]

      description
        "Creates a new project based on the selected template if it exists"

      inputs(
        context,
        ProjectInputs.projectName,
        ProjectInputs.byId,
        ProjectInputs.byShortName,
        ProjectInputs.skipPrompts
      )

      setAction handleCommand
    }

  let Test(container: AppContainer) =

    let handleCommand
      (
        ctx: ActionContext,
        browsers: Browser Set,
        files: string array,
        skips: string array,
        headless: bool option,
        watch: bool option,
        sequential: bool option
      ) =
      let options = {
        browsers = if Set.isEmpty browsers then None else Some browsers
        files = if files |> Array.isEmpty then None else Some files
        skip = if skips |> Array.isEmpty then None else Some skips
        headless = headless
        watch = watch
        browserMode =
          sequential
          |> Option.map(fun sequential ->
            if sequential then Some BrowserMode.Sequential else None)
          |> Option.flatten
      }

      Handlers.runTesting container options ctx.CancellationToken

    let cmd = command "test" {
      description "Runs client side tests in a headless browser"

      inputs(
        context,
        TestingInputs.browsers,
        TestingInputs.files,
        TestingInputs.skips,
        TestingInputs.headless,
        TestingInputs.watch,
        TestingInputs.sequential
      )

      setAction handleCommand
    }

    cmd.Hidden <- true
    cmd

  let Describe(container: AppContainer) =

    let handleCommand(ctx: ActionContext, properties: string[], current: bool) =
      let args = {
        properties = properties
        current = current
      }

      Handlers.runDescribePerla container args ctx.CancellationToken

    command "describe" {
      addAlias "ds"

      description
        "Describes the perla.json file or it's properties as requested"

      inputs(
        context,
        DescribeInputs.perlaProperties,
        DescribeInputs.describeCurrent
      )

      setAction handleCommand

    }
