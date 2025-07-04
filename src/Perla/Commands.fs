namespace Perla.Commands

open System.Threading

open System.CommandLine
open System.CommandLine.Invocation
open System.CommandLine.Parsing

open FSharp.SystemCommandLine

open FsToolkit.ErrorHandling
open FSharp.UMX

open Perla
open Perla.PackageManager.Types
open Perla.Types
open Perla.Handlers


[<Class; Sealed>]
type PerlaOptions =

  static member BoolOption
    (aliases: string seq, description: string)
    : Option<bool option> =
    let parser(result: ArgumentResult) =
      let defaultValue = Some true

      let withToken =
        result.Tokens
        |> Seq.tryHead
        |> Option.map(fun value ->
          match value.Value.Trim().ToLowerInvariant() with
          | "true" -> true
          | "false" -> false
          | _ -> false)

      Option.orElse defaultValue withToken



    Option<bool option>(
      aliases |> Seq.toArray,
      parseArgument = parser,
      description = description,
      Arity = ArgumentArity.ZeroOrOne
    )

  static member PackageSource: Option<PkgManager.DownloadProvider voption> =
    let parser(result: ArgumentResult) =
      match result.Tokens |> Seq.tryHead with
      | Some token ->
        PkgManager.DownloadProvider.fromString token.Value |> ValueSome
      | None -> ValueNone

    let opt =
      Option<PkgManager.DownloadProvider voption>(
        [| "--source"; "-s" |],
        parseArgument = parser,
        description = "Version of the package to install",
        IsRequired = false
      )

    opt.FromAmong(
      [|
        "jspm"
        "skypack"
        "unpkg"
        "jsdelivr"
        "esm.sh"
        "jspm.system"
        "jspm#system"
      |]
    )
    |> ignore

    opt

  static member Browsers: Option<Browser array> =
    let parser(result: ArgumentResult) =
      result.Tokens
      |> Seq.map(fun token -> token.Value |> Browser.FromString)
      |> Seq.distinct
      |> Seq.toArray

    let opt =
      Option<Browser array>(
        [| "--browsers"; "-b" |],
        parseArgument = parser,
        description = "Version of the package to install",
        Arity = ArgumentArity.ZeroOrMore,
        AllowMultipleArgumentsPerToken = true
      )

    opt.FromAmong([| "chromium"; "firefox"; "webkit"; "edge"; "chrome" |])
    |> ignore

    opt

  static member DisplayMode: Option<ListFormat> =
    let parser(result: ArgumentResult) =
      match result.Tokens |> Seq.tryHead with
      | Some token ->
        match token.Value with
        | "table" -> ListFormat.HumanReadable
        | "text" -> ListFormat.TextOnly
        | _ -> ListFormat.HumanReadable
      | None -> ListFormat.HumanReadable

    let opt =
      Option<ListFormat>(
        [| "--list"; "-ls" |],
        parseArgument = parser,
        description = "The chosen format to display the existing templates",
        IsRequired = false
      )

    opt.FromAmong([| "table"; "text" |]) |> ignore

    opt

[<Class; Sealed>]
type PerlaArguments =

  static member ArgStringMaybe
    (name: string, ?description: string)
    : Argument<string option> =
    let parser(result: ArgumentResult) =
      result.Tokens |> Seq.tryHead |> Option.map(fun value -> value.Value)

    Argument<string option>(
      name,
      parse = parser,
      ?description = description,
      Arity = ArgumentArity.ZeroOrOne
    )

  static member Properties: Argument<string array> =
    let parser(result: ArgumentResult) =
      result.Tokens
      |> Seq.map(fun token -> token.Value)
      |> Seq.distinct
      |> Seq.toArray

    Argument<string array>(
      "properties",
      parser,
      description =
        "A property, properties or json path-like string names to describe",
      Arity = ArgumentArity.ZeroOrMore
    )

[<RequireQualifiedAccess>]
module SharedInputs =

  let source: HandlerInput<Perla.PkgManager.DownloadProvider voption> =
    PerlaOptions.PackageSource |> Input.OfOption

