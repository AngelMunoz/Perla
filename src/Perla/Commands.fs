namespace Perla.Commands

open System.Threading

open System.CommandLine
open System.CommandLine.Invocation
open System.CommandLine.Parsing

open Perla
open Perla.Extensibility
open Perla.Types
open Perla.Handlers

open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input



[<Class; Sealed>]
type PerlaOptions =

  static member PackageSource =

    let inline parser(result: ArgumentResult) =
      match result.Tokens |> Seq.tryHead with
      | Some token ->
        PkgManager.DownloadProvider.fromString token.Value |> ValueSome
      | None -> ValueNone

    Option<PkgManager.DownloadProvider voption>(
      "--source",
      "-s",
      CustomParser = parser,
      Description = "The source to download packages from. Defaults to jspm.io",
      Required = false
    )
      .AcceptOnlyFromAmong("jspm.io", "unpkg", "jsdelivr")

  static member Browsers =
    let inline parser(result: ArgumentResult) =
      result.Tokens
      |> Seq.map(fun token -> token.Value |> Browser.FromString)
      |> Set.ofSeq

    Option<Browser Set>(
      "--browsers",
      "-b",
      CustomParser = parser,
      Description = "Version of the package to install",
      Arity = ArgumentArity.ZeroOrMore,
      AllowMultipleArgumentsPerToken = true
    )
      .AcceptOnlyFromAmong("chromium", "firefox", "webkit", "edge", "chrome")

  static member DisplayMode: Option<ListFormat> =
    let parser(result: ArgumentResult) =
      match result.Tokens |> Seq.tryHead with
      | Some token ->
        match token.Value with
        | "table" -> ListFormat.HumanReadable
        | "text" -> ListFormat.TextOnly
        | _ -> ListFormat.HumanReadable
      | None -> ListFormat.HumanReadable

    Option<ListFormat>(
      "--list-format",
      CustomParser = parser,
      Description = "The chosen format to display the existing templates",
      Required = false
    )
      .AcceptOnlyFromAmong("table", "text")

[<Class; Sealed>]
type PerlaArguments =

  static member Properties: Argument<string array> =
    let parser(result: ArgumentResult) =
      result.Tokens
      |> Seq.map(fun token -> token.Value)
      |> Seq.distinct
      |> Seq.toArray

    Argument<string array>(
      "properties",
      CustomParser = parser,
      Description =
        "A property, properties or json path-like string names to describe",
      Arity = ArgumentArity.ZeroOrMore
    )

[<RequireQualifiedAccess>]
module SharedInputs =

  let source: ActionInput<Perla.PkgManager.DownloadProvider voption> =
    PerlaOptions.PackageSource |> Input.ofOption

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
    |> Input.ofArgument

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


  let package: ActionInput<string> =
    argument "package" |> description "Name of the JS Package"

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

  let displayMode: ActionInput<ListFormat> =
    PerlaOptions.DisplayMode |> Input.ofOption

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
  let browsers: ActionInput<Browser Set> =
    PerlaOptions.Browsers |> Input.ofOption

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

    let handleCommand(context: ActionContext, enablePreview: bool option) =

      let options = {
        enablePreview = defaultArg enablePreview false
      }

      Handlers.runBuild container options context.CancellationToken

    command "build" {
      description "Builds the SPA application for distribution"
      addAlias "b"

      inputs(Input.context, BuildInputs.preview)

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
      let options = { port = port; host = host; ssl = ssl }
      Handlers.runServe container options (context.CancellationToken)

    let desc =
      "Starts the development server and if fable projects are present it also takes care of it."

    command "serve" {
      description desc
      addAliases [ "s"; "start" ]

      inputs(Input.context, ServeInputs.port, ServeInputs.host, ServeInputs.ssl)

      setAction handleCommand
    }

  let RemovePackage(container: AppContainer) =

    let handleCommand
      (ctx: ActionContext, package: string, alias: string option)
      =
      let options = { package = package }
      Handlers.runRemovePackage container options ctx.CancellationToken

    command "remove" {
      description "Removes a package from the project dependencies"

      inputs(Input.context, PackageInputs.package, PackageInputs.alias)
      setAction handleCommand
    }

  let Install(container: AppContainer) =
    let handleCommand
      (
        ctx: ActionContext,
        offline: bool option,
        source: PkgManager.DownloadProvider voption
      ) =
      let options = {
        offline = defaultArg offline false
        source = source
      }

      Handlers.runInstall container options ctx.CancellationToken

    command "install" {
      description "Installs the project dependencies from the perla.json file"
      addAlias "i"
      inputs(Input.context, PackageInputs.offline, SharedInputs.source)
      setAction handleCommand
    }

  let AddPackage(container: AppContainer) =

    let handleCommand
      (ctx: ActionContext, package: string, version: string option)
      =
      let options = { package = package; version = version }

      Handlers.runAddPackage container options ctx.CancellationToken

    command "add" {
      description "Adds a package to the project dependencies"

      addAlias "install"

      inputs(Input.context, PackageInputs.package, PackageInputs.version)

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

      inputs(Input.context, PackageInputs.showAsNpm)
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

      Handlers.runTemplate container options ctx.CancellationToken

    let template = command "templates" {
      addAlias "t"

      description
        "Handles Template Repository operations such as list, add, update, and remove templates"

      inputs(
        Input.context,
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
      let options = {
        projectName = name
        byId = byId
        byShortName = byShortName
        skipPrompts = skipPrompts
      }

      Handlers.runNew container options ctx.CancellationToken

    command "new" {
      addAliases [ "n"; "create"; "generate" ]

      description
        "Creates a new project based on the selected template if it exists"

      inputs(
        Input.context,
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
        Input.context,
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
        Input.context,
        DescribeInputs.perlaProperties,
        DescribeInputs.describeCurrent
      )

      setAction handleCommand

    }
