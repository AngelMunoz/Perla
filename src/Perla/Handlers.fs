namespace Perla.Handlers

open System
open System.IO

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Microsoft.Playwright

open AngleSharp
open AngleSharp.Html.Parser
open Spectre.Console

open FSharp.Control
open FSharp.Control.Reactive
open FSharp.Data.Adaptive

open IcedTasks

open FSharp.UMX
open FsToolkit.ErrorHandling

open Perla
open Perla.Units
open Perla.Types
open Perla.Json
open Perla
open Perla.FileSystem
open Perla.Database
open Perla.Fable
open Perla.Esbuild
open Perla.Extensibility
open Perla.VirtualFs
open Perla.Build
open Perla.Scaffolding
open Perla.Configuration
open Perla.Logger
open Perla.PkgManager
open Perla.PkgManager.PkgManager
open Perla.Server

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

type AddPackageOptions = {
  package: string
  version: string option
}

type RemovePackageOptions = { package: string }

type InstallOptions = {
  offline: bool
  source: Perla.PkgManager.DownloadProvider voption
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
        id |> Option.bind(fun id -> if tpl.id = id then Some tpl else None)

      let byShortName =
        name
        |> Option.bind(fun shortName ->
          if tpl.shortName = shortName then Some tpl else None)

      byId |> Option.orElse byShortName)

  let private copyFiles
    (sourcePath: DirectoryInfo)
    (targetPath: string<SystemPath>)
    =
    let progress = AnsiConsole.Progress()
    let files = sourcePath.GetFiles("*", SearchOption.AllDirectories)

    progress.Start(fun ctx ->
      let tsk =
        ctx.AddTask("Creating project...", true, maxValue = files.Length)

      files
      |> Array.Parallel.iter(fun file ->
        let targetPath =
          file.FullName.Replace(sourcePath.FullName, UMX.untag targetPath)

        match file.Directory with
        | null -> ()
        | directory -> directory.Create()

        File.Copy(file.FullName, UMX.untag targetPath, true)
        tsk.Increment 1)

      tsk.StopTask())

  let writeFoundTemplate(tpl: TemplateItem, targetPath: string<SystemPath>) =
    let tplPath = DirectoryInfo(UMX.untag tpl.FullPath)
    copyFiles tplPath targetPath

  let writeFoundDecodedTemplate
    (directories: PerlaDirectories)
    (
      config: DecodedTemplateConfiguration,
      tpl: DecodedTemplateConfigItem,
      targetPath: string<SystemPath>
    ) =
    let tplPath =
      Path.Combine(UMX.untag directories.OfflineTemplates, UMX.untag tpl.path)
      |> DirectoryInfo

    copyFiles tplPath targetPath

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
      "No templates found in the offline templates, please add a template to the perla database or the offline templates."
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

  let runNew (container: AppContainer) (options: ProjectOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    let (Logger logger) = container
    let tplList = container.TemplateService.ListTemplateItems()

    let handleOfflineTemplates() = cancellableTask {
      logger.LogWarning
        "No templates found in the perla database, searching in the default offline templates."

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
          found.name,
          found.shortName
        )

        RunNew.writeFoundDecodedTemplate
          container.Directories
          (otConfig, found, UMX.tag targetPath.FullName)

        RunNew.logProjectCreationSuccess logger targetPath
        return 0
      | RunNew.PromptUser templates when not(Seq.isEmpty templates) ->
        let prompt =
          SelectionPrompt()
            .Title("Select a template to create a new project:")
            .EnableSearch()
            .UseConverter(fun (tpl: DecodedTemplateConfigItem) ->
              $"{tpl.name} ({tpl.shortName}) - {tpl.description}")
            .AddChoices(templates)

        let! selected = AnsiConsole.PromptAsync(prompt, token)

        RunNew.writeFoundDecodedTemplate
          container.Directories
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

        RunNew.writeFoundTemplate(found, UMX.tag targetPath.FullName)
        RunNew.logProjectCreationSuccess logger targetPath
        return 0
      | RunNew.PromptUser templates when not(Seq.isEmpty templates) ->
        let prompt =
          SelectionPrompt()
            .Title("Select a template to create a new project:")
            .EnableSearch()
            .UseConverter(fun (tpl: TemplateItem) ->
              $"{tpl.Name} ({tpl.ShortName}) - {tpl.Description}")
            .AddChoices(templates)

        let! selected = AnsiConsole.PromptAsync(prompt, token)
        RunNew.writeFoundTemplate(selected, UMX.tag targetPath.FullName)
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

      let template = voption {
        let! username, repository, _ =
          parseFullRepositoryName options.fullRepositoryName

        return!
          TemplateSearchKind.FullName(username, repository)
          |> templateService.FindOne
          |> ValueOption.ofOption
      }

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
        // Simply list the templates using the service and return
        let templates = templateService.ListTemplateItems()

        match listFormat with
        | ListFormat.HumanReadable ->
          let table =
            Table()
              .AddColumn("Name")
              .AddColumn("Short Name")
              .AddColumn("Description")

          for template in templates do
            let description =
              template.Description
              |> Option.defaultValue "No description provided"

            table.AddRow(template.Name, template.ShortName, description)
            |> ignore

          AnsiConsole.Write(table)
        | ListFormat.TextOnly ->
          for template in templates do
            let description =
              template.Description
              |> Option.defaultValue "No description provided"

            logger.LogInformation
              $"{template.Name} ({template.ShortName}) - {description}"

        return 0

      | RunTemplateOperation.Add ->
        match parseFullRepositoryName options.fullRepositoryName with
        | ValueSome(username, repository, branch) ->
          try
            let! id =
              templateService.Add(username, UMX.tag repository, UMX.tag branch)

            logger.LogInformation $"Template added successfully with id: {id}"
            return 0
          with ex ->
            logger.LogError(ex, "Failed to add template: {Error}", ex.Message)
            return 1
        | ValueNone ->
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

  let runBuild (container: AppContainer) (options: BuildOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()

    let config = container.Configuration.PerlaConfig |> AVal.force

    match config.fable with
    | None ->
      container.Logger.LogWarning(
        "Fable configuration not found. Skipping Fable build."
      )
    | Some config ->
      container.Logger.LogInformation(
        "Fable configuration found. Running Fable build."
      )

      do! container.FableService.Run config

    let outDir = DirectoryInfo(UMX.untag config.build.outDir)

    try
      outDir.Delete(true)
    with ex ->
      container.Logger.LogWarning(
        "Failed to clean output directory {path}: {error}",
        outDir.FullName,
        ex.Message
      )

    let plugins = container.FsManager.ResolvePluginPaths()

    let esbuildPlugin =
      config.plugins
      |> List.tryFind(fun p ->
        p.Equals(
          Constants.PerlaEsbuildPluginName,
          StringComparison.InvariantCultureIgnoreCase
        ))
      |> Option.map(fun _ -> container.EsbuildService.GetPlugin config.esbuild)

    container.ExtensibilityService.LoadPlugins(
      plugins,
      ?esbuildPlugin = esbuildPlugin
    )
    |> Result.teeError(fun err ->
      container.Logger.LogError("Failed to load plugins: {error}", err))
    |> Result.ignore
    |> Result.ignoreError

    do!
      container.Logger.Spinner(
        "Mounting Virtual File System",
        container.VirtualFileSystem.Load config.mountDirectories
      )

    let! tempDir = container.VirtualFileSystem.ToDisk()


    container.Logger.LogInformation(
      "Copying Processed files to {tempDirectory}",
      tempDir
    )

    if config.build.emitEnvFile then
      container.Logger.LogInformation("Writing Env File")

      container.FsManager.EmitEnvFile(config, tempDir)

    use browserCtx = new BrowsingContext()

    let index = container.FsManager.ResolveIndex |> AVal.force

    let document =
      (browserCtx.GetService<IHtmlParser>() |> nonNull).ParseDocument index

    let map = container.FsManager.ResolveImportMap |> AVal.force

    // at this point all of the files in the tempDir should have gone through esbuild if it was enabled
    // but as individual files, not as a bundle
    let cssPaths, jsPaths, standalonePaths = Build.EntryPoints document

    let jsPaths = seq {
      yield! jsPaths
      yield! standalonePaths
    }

    // TODO: make sure the import map is correct and that it includes the "paths" map
    let indexContent = Build.Index(document, map, jsPaths, cssPaths)

    // TODO: check the copy globs logic
    // we've changed the virtual file system so we need to re-evaluate how we're copying the blobs
    // specially the vfs:<glob>
    container.FsManager.CopyGlobs(config.build, tempDir)

    do!
      File.WriteAllTextAsync(
        Path.Combine(UMX.untag tempDir, "index.html"),
        indexContent,
        token
      )

    // Move the temporary directory to the output directory as that's the source of truth for our build
    try
      Directory.Move(UMX.untag tempDir, outDir.FullName)
    with ex ->
      container.Logger.LogWarning(
        "Failed to move temporary directory {tempDir} to output directory {outDir}: {error}",
        tempDir,
        config.build.outDir,
        ex.Message
      )

    // TODO: evauate the bundle and minification usage here
    // at this point our original index file is still pointing to the entrypoints
    // so we could run esbuild to bundle and minify the sources
    // however, this might cause issues with the "paths" and other custom resolutions

    if options.enablePreview then
      container.Logger.LogInformation "Starting a preview server for the build"

      let app =
        Server.Server.GetStaticServer container.Configuration.PerlaConfig

      do! app.StartAsync token

      app.Urls
      |> Seq.iter(fun url ->
        container.Logger.LogInformation("Listening at: {url}", url))

      while not token.IsCancellationRequested do
        do! Async.Sleep 1000

      do! app.StopAsync token

    return 0
  }

  let runServe (container: AppContainer) (options: ServeOptions) = cancellableTask {
    let! cancellationToken = CancellableTask.getCancellationToken()

    let configA =
      container.Configuration.PerlaConfig
      |> AVal.map(fun config -> {
        config with
            devServer = {
              config.devServer with
                  port = defaultArg options.port config.devServer.port
                  host = defaultArg options.host config.devServer.host
                  useSSL = defaultArg options.ssl config.devServer.useSSL
            }
            esbuild = { config.esbuild with minify = false }
      })

    let config = configA |> AVal.force

    let esbuildPlugin =
      config.plugins
      |> List.tryFind(fun p ->
        p.Equals(
          Constants.PerlaEsbuildPluginName,
          StringComparison.InvariantCultureIgnoreCase
        ))
      |> Option.map(fun _ -> container.EsbuildService.GetPlugin config.esbuild)

    let plugins = container.FsManager.ResolvePluginPaths()

    container.ExtensibilityService.LoadPlugins(
      plugins,
      ?esbuildPlugin = esbuildPlugin
    )
    |> Result.teeError(fun err ->
      container.Logger.LogError("Failed to load plugins: {error}", err))
    |> Result.ignore
    |> Result.ignoreError

    let mountedDirectories = config.mountDirectories
    let fableConfig = config.fable

    let mutable isFableFirstRunDone = fableConfig.IsSome && false

    match fableConfig with
    | Some config ->
      container.Logger.LogInformation
        "Fable configuration found. Running Fable service."

      let work = asyncEx {
        let events = container.FableService.Monitor config

        for event in events do
          match event with
          | FableEvent.Log _ -> ()
          | FableEvent.ErrLog _ -> ()
          | FableEvent.WaitingForChanges ->
            if not isFableFirstRunDone then
              isFableFirstRunDone <- true
              container.Logger.LogInformation "Fable service is ready."
            else
              container.Logger.LogInformation
                "Fable service waiting for changes."
      }

      Async.Start(work, cancellationToken)
    | None ->
      container.Logger.LogWarning
        "Fable configuration not found. Skipping Fable service."

      isFableFirstRunDone <- true

    do!
      container.Logger.Spinner(
        "Mounting Virtual File System",
        container.VirtualFileSystem.Load mountedDirectories
      )

    let mutable server =
      Server.GetServerApp(
        configA,
        container.VirtualFileSystem,
        container.VirtualFileSystem.FileChanges,
        Observable.empty,
        container.FsManager
      )

    while not cancellationToken.IsCancellationRequested
          && not isFableFirstRunDone do
      do! Async.Sleep(TimeSpan.FromMilliseconds(100.))

    do! server.StartAsync(cancellationToken)

    server.Urls
    |> Seq.iter(fun url ->
      container.Logger.LogInformation("Listening at: {url}", url))

    use _ =
      container.FsManager.PerlaConfiguration.AddCallback
        (fun (config: PerlaConfig) ->
          let port, host, useSSL =
            configA
            |> AVal.map(fun config ->
              config.devServer.port,
              config.devServer.host,
              config.devServer.useSSL)
            |> AVal.force

          if
            port <> config.devServer.port
            || config.devServer.host <> host
            || config.devServer.useSSL <> useSSL
          then
            container.Logger.LogInformation
              "Server configuration changed, restarting server..."

            let work = asyncEx {
              do! server.StopAsync cancellationToken

              server <-
                Server.GetServerApp(
                  configA,
                  container.VirtualFileSystem,
                  container.VirtualFileSystem.FileChanges,
                  Observable.empty,
                  container.FsManager
                )

              do! server.StartAsync cancellationToken
            }

            Async.StartImmediate(work, cancellationToken))

    while not cancellationToken.IsCancellationRequested do
      do! Async.Sleep(TimeSpan.FromSeconds(1.))

    return 0
  }

  let runTesting (container: AppContainer) (options: TestingOptions) = cancellableTask {
    // let! cancellationToken = CancellableTask.getCancellationToken()

    // ConfigurationManager.UpdateFromCliArgs(
    //   testingOptions = [
    //     match options.browsers with
    //     | Some value -> TestingField.Browsers value
    //     | None -> ()
    //     match options.files with
    //     | Some value -> TestingField.Includes value
    //     | None -> ()
    //     match options.skip with
    //     | Some value -> TestingField.Excludes value
    //     | None -> ()
    //     match options.watch with
    //     | Some value -> TestingField.Watch value
    //     | None -> ()
    //     match options.headless with
    //     | Some value -> TestingField.Headless value
    //     | None -> ()
    //     match options.browserMode with
    //     | Some value -> TestingField.BrowserMode value
    //     | None -> ()
    //   ]
    // )

    // let config = {
    //   ConfigurationManager.CurrentConfig with
    //       mountDirectories =
    //         ConfigurationManager.CurrentConfig.mountDirectories
    //         |> Map.add
    //           (UMX.tag<ServerUrl> "/tests")
    //           (UMX.tag<UserPath> "./tests")
    // }

    // let isWatch = config.testing.watch

    // let fableEvents =
    //   match config.testing.fable with
    //   | Some fable -> Fable.Observe(fable, isWatch)
    //   | None -> Observable.single FableEvent.WaitingForChanges

    // fableEvents
    // |> Observable.add(fun events ->
    //   match events with
    //   | FableEvent.Log msg -> Logger.log(msg.EscapeMarkup())
    //   | FableEvent.ErrLog msg ->
    //     Logger.log $"[bold red]{msg.EscapeMarkup()}[/]"
    //   | FableEvent.WaitingForChanges -> ())

    // do! FsMonitor.FirstCompileDone isWatch fableEvents

    // match PluginLoader.Load<FileSystem, Esbuild>(config.esbuild) with
    // | Ok plugins -> Logger.log $"Loaded {plugins.Length} plugins"
    // | Error err ->
    //   for err in err do
    //     match err with
    //     | NoPluginFound name -> Logger.log($"Plugin {name} not found")
    //     | EvaluationFailed(ex) ->
    //       Logger.log($"Failed to evaluate plugin", ex = ex)
    //     | SessionExists
    //     | BoundValueMissing -> Logger.log "Failed to load plugins"
    //     | AlreadyLoaded name -> Logger.log($"Plugin {name} already loaded")

    // do! VirtualFileSystem.Mount config

    // let perlaChanges =
    //   FileSystem.ObservePerlaFiles(UMX.untag config.index, cancellationToken)

    // let fileChanges =
    //   FsMonitor.FileChanges(
    //     UMX.untag config.index,
    //     config.mountDirectories,
    //     perlaChanges,
    //     config.plugins
    //   )
    // // TODO: Grab these from esbuild
    // let compilerErrors = Observable.empty

    // let config = {
    //   config with
    //       devServer = {
    //         config.devServer with
    //             liveReload = isWatch
    //       }
    // }

    // let events = Subject<TestEvent>.broadcast

    // let! dependencies =
    //   Dependencies.GetMapAndDependencies Seq.empty
    //   |> TaskResult.map(fun (deps, map) ->
    //     let map = map.AddResolutions(config.paths).AddEnvResolution config
    //     deps, map)
    //   |> TaskResult.defaultValue(
    //     Seq.empty,
    //     FileSystem.GetImportMap().AddResolutions(config.paths).AddEnvResolution
    //       config
    //   )

    // let mutable app =
    //   Server.GetTestingApp(
    //     config,
    //     dependencies,
    //     events,
    //     fileChanges,
    //     compilerErrors,
    //     config.testing.includes
    //   )
    // // Keep this before initializing the server
    // // otherwise it will always say that the port is occupied
    // let http, _ =
    //   Server.GetServerURLs
    //     config.devServer.host
    //     config.devServer.port
    //     config.devServer.useSSL

    // do! app.StartAsync(cancellationToken)

    // perlaChanges
    // |> Observable.choose (function
    //   | PerlaFileChange.PerlaConfig -> Some()
    //   | _ -> None)
    // |> Observable.map(fun _ -> app.StopAsync() |> Async.AwaitTask)
    // |> Observable.switchAsync
    // |> Observable.map(fun _ ->
    //   ConfigurationManager.UpdateFromFile()
    //   app <- Server.GetServerApp(config, fileChanges, compilerErrors)
    //   app.StartAsync(cancellationToken) |> Async.AwaitTask)
    // |> Observable.switchAsync
    // |> Observable.add ignore

    // use! pl = Playwright.CreateAsync()

    // let testConfig = config.testing

    // if not isWatch then
    //   do!
    //     Testing.RunOnce(
    //       pl,
    //       testConfig.browserMode,
    //       testConfig.browsers,
    //       testConfig.headless,
    //       http
    //     )

    //   events.OnCompleted()

    //   events
    //   |> Observable.toEnumerable
    //   |> Seq.toList
    //   |> Testing.BuildReport
    //   |> Print.Report

    //   return 0
    // else
    //   let browser = config.testing.browsers |> Seq.head
    //   let fileChanges = fileChanges |> Observable.map ignore

    //   do!
    //     Testing.LiveRun(
    //       pl,
    //       browser,
    //       testConfig.headless,
    //       http,
    //       fileChanges,
    //       events,
    //       cancellationToken
    //     )

    //   events.OnCompleted()

    //   events
    //   |> Observable.toEnumerable
    //   |> Seq.toList
    //   |> Testing.BuildReport
    //   |> Print.Report

    return 0
  }

  let runAddPackage (container: AppContainer) (options: AddPackageOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    let pkgManager = container.PkgManager
    let logger = container.Logger

    let config = container.Configuration.PerlaConfig |> AVal.force
    let importMap = container.FsManager.ResolveImportMap |> AVal.force

    let basePkg, fullImport, version = options.package |> parsePackageName
    let version = version |> Option.orElseWith(fun _ -> options.version)
    let packages = importMap.ExtractDependencies()

    // Remove any existing entries for this full import or base package
    let packages =
      packages
      |> Set.filter(fun (name, _) -> name <> fullImport && name <> basePkg)
      // Add both the base package and the full import (if different)
      |> fun pkgs ->
          let pkgs = pkgs |> Set.add(basePkg, version)

          if fullImport <> basePkg then
            Set.add (fullImport, version) pkgs
          else
            pkgs

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
        let basePkg, full, _ = parsePackageName name

        match version with
        | Some v when full <> basePkg ->
          $"{basePkg}@{v}/{full.Substring(basePkg.Length + 1)}"
        | Some v -> $"{basePkg}@{v}"
        | None -> full)

    let! installResponse =
      logger.Spinner(
        "Generating Import Map...",
        pkgManager.Install(
          installSet,
          [ DefaultProvider provider ],
          cancellationToken = token
        )
      )

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
      let! result =
        logger.Spinner(
          "Downloading Sources...",
          pkgManager.GoOffline(
            installResponse.map,
            [ Provider config.provider ],
            token
          )
        )

      do! container.FsManager.SaveImportMap result
    else
      do! container.FsManager.SaveImportMap installResponse.map

    do! container.FsManager.SavePerlaConfig configUpdates

    logger.LogInformation(
      "Package '{name}' installed successfully.",
      fullImport
    )

    return 0
  }

  let runRemovePackage
    (container: AppContainer)
    (options: RemovePackageOptions)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let config = container.Configuration.PerlaConfig |> AVal.force
      let logger = container.Logger
      let map = container.FsManager.ResolveImportMap |> AVal.force

      match map.FindDependency options.package with
      | None ->
        logger.LogError(
          "Package '{name}' not found in the import map.",
          options.package
        )

        return 1
      | Some(name, _) ->
        let! uninstallResponse =
          logger.Spinner(
            $"Uninstalling package '{name}'...",
            container.PkgManager.Uninstall(
              map,
              [ name ],
              [
                DefaultProvider(
                  match config.provider with
                  | JsDelivr -> Provider.JsDelivr
                  | Unpkg -> Provider.Unpkg
                  | JspmIo -> Provider.JspmIo
                )
              ],
              token
            )
          )

        let packageUpdates =
          // extract the dependencies before we save the offline map
          uninstallResponse.map.ExtractDependencies()
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
          let! result =
            logger.Spinner(
              "Consolidating local packages...",
              container.PkgManager.GoOffline(
                uninstallResponse.map,
                [ Provider config.provider ],
                token
              )
            )

          do! container.FsManager.SaveImportMap result
        else
          do! container.FsManager.SaveImportMap uninstallResponse.map

        do! container.FsManager.SavePerlaConfig configUpdates
        logger.LogInformation("Packages installed successfully.")
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

      let useLocalPkgs = config |> AVal.map _.useLocalPkgs |> AVal.force
      let provider = config |> AVal.map _.provider |> AVal.force

      let changes = ResizeArray()

      if options.offline <> useLocalPkgs then
        let msg = if options.offline then "Enabling" else "Disabling"
        logger.LogInformation($"{msg} local packages.")

        if options.offline then
          logger.LogInformation(
            "Perla will generate a local node_modules directory and the import map will point at the dependencies there."
          )
        else
          logger.LogInformation(
            "Perla will use an import map that points to the provider's CDN."
          )

        changes.Add(PerlaConfig.PerlaWritableField.UseLocalPkgs options.offline)

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
        dependencies
        |> Set.map(fun dep -> $"{dep.package}@{UMX.untag dep.version}")

      let provider =
        match options.source with
        | ValueSome source -> source
        | ValueNone -> config |> AVal.map _.provider |> AVal.force

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

      let packageUpdates =
        // extract the dependencies before we save the offline map
        map.map.ExtractDependencies()
        |> Set.map(fun (name, version) ->
          let dep: PkgDependency = {
            package = name
            // dependencies from a GeneratorResponse have embedded versions even if they are not specified
            version = version.Value |> UMX.tag<Semver>
          }

          dep)
        |> PerlaConfig.PerlaWritableField.Dependencies

      let! map = cancellableTask {
        if options.offline then
          logger.LogWarning(
            "Offline mode selected, we'll proceed to download the dependencies as local sources."
          )

          return!
            logger.Spinner(
              "Downloading Sources...",
              pkgManager.GoOffline(
                map.map,
                [ DownloadOption.Provider provider ],
                token
              )
            )
        else
          logger.LogInformation("Import map generated successfully.")
          return map.map
      }

      let configUpdates = [
        packageUpdates
        PerlaConfig.PerlaWritableField.UseLocalPkgs options.offline
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
