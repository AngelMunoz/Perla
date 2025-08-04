namespace Perla.Handlers

open System
open System.IO
open System.Threading
open System.Threading.Tasks


open Microsoft.Extensions.Logging

open AngleSharp
open AngleSharp.Html.Parser
open Spectre.Console

open FSharp.Control
open FSharp.Data.Adaptive
open IcedTasks

open FSharp.UMX
open FsToolkit.ErrorHandling


open System.Collections.Generic
open Perla
open Perla.Units
open Perla.Types
open Perla.Json
open Perla.Database
open Perla.Fable
open Perla.Build
open Perla.Logger
open Perla.PkgManager
open Perla.PkgManager.PkgManager
open Perla.SuaveService
open Perla.Testing

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

type InstallOptions = {
  offline: bool option
  source: PkgManager.DownloadProvider voption
}

type ListPackagesOptions = { format: ListFormat }

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

[<Struct>]
type PathOperation =
  | AddOrUpdate of
    addImport: string<BareImport> *
    addPath: string<ResolutionUrl>
  | Remove of removeImport: string

type PathsOptions = { operation: PathOperation }

[<RequireQualifiedAccess>]
module RunNew =
  let inline tplConverter<'T
    when 'T: (member Name: string)
    and 'T: (member Description: string option)
    and 'T: (member ShortName: string)>
    (tpl: 'T)
    =
    let description =
      tpl.Description |> Option.defaultValue "No description provided"

    $"{tpl.Name} ({tpl.ShortName}) - {description}"

  let findTemplateItemByNameOrId
    (id: string option, name: string option)
    (tplList: TemplateItem seq)
    =
    tplList
    |> Seq.tryPick(fun tpl ->
      let byId =
        id
        |> Option.bind(fun id ->
          if $"{tpl.Group}.{tpl.Id}" = id then Some tpl else None)

      let byShortName =
        name
        |> Option.bind(fun shortName ->
          if tpl.ShortName = shortName then Some tpl else None)

      byId |> Option.orElse byShortName)

  let findDecodedTemplateByNameOrId
    (id: string option, name: string option)
    (tplList: DecodedTemplateConfigItem seq)
    =
    tplList
    |> Seq.tryPick(fun tpl ->
      let byId =
        id
        |> Option.bind(fun id ->
          if $"perla.templates.{tpl.Id}" = id then Some tpl else None)

      let byShortName =
        name
        |> Option.bind(fun shortName ->
          if tpl.ShortName = shortName then Some tpl else None)

      byId |> Option.orElse byShortName)


  let writeFoundTemplate
    (FsManager fsManager, tpl: TemplateItem, targetPath: string<SystemPath>)
    =
    let tplPath = DirectoryInfo(UMX.untag tpl.FullPath)
    fsManager.CopyFiles(tplPath, targetPath)

  let writeFoundDecodedTemplate
    (FsManager fsManager & Directories directories)
    (
      _: DecodedTemplateConfiguration,
      tpl: DecodedTemplateConfigItem,
      targetPath: string<SystemPath>
    ) =
    let tplPath =
      Path.Combine(UMX.untag directories.OfflineTemplates, UMX.untag tpl.Path)
      |> DirectoryInfo

    fsManager.CopyFiles(tplPath, targetPath)

  let logProjectCreationSuccess (logger: ILogger) (targetPath: DirectoryInfo) =
    logger.LogInformation(
      "Project created successfully at {path}",
      targetPath.FullName
    )

    logger.LogInformation(
      "cd {path} and run 'perla serve' to start the development server.",
      targetPath.FullName
    )

  let getTargetPath (directories: PerlaDirectories) (projectName: string) =
    Path.Combine(UMX.untag directories.CurrentWorkingDirectory, projectName)
    |> DirectoryInfo

  let handleTemplateNotFound(logger: ILogger) =
    logger.LogWarning(
      "No templates found in the Default Templates, please add a template to the perla database or the Default Templates."
    )

    logger.LogInformation(
      "You can add templates with 'perla template add <repository>' or 'perla template add <repository>:<branch>'."
    )

  let handleTemplateNotFoundById(logger: ILogger) =
    logger.LogWarning(
      "No templates found with the provided id or short name, please try again."
    )

  type TemplateChoice<'T> =
    | FoundById of 'T
    | PromptUser of 'T seq

  let resolveTemplateChoice
    (byId: string option)
    (byShortName: string option)
    (templates: 'T seq)
    (finder: string option * string option -> 'T seq -> 'T option)
    =
    if byId.IsSome || byShortName.IsSome then
      match finder (byId, byShortName) templates with
      | Some found -> FoundById found
      | None -> PromptUser Seq.empty
    else
      PromptUser templates

[<RequireQualifiedAccess>]
module Handlers =
  open Microsoft.Playwright

  let runNew (container: AppContainer) (options: ProjectOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    let (Logger logger) = container
    let tplList = container.TemplateService.ListTemplateItems()

    let handleOfflineTemplates() = cancellableTask {
      logger.LogWarning
        "No templates found in the perla database, searching in the default Default Templates."

      let! otConfig = container.FsManager.ResolveOfflineTemplatesConfig()
      let tplList = otConfig.templates

      let targetPath =
        RunNew.getTargetPath container.Directories options.projectName

      match
        RunNew.resolveTemplateChoice
          options.byId
          options.byShortName
          tplList
          RunNew.findDecodedTemplateByNameOrId
      with
      | RunNew.FoundById found ->
        logger.LogInformation(
          "Found template '{name}' with short name '{shortName}'",
          found.Name,
          found.ShortName
        )

        RunNew.writeFoundDecodedTemplate
          container
          (otConfig, found, UMX.tag targetPath.FullName)

        RunNew.logProjectCreationSuccess logger targetPath
        return 0
      | RunNew.PromptUser templates when not(Seq.isEmpty templates) ->
        let prompt =
          SelectionPrompt()
            .Title("Select a template to create a new project:")
            .EnableSearch()
            .AddChoices(templates)
            .UseConverter(fun (tpl: DecodedTemplateConfigItem) ->
              RunNew.tplConverter tpl)

        let! selected = AnsiConsole.PromptAsync(prompt, token)

        RunNew.writeFoundDecodedTemplate
          container
          (otConfig, selected, UMX.tag targetPath.FullName)

        RunNew.logProjectCreationSuccess logger targetPath
        return 0
      | RunNew.PromptUser _ ->
        RunNew.handleTemplateNotFound logger
        return 1
    }

    let handleDatabaseTemplates(templates: TemplateItem list) = cancellableTask {
      let targetPath =
        RunNew.getTargetPath container.Directories options.projectName

      match
        RunNew.resolveTemplateChoice
          options.byId
          options.byShortName
          templates
          RunNew.findTemplateItemByNameOrId
      with
      | RunNew.FoundById found ->
        logger.LogInformation(
          "Found template '{name}' with short name '{shortName}'",
          found.Name,
          found.ShortName
        )

        RunNew.writeFoundTemplate(container, found, UMX.tag targetPath.FullName)
        RunNew.logProjectCreationSuccess logger targetPath
        return 0
      | RunNew.PromptUser templates when not(Seq.isEmpty templates) ->
        let prompt =
          SelectionPrompt()
            .Title("Select a template to create a new project:")
            .EnableSearch()
            .AddChoices(templates)
            .UseConverter(fun (tpl: TemplateItem) -> RunNew.tplConverter tpl)


        let! selected = AnsiConsole.PromptAsync(prompt, token)

        RunNew.writeFoundTemplate(
          container,
          selected,
          UMX.tag targetPath.FullName
        )

        RunNew.logProjectCreationSuccess logger targetPath
        return 0
      | RunNew.PromptUser _ ->
        RunNew.handleTemplateNotFoundById logger
        return 1
    }

    match tplList with
    | [] -> return! handleOfflineTemplates()
    | templates -> return! handleDatabaseTemplates templates
  }

  let runTemplate
    (container: AppContainer)
    (options: TemplateRepositoryOptions)
    =
    cancellableTask {
      let (Logger logger) = container
      let (TemplateService templateService) = container

      let template =
        match options.fullRepositoryName with
        | FullRepositoryName(user, repo, branch) ->
          TemplateSearchKind.FullName(user, repo)
          |> templateService.FindOne
          |> ValueOption.ofOption
        | _ -> ValueNone

      let updateRepo() = cancellableTask {
        match template with
        | ValueSome template ->
          logger.LogInformation
            $"Template {template.ToFullNameWithBranch} already exists."

          let! updated = templateService.Update(template)

          if updated then
            logger.LogInformation "Template updated successfully."
            return 0
          else
            logger.LogError "Failed to update template."
            return 1
        | ValueNone ->
          logger.LogWarning "We were unable to parse the repository name."

          logger.LogInformation(
            "please ensure that the repository name is in the format: username/repository:branch"
          )

          return 1
      }

      match options.operation with
      | RunTemplateOperation.List listFormat ->
        // List the templates using the service and return
        let templates = templateService.ListTemplateItems()

        // Also get Default Templates
        let! otConfig = container.FsManager.ResolveOfflineTemplatesConfig()
        let offlineTemplates = otConfig.templates


        match listFormat with
        | ListFormat.HumanReadable ->
          // Use a single table for both lists, with a divider row
          let table =
            Table()
              .AddColumn("Name")
              .AddColumn("Short Name")
              .AddColumn("Description")
              .Title("Templates")

          // Downloaded templates
          let templatesList = List.ofSeq templates
          let offlineTemplatesList = List.ofSeq offlineTemplates

          for template in templatesList do
            let description =
              template.Description
              |> Option.defaultValue "No description provided"

            table.AddRow(template.Name, template.ShortName, description)
            |> ignore

          // Divider row
          if
            not(List.isEmpty templatesList)
            && not(List.isEmpty offlineTemplatesList)
          then
            table.AddRow(
              Markup("[grey]───────────────[/]"),
              Markup("[grey]───────────────[/]"),
              Markup("[grey]───────────────[/]")
            )
            |> ignore

          // Offline templates
          for tpl in offlineTemplatesList do
            let description =
              tpl.Description |> Option.defaultValue "No description provided"

            table.AddRow(tpl.Name, tpl.ShortName, description) |> ignore

          AnsiConsole.Write(table)

        | ListFormat.TextOnly ->
          logger.LogInformation("Downloaded Templates:")

          for template in templates do
            let description =
              template.Description
              |> Option.defaultValue "No description provided"

            logger.LogInformation
              $"{template.Name} ({template.ShortName}) - {description}"

          logger.LogInformation("")
          logger.LogInformation("Default Templates:")

          for tpl in offlineTemplates do
            let description =
              tpl.Description |> Option.defaultValue "No description provided"

            logger.LogInformation(
              $"{tpl.Name} ({tpl.ShortName}) - {description}"
            )

        return 0

      | RunTemplateOperation.Add ->
        match options.fullRepositoryName with
        | FullRepositoryName(user, repo, branch) ->
          try
            let! id = templateService.Add(user, UMX.tag repo, UMX.tag branch)

            logger.LogInformation $"Template added successfully with id: {id}"
            return 0
          with ex ->
            logger.LogError(ex, "Failed to add template: {Error}", ex.Message)
            return 1
        | _ ->
          logger.LogWarning "We were unable to parse the repository name."

          logger.LogInformation(
            "please ensure that the repository name is in the format: username/repository:branch"
          )

          return 1

      | RunTemplateOperation.Update -> return! updateRepo()

      | RunTemplateOperation.Update
      | RunTemplateOperation.Add when template.IsSome -> return! updateRepo()

      | RunTemplateOperation.Remove ->
        match template with
        | ValueSome template ->
          logger.LogInformation
            $"Removing template '{template.ToFullNameWithBranch}'..."

          let result =
            TemplateSearchKind.Id(template._id) |> templateService.Delete

          if result then
            logger.LogInformation "Template removed successfully."
            return 0
          else
            logger.LogError "Failed to remove template."
            return 1
        | ValueNone ->
          logger.LogWarning "We were unable to parse the repository name."

          logger.LogInformation(
            "please ensure that the repository name is in the format: username/repository:branch"
          )

          return 1
    }

  // Helper to adaptively add fable outDir mount if needed
  let withFableOutDirMount(config: PerlaConfig aval) = adaptive {
    let! config = config

    let outDirOpt = option {
      let! fable = config.fable
      let! outDir = fable.outDir |> Option.map UMX.untag

      let relPath =
        if outDir.StartsWith("./") then outDir.Substring(1)
        elif outDir.StartsWith(".\\") then outDir.Substring(1)
        elif outDir.StartsWith("/") then outDir
        else "/" + outDir

      let serverPath = relPath.Replace("\\", "/")

      let alreadyMounted =
        config.mountDirectories
        |> Map.exists(fun k v ->
          UMX.untag v = outDir || UMX.untag k = serverPath)

      if alreadyMounted then
        return! None

      return UMX.tag<ServerUrl> serverPath, UMX.tag<UserPath> outDir
    }

    match outDirOpt with
    | Some(serverPath, outDir) ->

      return {
        config with
            mountDirectories =
              config.mountDirectories |> Map.add serverPath outDir
      }
    | None -> return config
  }

  // Helper to adaptively add /node_modules mount if needed
  let withNodeModules(config: PerlaConfig aval) = adaptive {
    let! config = config

    let hasKey =
      config.mountDirectories
      |> Map.containsKey(UMX.tag<ServerUrl> "/node_modules")

    if config.useLocalPkgs && not hasKey then
      return {
        config with
            mountDirectories =
              config.mountDirectories
              |> Map.add
                (UMX.tag<ServerUrl> "/node_modules")
                (UMX.tag<UserPath> "./node_modules")
      }
    else
      return config
  }

  let withDefaultMounts = withNodeModules >> withFableOutDirMount

  let runBuild (container: AppContainer) (options: BuildOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()

    let config = container.Configuration.PerlaConfig |> withDefaultMounts

    let buildOutDir = config |> AVal.map _.build.outDir |> AVal.force

    // Step 1: Fable build (if configured)
    do! container.BuildService.RunFable(config)

    // Step 2: Clean output directory
    container.BuildService.CleanOutput(config)

    // Step 3: Prepare VFS output directory
    let vfsOutputDir =
      ".tmp/perla/vfs" |> Path.GetFullPath |> UMX.tag<SystemPath>

    Directory.CreateDirectory(UMX.untag vfsOutputDir) |> ignore

    // Step 4: Load plugins
    container.BuildService.LoadPlugins(config, vfsOutputDir)

    // Step 5: Load virtual file system
    do! container.BuildService.LoadVfs(config)

    // Step 6: Copy VFS to disk
    let! tempDir = container.BuildService.CopyVfsToDisk vfsOutputDir

    container.Logger.LogInformation(
      "Copying Processed files to {tempDirectory}",
      tempDir
    )

    // Step 7: Emit env file if needed
    container.BuildService.EmitEnvFile(config, buildOutDir)

    // Step 8: Prepare index and import map
    use browserCtx = new BrowsingContext()
    let index = container.FsManager.ResolveIndex |> AVal.force

    let document =
      (browserCtx.GetService<IHtmlParser>() |> nonNull).ParseDocument index

    let importMapA =
      container.FsManager.ResolveImportMap |> ImportMaps.withPathsA config

    let resolutionA = ImportMaps.resolveForBuildA config importMapA

    let missing =
      adaptive {
        let! config = config
        let! map = importMapA
        return Build.getMissingLocalDependencies config map
      }
      |> AVal.force

    let {
          importMap = map
          externals = externals
        } =
      resolutionA |> AVal.force

    // Pre-build check for missing local dependencies
    if (config |> AVal.force).useLocalPkgs && missing.Length > 0 then
      container.Logger.LogError(
        "We've found undeclared dependencies: {pkgs}.",
        String.concat ", " missing
      )

      container.Logger.LogError
        "Your project is currently implicitly using these undeclared dependencies, to proceed with the build add them to your dependencies."

      container.Logger.LogError(
        "{cmd}",
        "perla add " + String.concat " " missing
      )

      return 1
    else
      let cssPaths, jsBundleEntrypoints, jsStandalonePaths =
        Build.EntryPoints document

      // Step 9: Run esbuild or move/copy output
      let! esbuildOutput =
        container.BuildService.RunEsbuild(
          config,
          tempDir,
          cssPaths,
          [ yield! jsBundleEntrypoints; yield! jsStandalonePaths ],
          externals |> Seq.map UMX.untag |> Seq.toList
        )

      container.BuildService.MoveOrCopyOutput(
        config,
        tempDir,
        esbuildOutput.outputDir
      )

      // Step 10: Write index.html
      let jsPaths = seq {
        yield! jsStandalonePaths
        yield! jsBundleEntrypoints
      }

      do!
        container.BuildService.WriteIndex(
          config,
          document,
          map,
          jsPaths,
          cssPaths,
          esbuildOutput.cssFiles
        )

      // // cleanup temporary directory
      try
        Directory.Delete(UMX.untag ".tmp/perla", true) |> ignore
      with ex ->
        container.Logger.LogWarning(
          "Failed to delete temporary directory {dir}: {error}",
          UMX.untag ".tmp/perla",
          ex.Message
        )

      // Step 11: Start preview server if requested
      if options.enablePreview then
        container.Logger.LogInformation
          "Starting a preview server for the build"

        SuaveServer.startStaticServer
          {
            Logger = container.Logger
            VirtualFileSystem = container.VirtualFileSystem
            Config = config
            FsManager = container.FsManager
          }
          token

        return 0
      else
        return 0
  }

  let runServe (container: AppContainer) (options: ServeOptions) = cancellableTask {
    let! cancellationToken = CancellableTask.getCancellationToken()

    let configA =
      adaptive {
        let! config = container.Configuration.PerlaConfig

        return {
          config with
              devServer = {
                config.devServer with
                    port = defaultArg options.port config.devServer.port
                    host = defaultArg options.host config.devServer.host
                    useSSL = defaultArg options.ssl config.devServer.useSSL
              }
              esbuild.minify = false
        }
      }
      |> withDefaultMounts

    let config = configA |> AVal.force
    let fableConfig = config.fable

    let fableFirstRunTcs = TaskCompletionSource()

    match fableConfig with
    | Some config ->
      container.Logger.LogInformation
        "Fable configuration found. Running Fable service."

      let work = asyncEx {
        let! token = Async.CancellationToken
        let events = container.FableService.Monitor(config, token)

        for event in events do
          match event with
          | FableEvent.Log _ -> ()
          | FableEvent.ErrLog _ -> ()
          | FableEvent.WaitingForChanges ->
            if not fableFirstRunTcs.Task.IsCompleted then
              fableFirstRunTcs.TrySetResult() |> ignore
              container.Logger.LogInformation "Fable service is ready."
            else
              container.Logger.LogInformation
                "Fable service is waiting for changes."
      }

      Async.Start(work, cancellationToken)
    | None ->
      container.Logger.LogWarning
        "Fable configuration not found. Skipping Fable service."

      fableFirstRunTcs.TrySetResult() |> ignore

    // Wait for either the Fable first run to complete or cancellation
    do! fableFirstRunTcs.Task

    if cancellationToken.IsCancellationRequested then
      container.Logger.LogInformation(
        "Fable service cancelled before starting."
      )

      return 0
    else

      let plugins = container.FsManager.ResolvePluginPaths()

      let defaultPlugins = seq {
        if
          config.plugins
          |> List.exists(fun p ->
            p.Equals(
              Constants.PerlaEsbuildPluginName,
              StringComparison.InvariantCultureIgnoreCase
            ))
        then
          container.EsbuildService.GetPlugin config.esbuild
      }

      container.ExtensibilityService.LoadPlugins(plugins, defaultPlugins)
      |> Result.teeError(fun err ->
        container.Logger.LogError("Failed to load plugins: {error}", err))
      |> Result.ignore
      |> Result.ignoreError

      let mountedDirectories = config.mountDirectories

      do!
        container.Logger.Spinner(
          "Mounting Virtual File System",
          container.VirtualFileSystem.Load mountedDirectories
        )

      SuaveServer.startServer
        (SuaveContext {
          Logger = container.Logger
          VirtualFileSystem = container.VirtualFileSystem
          Config = configA
          FsManager = container.FsManager
        })
        cancellationToken

      return 0
  }

  let mountTestingDirectories(config: PerlaConfig aval) = adaptive {
    let! config = config

    let found =
      config.mountDirectories |> Map.tryFind(UMX.tag<ServerUrl> "/tests")

    match found with
    | Some _ -> return config
    | None ->
      let mountedDirs =
        config.mountDirectories
        |> Map.add (UMX.tag<ServerUrl> "/tests") (UMX.tag<UserPath> "./tests")

      return {
        config with
            mountDirectories = mountedDirs
      }
  }

  let withTestingAndDefaultMounts = mountTestingDirectories >> withDefaultMounts

  let runTesting (container: AppContainer) (options: TestingOptions) = cancellableTask {
    let! cancellationToken = CancellableTask.getCancellationToken()

    let configA =
      container.Configuration.PerlaConfig
      |> AVal.map(fun config ->
        let includes =
          let includes =
            defaultArg options.files config.testing.includes |> Seq.toList

          if includes |> List.isEmpty then
            // Default to all test files if no includes are specified
            [ "**/*.test.js"; "**/*.spec.js" ]
          else
            includes

        let excludes = [
          "**/bin/**"
          "**/obj/**"
          "**/*.fs"
          "**/*.fsproj"
          yield! defaultArg options.skip config.testing.excludes
        ]

        {
          config with
              testing = {
                config.testing with
                    browsers =
                      defaultArg options.browsers config.testing.browsers
                    includes = includes
                    excludes = excludes
                    watch = defaultArg options.watch config.testing.watch
                    headless =
                      defaultArg options.headless config.testing.headless
                    browserMode =
                      defaultArg options.browserMode config.testing.browserMode
              }
        })
      |> withTestingAndDefaultMounts

    let config = configA |> AVal.force
    let fableConfig = config.testing.fable

    let fableFirstRunTcs = TaskCompletionSource()

    match fableConfig with
    | Some config ->
      container.Logger.LogInformation
        "Fable configuration found. Running Fable service."

      let work = asyncEx {
        let! token = Async.CancellationToken
        let events = container.FableService.Monitor(config, token)

        for event in events do
          match event with
          | FableEvent.Log _ -> ()
          | FableEvent.ErrLog _ -> ()
          | FableEvent.WaitingForChanges ->
            if not fableFirstRunTcs.Task.IsCompleted then
              fableFirstRunTcs.TrySetResult() |> ignore
              container.Logger.LogInformation "Fable service is ready."
            else
              container.Logger.LogInformation
                "Fable service is waiting for changes."
      }

      Async.Start(work, cancellationToken)
    | None ->
      container.Logger.LogWarning
        "Fable configuration not found. Skipping Fable service."

      fableFirstRunTcs.TrySetResult() |> ignore

    // Wait for either the Fable first run to complete or cancellation
    do! fableFirstRunTcs.Task

    if cancellationToken.IsCancellationRequested then
      container.Logger.LogInformation "Fable service cancelled before starting."

      return 0
    else
      let isWatch = config.testing.watch
      let plugins = container.FsManager.ResolvePluginPaths()

      let defaultPlugins = seq {
        if
          config.plugins
          |> List.exists(fun p ->
            p.Equals(
              Constants.PerlaEsbuildPluginName,
              StringComparison.InvariantCultureIgnoreCase
            ))
        then
          container.EsbuildService.GetPlugin config.esbuild
      }

      container.ExtensibilityService.LoadPlugins(plugins, defaultPlugins)
      |> Result.teeError(fun err ->
        container.Logger.LogError("Failed to load plugins: {error}", err))
      |> Result.ignore
      |> Result.ignoreError

      let mountedDirectories = config.mountDirectories

      do!
        container.Logger.Spinner(
          "Mounting Virtual File System",
          container.VirtualFileSystem.Load mountedDirectories
        )

      use! pw = Playwright.CreateAsync()

      let testingService =
        TestingService.Create {
          config = configA
          Logger = container.Logger
          VirtualFileSystem = container.VirtualFileSystem
          FsManager = container.FsManager
          ReqHandler = container.RequestHandler
          Directories = container.Directories
          Playwright = pw
        }

      let testingOptions =
        [
          for browser in config.testing.browsers do
            Browser browser

          Headless config.testing.headless
          if not isWatch then
            BrowserMode config.testing.browserMode
        ]
        |> set

      if isWatch then
        container.Logger.LogInformation "Running tests in watch mode..."

        let mutable state = Print.LiveDashboard.TestRunState.Empty

        let display =
          AnsiConsole
            .Live(Print.LiveDashboard.createDashboard state)
            .AutoClear(true)

        let work(ctx: LiveDisplayContext) = asyncEx {
          try
            for event in
              testingService.RunWatch(testingOptions, cancellationToken) do
              state <- Print.LiveDashboard.updateState state event
              ctx.UpdateTarget(Print.LiveDashboard.createDashboard state)
              ctx.Refresh()
          with
          | :? OperationCanceledException ->
            AnsiConsole.MarkupLine "[yellow]🛑 Test watch stopped by user[/]"
          | ex ->
            AnsiConsole.MarkupLine
              $"[red]💥 Error in test watch: {ex.Message.EscapeMarkup()}[/]"

          return ()
        }

        do!
          display.StartAsync(fun ctx ->
            Async.StartAsTask(work ctx, cancellationToken = cancellationToken))

        container.Logger.LogInformation "Test run completed"
        return 0
      else
        container.Logger.LogInformation "Running tests once..."

        let! reports = testingService.RunOnce testingOptions

        for report in reports do
          let title =
            match report.Browser with
            | Some browser -> $"Test Report for {browser.AsString}"
            | None -> "Test Report"

          Print.HeaderedPanel(
            title,
            Print.Report(report.Stats, report.Suites, report.Errors)
          )
          |> AnsiConsole.Write

        match
          reports |> Seq.tryFind(fun r -> r.Errors |> Seq.isEmpty |> not)
        with
        | Some report ->
          container.Logger.LogError(
            "Tests failed with {count} errors.",
            report.Errors |> Seq.length
          )

          return 1
        | None ->
          container.Logger.LogInformation "All tests passed successfully."
          return 0
  }

  let runAddPackage (container: AppContainer) (options: DependencyOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    let pkgManager = container.PkgManager
    let logger = container.Logger

    let config = container.Configuration.PerlaConfig |> AVal.force
    let importMap = container.FsManager.ResolveImportMap |> AVal.force

    // Process each package in the set
    let packageInfos =
      options.packages
      |> Set.map(fun pkg ->
        match (pkg, None) with
        | PkgWithVersion(basePkg, fullImport, version) ->
          basePkg, fullImport, version
        | _ ->
          logger.LogError("Invalid package name: {pkg}", pkg)
          failwith $"Invalid package name: {pkg}")

    // Start with existing packages
    let initialPackages =
      if config.useLocalPkgs then
        config.dependencies
        |> Set.map(fun { package = package; version = version } ->
          package, Some(UMX.untag version))
      else
        // Otherwise, we start with an empty set of packages
        importMap.ExtractDependencies()

    // Process each package to add
    let packages =
      packageInfos
      |> Set.fold
        (fun pkgs (basePkg, fullImport, version) ->
          // Remove any existing entries for this full import or base package
          pkgs
          |> Set.filter(fun (name, _) -> name <> fullImport && name <> basePkg)
          // Add both the base package and the full import (if different)
          |> fun filteredPkgs ->
              let updatedPkgs = filteredPkgs |> Set.add(basePkg, version)

              if fullImport <> basePkg then
                Set.add (fullImport, version) updatedPkgs
              else
                updatedPkgs)
        initialPackages

    // Log the packages being added
    for basePkg, fullImport, version in packageInfos do
      logger.LogInformation(
        "Adding package '{name}' with version '{version}'",
        fullImport,
        version
      )

    let provider =
      match config.provider with
      | JsDelivr -> Provider.JsDelivr
      | Unpkg -> Provider.Unpkg
      | JspmIo -> Provider.JspmIo

    // Map to install strings: base@version and base@version/deep
    let installSet =
      packages
      |> Set.map(fun (name, version) ->
        match name, version with
        | InstallString s -> s
        | _ ->
          logger.LogError("Invalid package name: {name}", name)
          failwith $"Invalid package name: {name}")

    let! installResponse =
      logger.Spinner(
        "Generating Import Map...",
        pkgManager.Install(
          installSet,
          [
            DefaultProvider provider
            Env(set [ ExportCondition.Browser; ExportCondition.Module ])
          ],
          cancellationToken = token
        )
      )

    match installResponse with
    | GeneratorResponseKind.ResponseError err ->
      logger.LogError("Unable to install packages: {error}", err.error)
      return 1
    | GeneratorResponseKind.Success installResponse ->

    let packageUpdates =
      // extract the dependencies before we save the offline map
      installResponse.map.ExtractDependencies()
      |> Set.map(fun (name, version) ->
        let dep: PkgDependency = {
          package = name
          // dependencies from a GeneratorResponse have embedded versions even if they are not specified
          version = version.Value |> UMX.tag<Semver>
        }

        dep)
      |> PerlaConfig.PerlaWritableField.Dependencies

    let configUpdates = ResizeArray()
    configUpdates.Add packageUpdates

    if config.useLocalPkgs then
      let! result = cancellableTask {
        try
          return!
            logger.Spinner(
              "Downloading Sources...",
              pkgManager.GoOffline(
                installResponse.map,
                [ Provider config.provider; Exclude(set [ Unused ]) ],
                token
              )
            )
        with ex ->
          logger.LogWarning(
            "Failed to download sources: {error}, retrying...",
            ex.Message
          )

          return!
            logger.Spinner(
              "Downloading Sources...",
              pkgManager.GoOffline(
                installResponse.map,
                [ Provider config.provider ],
                token
              )
            )
      }

      do! container.FsManager.SaveImportMap result
    else
      do! container.FsManager.SaveImportMap installResponse.map

    do! container.FsManager.SavePerlaConfig configUpdates

    logger.LogInformation("Packages added successfully.")

    return 0
  }

  let runRemovePackage (container: AppContainer) (options: DependencyOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    let config = container.Configuration.PerlaConfig |> AVal.force
    let logger = container.Logger
    let map = container.FsManager.ResolveImportMap |> AVal.force

    // Common helper: map config provider to Provider enum
    let provider =
      match config.provider with
      | JsDelivr -> Provider.JsDelivr
      | Unpkg -> Provider.Unpkg
      | JspmIo -> Provider.JspmIo

    // Common helper: validate packages and get list to remove
    let packagesToRemove =
      if config.useLocalPkgs then
        // Check against perla.json dependencies
        options.packages
        |> Seq.choose(fun pkg ->
          let found =
            config.dependencies
            |> Set.exists(fun dep ->
              dep.package.Equals(
                pkg,
                StringComparison.InvariantCultureIgnoreCase
              ))

          if found then
            Some pkg
          else
            logger.LogWarning(
              "Package '{name}' not found in the perla.json dependencies.",
              pkg
            )

            None)
      else
        // Check against import map
        options.packages
        |> Seq.choose(fun pkg ->
          match map.FindDependency pkg with
          | None ->
            logger.LogWarning(
              "Package '{name}' not found in the import map.",
              pkg
            )

            None
          | Some(name, _) -> Some name)

    if Seq.isEmpty packagesToRemove then
      logger.LogError("No valid packages found to remove.")
      return 1
    else if config.useLocalPkgs then
      logger.LogDebug("Using useLocalPkgs = true flow")
      // useLocalPkgs = true flow
      // Remove user provided packages that match on package name
      let remainingDependencies =
        config.dependencies
        |> Set.filter(fun dep ->
          not(
            packagesToRemove
            |> Seq.exists(fun pkg ->
              dep.package.Equals(
                pkg,
                StringComparison.InvariantCultureIgnoreCase
              ))
          ))

      // Convert remaining dependencies to install strings with versions
      let remainingPackages =
        remainingDependencies
        |> Set.map(fun { package = package; version = version } ->
          match package, Some version with
          | InstallString s -> s
          | _ ->
            logger.LogError("Invalid package name: {package}", package)
            failwith $"Invalid package name: {package}")

      // Regenerate import map with remaining packages
      let! newMap =
        logger.Spinner(
          "Regenerating import map with remaining packages...",
          container.PkgManager.Install(
            remainingPackages,
            [
              DefaultProvider provider
              Env(set [ ExportCondition.Browser; ExportCondition.Module ])
            ],
            token
          )
        )

      match newMap with
      | GeneratorResponseKind.ResponseError err ->
        logger.LogError("Unable to regenerate import map: {error}", err.error)
        return 1
      | GeneratorResponseKind.Success newMapResponse ->
        logger.LogDebug(
          "Successfully regenerated import map, calling goOffline"
        )
        // Call goOffline to download local packages
        let! result = cancellableTask {
          try
            return!
              logger.Spinner(
                "Consolidating local packages...",
                container.PkgManager.GoOffline(
                  newMapResponse.map,
                  [ Provider config.provider; Exclude(set [ Unused ]) ],
                  token
                )
              )
          with ex ->
            logger.LogWarning(
              "Failed to download sources: {error}, retrying...",
              ex.Message
            )

            return!
              logger.Spinner(
                "Consolidating local packages...",
                container.PkgManager.GoOffline(
                  newMapResponse.map,
                  [ Provider config.provider ],
                  token
                )
              )
        }

        logger.LogDebug("goOffline completed successfully")

        // Update config and save files
        let configUpdates = ResizeArray()

        configUpdates.Add(
          PerlaConfig.PerlaWritableField.Dependencies remainingDependencies
        )

        do! container.FsManager.SaveImportMap result
        do! container.FsManager.SavePerlaConfig configUpdates
        logger.LogInformation("Packages removed successfully.")
        return 0
    else
      // useLocalPkgs = false flow
      let! uninstallResponse =
        logger.Spinner(
          "Uninstalling packages...",
          container.PkgManager.Uninstall(
            map,
            packagesToRemove,
            [ DefaultProvider provider ],
            token
          )
        )

      match uninstallResponse with
      | GeneratorResponseKind.ResponseError err ->
        logger.LogError("Unable to uninstall packages: {error}", err.error)
        return 1
      | GeneratorResponseKind.Success uninstallResponse ->
        let packageUpdates =
          uninstallResponse.map.ExtractDependencies()
          |> Set.map(fun (name, version) ->
            let dep: PkgDependency = {
              package = name
              version = version.Value |> UMX.tag<Semver>
            }

            dep)
          |> PerlaConfig.PerlaWritableField.Dependencies

        let configUpdates = ResizeArray()
        configUpdates.Add packageUpdates

        do! container.FsManager.SaveImportMap uninstallResponse.map
        do! container.FsManager.SavePerlaConfig configUpdates
        logger.LogInformation("Packages removed successfully.")
        return 0

  }

  let runInstall (container: AppContainer) (options: InstallOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    let config = container.Configuration.PerlaConfig
    let logger = container.Logger
    let pkgManager = container.PkgManager

    let dependencies = config |> AVal.map _.dependencies |> AVal.force

    if Set.isEmpty dependencies then
      logger.LogWarning("No dependencies found.")

      logger.LogInformation(
        "You can add dependencies with 'perla add <package>'."
      )

      let configUseLocalPkgs = config |> AVal.map _.useLocalPkgs |> AVal.force
      let provider = config |> AVal.map _.provider |> AVal.force

      let changes = ResizeArray()

      let useLocalPkgs =
        options.offline |> Option.defaultValue configUseLocalPkgs

      if useLocalPkgs <> configUseLocalPkgs then
        let msg = if useLocalPkgs then "Enabling" else "Disabling"
        logger.LogInformation($"{msg} local packages.")

        if useLocalPkgs then
          logger.LogInformation(
            "Perla will generate a local node_modules directory and the import map will point at the dependencies there."
          )
        else
          logger.LogInformation(
            "Perla will use an import map that points to the provider's CDN."
          )

        changes.Add(PerlaConfig.PerlaWritableField.UseLocalPkgs useLocalPkgs)

      if options.source.IsSome && options.source.Value <> provider then
        logger.LogInformation(
          $"Changing the provider to {DownloadProvider.asString options.source.Value}."
        )

        changes.Add(
          PerlaConfig.PerlaWritableField.Provider options.source.Value
        )

      do! container.FsManager.SavePerlaConfig(changes)
      return 0
    else
      let packages =
        config
        |> AVal.map _.dependencies
        |> AVal.force
        |> Set.map(fun ({ package = package; version = version }) ->
          match package, Some version with
          | InstallString s -> s
          | _ ->
            logger.LogError("Invalid package name: {package}", package)
            failwith $"Invalid package name: {package}")

      logger.LogDebug("Found Dependencies: {dependencies}", packages)

      let provider =
        match options.source with
        | ValueSome source -> source
        | ValueNone -> config |> AVal.map _.provider |> AVal.force

      let configUseLocalPkgs =
        container.Configuration.PerlaConfig
        |> AVal.map _.useLocalPkgs
        |> AVal.force

      let useLocalPkgs =
        options.offline |> Option.defaultValue configUseLocalPkgs


      let! map =
        logger.Spinner(
          "Generating Import Map...",
          pkgManager.Install(
            packages,
            [
              match provider with
              | JsDelivr -> DefaultProvider Provider.JsDelivr
              | Unpkg -> DefaultProvider Provider.Unpkg
              | PkgManager.DownloadProvider.JspmIo ->
                DefaultProvider Provider.JspmIo
            ],
            cancellationToken = token
          )
        )

      logger.LogDebug("Response: {response}", map)

      match map with
      | GeneratorResponseKind.ResponseError err ->
        logger.LogError("Unable to install packages: {error}", err.error)
        return 1
      | GeneratorResponseKind.Success updateResponse ->

      let packageUpdates =
        // extract the dependencies before we save the offline map
        updateResponse.map.ExtractDependencies()
        |> Set.map(fun (name, version) ->
          let dep: PkgDependency = {
            package = name
            // dependencies from a GeneratorResponse have embedded versions even if they are not specified
            version = version.Value |> UMX.tag<Semver>
          }

          dep)
        |> PerlaConfig.PerlaWritableField.Dependencies

      let! map = cancellableTask {
        if useLocalPkgs then
          logger.LogWarning(
            "Offline mode selected, we'll proceed to download the dependencies as local sources."
          )

          return!
            logger.Spinner(
              "Downloading Sources...",
              pkgManager.GoOffline(
                updateResponse.map,
                [ DownloadOption.Provider provider ],
                token
              )
            )
        else
          logger.LogInformation("Import map generated successfully.")
          return updateResponse.map
      }

      let configUpdates = [
        packageUpdates
        if useLocalPkgs <> configUseLocalPkgs then
          PerlaConfig.PerlaWritableField.UseLocalPkgs useLocalPkgs

        match options.source with
        | ValueSome source -> PerlaConfig.PerlaWritableField.Provider source
        | ValueNone -> ()
      ]

      do! container.FsManager.SaveImportMap(map)
      do! container.FsManager.SavePerlaConfig(configUpdates)
      logger.LogInformation("Packages installed successfully.")

      return 0
  }

  let runListPackages (container: AppContainer) (options: ListPackagesOptions) = cancellableTask {
    let config = container.Configuration.PerlaConfig
    let logger = container.Logger

    let dependencies = config |> AVal.map _.dependencies |> AVal.force

    match options.format with
    | ListFormat.HumanReadable ->
      logger.LogInformation("Installed packages:")

      let prodTable = Table().AddColumn("Package").AddColumn("Version")

      for dep in dependencies do
        prodTable.AddRow(
          Text(dep.package, Style(foreground = Color.Green)),
          Text(UMX.untag dep.version, Style(foreground = Color.Blue))
        )
        |> ignore


      AnsiConsole.Write prodTable
      AnsiConsole.WriteLine()

    | ListFormat.TextOnly ->

      let dependencies =
        dependencies
        |> Set.map(fun dep -> dep.package, dep.version)
        |> Map.ofSeq

      AnsiConsole.Clear()
      AnsiConsole.Write(Json.Json.ToText(dependencies, false))

    return 0
  }

  let runDescribePerla
    (container: AppContainer)
    ({
       properties = props
       current = current
     }: DescribeOptions)
    =
    cancellableTask {

      let config = container.Configuration.PerlaConfig

      let table = Table().AddColumn("Property")

      AnsiConsole.Write(FigletText("Perla.json"))

      let! descriptions = container.FsManager.ResolveDescriptionsFile()

      match props, current with
      | props, true ->
        table.AddColumns("Value", "Explanation") |> ignore
        let config = config |> AVal.force

        for prop in props do
          let description =
            descriptions |> Map.tryFind prop |> Option.defaultValue ""

          match prop with
          | TopLevelProp prop ->
            table.AddRow(
              Text(prop),
              config[prop] |> Option.defaultValue(Text ""),
              Text(description)
            )
            |> ignore
          | NestedProp props ->
            table.AddRow(
              Text(prop),
              config[props] |> Option.defaultValue(Text ""),
              Text(description)
            )
            |> ignore
          | TripleNestedProp props ->
            table.AddRow(
              Text(prop),
              config[props] |> Option.defaultValue(Text ""),
              Text(description)
            )
            |> ignore
          | InvalidPropPath ->
            table.AddRow(prop, "", "This is not a valid property") |> ignore

      | props, false ->
        table.AddColumns("Description", "Default Value") |> ignore

        for prop in props do
          let description =
            descriptions |> Map.tryFind prop |> Option.defaultValue ""

          match prop with
          | TopLevelProp prop ->
            table.AddRow(
              Text(prop),
              Text(description),
              Defaults.PerlaConfig[prop] |> Option.defaultValue(Text "")
            )
            |> ignore
          | NestedProp props ->
            table.AddRow(
              Text(prop),
              Text(description),
              Defaults.PerlaConfig[props] |> Option.defaultValue(Text "")
            )
            |> ignore
          | TripleNestedProp props ->
            table.AddRow(
              Text(prop),
              Text(description),
              Defaults.PerlaConfig[props] |> Option.defaultValue(Text "")
            )
            |> ignore
          | InvalidPropPath ->
            table.AddRow(
              Text(
                prop,
                Style(foreground = Color.Yellow, background = Color.Yellow)
              ),
              Text(""),
              Text(
                "This is not a valid property",
                Style(foreground = Color.Yellow)
              )
            )
            |> ignore

      table.Caption <-
        TableTitle(
          "For more information visit: https://perla-docs.web.app/#/v1/docs/reference/perla"
        )

      table.DoubleBorder() |> AnsiConsole.Write
      return 0
    }