[<RequireQualifiedAccess>]
module DescribeInputs =
  let perlaProperties: HandlerInput<string[] option> =
    Argument<string[] option>(
      "properties",
      (fun (result: ArgumentResult) ->
        match result.Tokens |> Seq.toArray with
        | [||] -> None
        | others -> Some(others |> Array.map(fun token -> token.Value))),
      Description =
        "A property, properties or json path-like string names to describe",
      Arity = ArgumentArity.ZeroOrMore
    )
    |> HandlerInput.OfArgument

  let describeCurrent: HandlerInput<bool> =
    Input.Option(
      [ "--current"; "-c" ],
      false,
      "Take my current perla.json file and print my current configuration"
    )

[<RequireQualifiedAccess>]
module BuildInputs =
  let enablePreloads: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "-epl"; "--enable-preload-links" ],
      "enable adding modulepreload links in the final build"
    )

  let rebuildImportMap: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "-rim"; "--rebuild-importmap" ],
      "discards the current import map (and custom resolutions)
        and generates a new one based on the dependencies listed in the config file."
    )

  let preview: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "-prev"; "--preview" ],
      "discards the current import map (and custom resolutions)
        and generates a new one based on the dependencies listed in the config file."
    )

[<RequireQualifiedAccess>]
module SetupInputs =
  let installTemplates: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--templates"; "-t" ],
      "Install Default templates (defaults to true)"
    )

  let skipPrompts: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--skip"; "-s"; "-y" ],
      "Skip Prompts and accept all defaults"
    )

[<RequireQualifiedAccess>]
module PackageInputs =
  let package: HandlerInput<string> =
    Input.Argument("package", "Name of the JS Package")

  let import: HandlerInput<string> =
    Input.Argument(
      "import",
      "Name to assign to this import e.g 'app/buttons' 'lodashv3'"
    )

  let resolution: HandlerInput<string option> =
    PerlaArguments.ArgStringMaybe(
      "resolution",
      "URL, or path (absolute or relative) to your import's actual source."
    )
    |> Input.OfArgument

  let addOrUpdate: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--add"; "--update"; "-u"; "-a" ],
      "Attempts to add or update an import in paths configuration."
    )

  let removeResolution: HandlerInput<bool> =
    Input.Option(
      [ "--remove-resolution"; "-r" ],
      "Remove the resolution from the config file"
    )

  let currentPage: HandlerInput<int option> =
    Input.OptionMaybe(
      [| "--page"; "-p" |],
      "change the page number in the search results"
    )

  let alias: HandlerInput<string option> =
    Input.OptionMaybe(
      [ "--alias"; "-a" ],
      "the alias of the package if you added one"
    )

  let version: HandlerInput<string option> =
    Input.OptionMaybe(
      [ "--version"; "-v" ],
      "The version of the package you want to add"
    )

  let showAsNpm: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--npm"; "--as-package-json"; "-j" ],
      "Show the packages similar to npm's package.json"
    )

[<RequireQualifiedAccess>]
module TemplateInputs =
  let repositoryName: HandlerInput<string option> =
    Input.ArgumentMaybe(
      "templateRepositoryName",
      "The User/repository name combination"
    )

  let addTemplate: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--add"; "-a" ],
      "Adds the template repository to Perla"
    )

  let updateTemplate: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--update"; "-u" ],
      "If it exists, updates the template repository for Perla"
    )

  let removeTemplate: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--remove"; "-r" ],
      "If it exists, removes the template repository for Perla"
    )

  let displayMode: HandlerInput<ListFormat> =
    PerlaOptions.DisplayMode |> Input.OfOption

[<RequireQualifiedAccess>]
module ProjectInputs =

  let projectName: HandlerInput<string> =
    Input.Argument("name", "Name of the new project")

  let byId: HandlerInput<string option> =
    Input.OptionMaybe(
      [ "-id"; "--group-id" ],
      "fully.qualified.name of the template, e.g. perla.templates.vanilla.js"
    )

  let byShortName: HandlerInput<string option> =
    Input.OptionMaybe(
      [ "-t"; "--template" ],
      "shortname of the template, e.g. ff"
    )

[<RequireQualifiedAccess>]
module TestingInputs =
  let browsers: HandlerInput<Browser array> =
    PerlaOptions.Browsers |> Input.OfOption

  let files: HandlerInput<string array> =
    Input.Option(
      [ "--tests"; "-t" ],
      [||],
      "Specify a glob of tests to run. e.g '**/featureA/*.test.js' or 'tests/my-test.test.js'"
    )

  let skips: HandlerInput<string array> =
    Input.Option(
      [ "--skip"; "-s" ],
      [||],
      "Specify a glob of tests to skip. e.g '**/featureA/*.test.js' or 'tests/my-test.test.js'"
    )


  let headless: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--headless"; "-hl" ],
      "Turn on or off the Headless mode and open the browser (useful for debugging tests)"
    )

  let watch: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--watch"; "-w" ],
      "Start the server and keep watching for file changes"
    )

  let sequential: HandlerInput<bool option> =
    Input.OptionMaybe(
      [ "--browser-sequential"; "-bs" ],
      "Run each browser's test suite in sequence, rather than parallel"
    )

[<RequireQualifiedAccess>]
module ServeInputs =
  let port: HandlerInput<int option> =
    Input.OptionMaybe([ "--port"; "-p" ], "Port where the application starts")

  let host: HandlerInput<string option> =
    Input.OptionMaybe(
      [ "--host" ],
      "network ip address where the application will run"
    )

  let ssl: HandlerInput<bool option> =
    Input.OptionMaybe([ "--ssl" ], "Run dev server with SSL")

[<RequireQualifiedAccess>]
module Commands =
  type HandlerInput<'T> with

    member this.GetValue(ctx: CommandResult) : 'T =
      match this.Source with
      | ParsedOption o -> o :?> Option<'T> |> ctx.GetValueForOption
      | ParsedArgument a -> a :?> Argument<'T> |> ctx.GetValueForArgument
      | Context -> failwith "Unable to get a result from context"

  let Build =

    let handleCommand
      (
        context: InvocationContext,
        enablePreloads: bool option,
        rebuildImportMap: bool option,
        enablePreview: bool option
      ) =

      let options = {
        enablePreloads = defaultArg enablePreloads true
        rebuildImportMap = defaultArg rebuildImportMap false
        enablePreview = defaultArg enablePreview false
      }

      Handlers.runBuild options (context.GetCancellationToken())

    command "build" {
      description "Builds the SPA application for distribution"
      addAlias "b"

      inputs(
        Input.Context(),
        BuildInputs.enablePreloads,
        BuildInputs.rebuildImportMap,
        BuildInputs.preview
      )

      setHandler handleCommand
    }

  let Serve =
    let handleCommand
      (
        context: InvocationContext,
        port: int option,
        host: string option,
        ssl: bool option
      ) =
      let options = { port = port; host = host; ssl = ssl }
      Handlers.runServe options (context.GetCancellationToken())

    let desc =
      "Starts the development server and if fable projects are present it also takes care of it."

    command "serve" {
      description desc
      addAliases [ "s"; "start" ]

      inputs(
        Input.Context(),
        ServeInputs.port,
        ServeInputs.host,
        ServeInputs.ssl
      )

      setHandler handleCommand
    }

  let Setup =
    let handleCommand
      (
        ctx: InvocationContext,
        installTemplates: bool option,
        skipPrompts: bool option
      ) =
      let options = {
        installTemplates = defaultArg installTemplates true
        skipPrompts = defaultArg skipPrompts false
      }

      Handlers.runSetup options (ctx.GetCancellationToken())


    command "setup" {
      description "Initialized a given directory or perla itself"

      inputs(
        Input.Context(),
        SetupInputs.installTemplates,
        SetupInputs.skipPrompts
      )

      setHandler handleCommand
    }

  let RemovePackage =

    let handleCommand
      (ctx: InvocationContext, package: string, alias: string option)
      =
      let options = { package = package; alias = alias }
      Handlers.runRemovePackage options (ctx.GetCancellationToken())

    command "remove" {
      description "removes a package from the "

      inputs(Input.Context(), PackageInputs.package, PackageInputs.alias)
      setHandler handleCommand
    }

  let AddPackage =

    let handleCommand
      (
        ctx: InvocationContext,
        source: PkgManager.DownloadProvider voption,
        package: string,
        version: string option,
        alias: string option
      ) =
      let options = {
        package = package
        version = version
        source = source |> Option.ofValueOption
        alias = alias
      }

      Handlers.runAddPackage options (ctx.GetCancellationToken())

    command "add" {
      description
        "Shows information about a package if the name matches an existing one"

      addAlias "install"

      inputs(
        Input.Context(),
        SharedInputs.source,
        PackageInputs.package,
        PackageInputs.version,
        PackageInputs.alias
      )

      setHandler handleCommand
    }

  let ListPackages =

    let handleCommand(ctx: InvocationContext, asNpm: bool option) =
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

      Handlers.runListPackages args (ctx.GetCancellationToken())

    command "list" {
      addAlias "ls"

      description
        "Lists the current dependencies in a table or an npm style json string"

      inputs(Input.Context(), PackageInputs.showAsNpm)
      setHandler handleCommand
    }

  let Template =

    let handleCommand
      (
        ctx: InvocationContext,
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

      Handlers.runTemplate options (ctx.GetCancellationToken())

    let template = command "templates" {
      addAlias "t"

      description
        "Handles Template Repository operations such as list, add, update, and remove templates"

      inputs(
        Input.Context(),
        TemplateInputs.repositoryName,
        TemplateInputs.addTemplate,
        TemplateInputs.updateTemplate,
        TemplateInputs.removeTemplate,
        TemplateInputs.displayMode
      )

      setHandler handleCommand
    }

    template

  let NewProject =

    let handleCommand
      (
        ctx: InvocationContext,
        name: string,
        byId: string option,
        byShortName: string option
      ) =
      let options = {
        projectName = name
        byId = byId
        byShortName = byShortName
      }

      Handlers.runNew options (ctx.GetCancellationToken())

    command "new" {
      addAliases [ "n"; "create"; "generate" ]

      description
        "Creates a new project based on the selected template if it exists"

      inputs(
        Input.Context(),
        ProjectInputs.projectName,
        ProjectInputs.byId,
        ProjectInputs.byShortName
      )

      setHandler handleCommand
    }

  let Test =

    let handleCommand
      (
        ctx: InvocationContext,
        browsers: Browser array,
        files: string array,
        skips: string array,
        headless: bool option,
        watch: bool option,
        sequential: bool option
      ) =
      let options = {
        browsers = if Array.isEmpty browsers then None else Some browsers
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

      Handlers.runTesting options (ctx.GetCancellationToken())

    let cmd = command "test" {
      description "Runs client side tests in a headless browser"

      inputs(
        Input.Context(),
        TestingInputs.browsers,
        TestingInputs.files,
        TestingInputs.skips,
        TestingInputs.headless,
        TestingInputs.watch,
        TestingInputs.sequential
      )

      setHandler handleCommand
    }

    cmd.IsHidden <- true
    cmd

  let Describe =

    let handleCommand
      (ctx: InvocationContext, properties: string[] option, current: bool)
      =
      let args = {
        properties = properties
        current = current
      }

      Handlers.runDescribePerla args (ctx.GetCancellationToken())

    command "describe" {
      addAlias "ds"

      description
        "Describes the perla.json file or it's properties as requested"

      inputs(
        Input.Context(),
        DescribeInputs.perlaProperties,
        DescribeInputs.describeCurrent
      )

      setHandler handleCommand
    }
