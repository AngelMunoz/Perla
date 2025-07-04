namespace Perla.Handlers

open System
open System.IO

open System.Threading
open System.Threading.Tasks

open AngleSharp
open AngleSharp.Html.Parser
open Microsoft.Playwright
open Spectre.Console

open Zio.FileSystems
open Zio

open FSharp.Control
open FSharp.Control.Reactive

open IcedTasks

open FSharp.UMX
open FsToolkit.ErrorHandling

open Perla
open Perla.Types
open Perla.Units
open Perla.Server
open Perla.Build
open Perla.Logger
open Perla.FileSystem
open Perla.VirtualFs
open Perla.Esbuild
open Perla.Extensibility
open Perla.Fable
open Perla.Json
open Perla.Scaffolding
open Perla.Configuration.Types
open Perla.Configuration

open Perla.Plugins
open Perla.Plugins.Registry
open Perla.PackageManager
open Perla.PackageManager.Types

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

type BuildOptions = {
  enablePreview: bool
  enablePreloads: bool
  rebuildImportMap: bool
}

type SetupOptions = {
  installTemplates: bool
  skipPrompts: bool
}

type SearchOptions = { package: string; page: int }

type ShowPackageOptions = { package: string }

type ListTemplatesOptions = { format: ListFormat }

type AddPackageOptions = {
  package: string
  version: string option
  source: PkgManager.DownloadProvider option
  alias: string option
}

type RemovePackageOptions = {
  package: string
  alias: string option
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
}

type RestoreOptions = {
  source: PkgManager.DownloadProvider option
}

type TestingOptions = {
  browsers: Browser seq option
  files: string seq option
  skip: string seq option
  watch: bool option
  headless: bool option
  browserMode: BrowserMode option
}

type DescribeOptions = {
  properties: string[] option
  current: bool
}

[<Struct>]
type PathOperation =
  | AddOrUpdate of
    addImport: string<BareImport> *
    addPath: string<ResolutionUrl>
  | Remove of removeImport: string

type PathsOptions = { operation: PathOperation }

module Templates =

  type FoundTemplate =
    | Repository of PerlaTemplateRepository
    | Existing of TemplateItem

  type TemplateNotFoundCases =
    | NoQueryParams
    | ParentTemplateNotFound
    | ChildTemplateNotFound

  [<RequireQualifiedAccess; Struct>]
  type TemplateOperation =
    | Add of repoName: (string * string * string)
    | Update of foundRepo: PerlaTemplateRepository

  let private updateTemplate
    (template: PerlaTemplateRepository)
    (branch: string)
    (context: StatusContext)
    =
    context.Status <-
      $"Download and extracting template {template.ToFullNameWithBranch}"

    Templates.Update({ template with branch = branch })

  let private addTemplate
    (user: string)
    (repository: string)
    (branch: string)
    (context: StatusContext)
    =
    context.Status <-
      $"Download and extracting template {user}/{repository}:{branch}"

    Templates.Add(user, repository, branch)


  let List(options: ListTemplatesOptions) =
    let results =
      Templates.ListTemplateItems()
      |> List.groupBy(fun x -> x.parent)
      |> List.choose(fun (parentId, children) ->
        match Templates.FindOne(TemplateSearchKind.Id parentId) with
        | Some parent -> Some(parent, children)
        | None -> None)
      |> List.collect(fun (parent, children) ->
        children |> List.map(fun child -> (parent, child)))

    match options.format with
    | ListFormat.HumanReadable ->
      let table =
        Table()
          .AddColumns(
            [| "Name"; "perla new -t"; "perla new -id"; "Repository name" |]
          )

      for column in table.Columns do
        column.Alignment <- Justify.Center

      for parent, template in results do
        let name: Rendering.IRenderable =
          Markup($"[bold green]{template.name}[/]")

        let shortname = Markup($"[bold yellow]{template.shortName}[/]")
        let group = Markup($"[bold blue]{template.group}[/]")

        let repositoryName =
          Markup($"[bold blue]{parent.ToFullNameWithBranch}[/]")

        table.AddRow([| name; shortname; group; repositoryName |]) |> ignore

      AnsiConsole.Write table
      0
    | ListFormat.TextOnly ->
      let columns = Columns([| "Name"; "perla new -t"; "perla new -id" |])

      AnsiConsole.Write columns

      let rows = [|
        for parent, template in results do
          let name = Markup($"[bold green]{template.name}[/]")
          let shortname = Markup($"[bold yellow]{template.shortName}[/]")
          let group = Markup($"[bold blue]{template.group}[/]")

          let repositoryName =
            TextPath(
              UMX.untag parent.ToFullNameWithBranch,
              LeafStyle = Style(Color.Green),
              StemStyle = Style(Color.Yellow),
              SeparatorStyle = Style(Color.Blue)
            )

          Columns(name, shortname, group, repositoryName)
          :> Rendering.IRenderable
      |]

      rows |> Rows |> AnsiConsole.Write

      0

  let AddOrUpdate(operation: TemplateOperation) = taskResult {
    let listTemplates() =
      List { format = ListFormat.HumanReadable } |> ignore

      Logger.log(
        "[bold yellow]perla[/] [bold blue]new [/] [bold blue] <PROJECT_NAME>[/]",
        escape = false
      )

      Logger.log "Feel free to create a new perla project"

    match operation with
    | TemplateOperation.Add repoName ->
      let username, repository, branch = repoName

      do!
        Logger.spinner(
          $"Adding templates from: {username}/{repository}:{branch}",
          (addTemplate username repository branch)
        )
        |> TaskResult.ignore

      listTemplates()
      return ()
    | TemplateOperation.Update template ->
      do! taskResult {
        let! result =
          Logger.spinner(
            $"Updating templates from: {template.ToFullNameWithBranch}",
            (updateTemplate template template.branch)
          )

        if result then
          return ()
        else
          return!
            Error
              "We were unable to update the existing [bold red]templates[/]."
      }

      listTemplates()
      return ()
  }

  let Remove(template: PerlaTemplateRepository) : Result<unit, string> =
    Templates.Delete(TemplateSearchKind.Id template._id)
    |> Result.requireTrue
      "There was an error while trying to delete this template."

module Fable =
  let StartFable(config: PerlaConfig, cancel: CancellationToken) = task {
    match config.fable with
    | Some fable -> do! Fable.Start(fable, cancellationToken = cancel) :> Task
    | None ->
      Logger.log(
        "No Fable configuration provided, skipping fable",
        target = PrefixKind.Build
      )
  }

module FsMonitor =
  let FirstCompileDone isWatch (observable: IObservable<FableEvent>) =
    observable
    |> Observable.choose (function
      | FableEvent.WaitingForChanges -> Some()
      | _ -> None)
    |> (fun obs ->
      if isWatch then
        Observable.first obs
      else
        Observable.takeLast 1 obs)
    |> AsyncSeq.ofObservableBuffered
    |> AsyncSeq.iter ignore

  let FileChanges
    (index: string, mountDirectories, perlaFilesChanges, plugins: string list)
    =
    let perlaFilesChanges =
      perlaFilesChanges
      |> Observable.map(fun event ->
        let name, path, extension =
          match event with
          | PerlaFileChange.Index ->
            (Path.GetFileName index, Path.GetFullPath index, ".html")
          | PerlaFileChange.PerlaConfig ->
            (Constants.PerlaConfigName,
             UMX.untag FileSystem.PerlaConfigPath,
             ".json")
          | PerlaFileChange.ImportMap ->
            (Constants.ImportMapName,
             UMX.untag(FileSystem.GetConfigPath Constants.ImportMapName None),
             ".importmap")

        {
          serverPath = UMX.tag "/"
          userPath = UMX.tag "/"
          oldPath = None
          oldName = None
          changeType = ChangeKind.Changed
          path = UMX.tag path
          name = UMX.tag name
        },
        { content = ""; extension = extension })

    VirtualFileSystem.GetFileChangeStream mountDirectories
    |> VirtualFileSystem.ApplyVirtualOperations plugins
    |> Observable.merge perlaFilesChanges

module Esbuild =

  let Run
    (
      config: PerlaConfig,
      workingDirectory: UPath,
      fs: IFileSystem,
      (css, js, standalone):
        string<ServerUrl> seq * string<ServerUrl> seq * string<ServerUrl> seq,
      externals: string seq,
      cancel: CancellationToken
    ) =
    let js = seq {
      yield! js
      yield! standalone
    }

    let taggedCwd = fs.ConvertPathToInternal workingDirectory

    let cssTasks = backgroundTask {
      for css in css do
        let path =
          UPath.Combine(workingDirectory, UMX.untag css)
          |> fs.ConvertPathToInternal

        let targetPath =
          Path.Combine(UMX.untag config.build.outDir, UMX.untag css)
          |> Path.GetFullPath
          |> Path.GetDirectoryName

        let tsk =
          Esbuild
            .ProcessCss(taggedCwd, path, config.esbuild, targetPath)
            .ExecuteAsync(cancel)

        do! tsk.Task :> Task
    }

    let jsTasks = backgroundTask {
      let aliases =
        // remove all the paths that are not relative
        let aliases =
          config.paths |> Map.filter(fun _ v -> (UMX.untag v).StartsWith("./"))

        // if the env is enabled, also check if we want to produce the file
        // if the user doesn't want to produce the file it is likely that they
        // will provide said file at runtime and we don't want to make esbuild remove that import
        if config.enableEnv && config.build.emitEnvFile then
          // envPaths should be at the root of the server "/" so we can prefix it with a dot
          // to make it relative to the root of the server, when build command runs it will
          // be produced there
          let path = UMX.tag $".{config.envPath}"
          Map.add (UMX.tag Constants.EnvBareImport) path aliases
        else
          aliases

      for js in js do
        let path =
          UPath.Combine(workingDirectory, UMX.untag js)
          |> fs.ConvertPathToInternal

        let targetPath =
          Path.Combine(UMX.untag config.build.outDir, UMX.untag js)
          |> Path.GetFullPath
          |> Path.GetDirectoryName

        let tsk =
          Esbuild
            .ProcessJS(
              taggedCwd,
              path,
              config.esbuild,
              targetPath,
              externals,
              aliases
            )
            .ExecuteAsync(cancel)

        do! tsk.Task :> Task
    }

    Task.WhenAll(cssTasks, jsTasks)

module Testing =
  let RunOnce
    (
      pl: IPlaywright,
      browserMode: BrowserMode,
      browsers: Browser seq,
      isHeadless: bool,
      url: string
    ) =
    let browsers = asyncSeq {
      for browser in browsers do
        let! iBrowser =
          Testing.GetBrowser(pl, browser, isHeadless) |> Async.AwaitTask

        browser, iBrowser
    }

    let runTest(browser, iBrowser) = async {
      let executor = Testing.GetExecutor(url, browser)
      do! executor iBrowser |> Async.AwaitTask
      do! iBrowser.CloseAsync() |> Async.AwaitTask
      return! iBrowser.DisposeAsync().AsTask() |> Async.AwaitTask
    }

    match browserMode with
    | BrowserMode.Parallel ->
      browsers |> AsyncSeq.iterAsyncParallelThrottled 2 runTest
    | BrowserMode.Sequential -> browsers |> AsyncSeq.iterAsync runTest

  let LiveRun
    (
      pl: IPlaywright,
      browser: Browser,
      isHeadless: bool,
      url: string,
      fileChanges: IObservable<unit>,
      broadcast: IObservable<TestEvent>,
      cancel: CancellationToken
    ) =
    task {

      let! iBrowser = Testing.GetBrowser(pl, browser, isHeadless)

      let liveExecutor =
        Testing.GetLiveExecutor(
          url,
          browser,
          fileChanges |> Observable.map ignore
        )

      use _ = Testing.PrintReportLive broadcast
      let! pageReloads = liveExecutor iBrowser

      use _ =
        pageReloads
        |> Observable.subscribeSafe(fun _ ->
          Logger.log $"Live Reload: Page Reloaded After Change")

      while not cancel.IsCancellationRequested do
        do! Async.Sleep(TimeSpan.FromSeconds(1.))
    }

[<RequireQualifiedAccess>]
module Handlers =
  open Perla.Database

  let runSetup(options: SetupOptions) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    Logger.log "Perla will set up the following resources:"
    Logger.log "- Esbuild"
    Logger.log "- Default Templates"

    Logger.log
      "After that you should be able to run perla commands without extra effort."

    do! FileSystem.SetupEsbuild(UMX.tag Constants.Esbuild_Version, token)

    Checks.SaveEsbuildBinPresent(UMX.tag Constants.Esbuild_Version) |> ignore

    Logger.log("[bold green]esbuild[/] has been setup!", escape = false)

    let username, repository, branch =
      PerlaTemplateRepository.DefaultTemplatesRepository

    let getTemplate() =
      TemplateSearchKind.FullName(username, repository)
      |> Templates.FindOne
      |> Option.map(fun template ->
        Templates.AddOrUpdate(Templates.TemplateOperation.Update template))
      |> Option.defaultWith(fun () ->
        Templates.AddOrUpdate(
          Templates.TemplateOperation.Add(username, repository, branch)
        ))

    match options.skipPrompts, options.installTemplates with
    | false, true
    | true, true ->
      let! operation = getTemplate()

      match operation with
      | Ok() ->
        Checks.SaveTemplatesPresent() |> ignore
        Checks.SaveSetup() |> ignore
        return 0
      | Error err ->
        Logger.log err
        return 1
    | true, false ->
      if AnsiConsole.Confirm("Add default templates?", false) then
        let! operation = getTemplate()

        match operation with
        | Ok() ->
          Checks.SaveTemplatesPresent() |> ignore
          Checks.SaveSetup() |> ignore
          return 0
        | Error err ->
          Logger.log err
          return 1
      else
        Logger.log "Skip installing templates"
        Checks.SaveSetup() |> ignore
        return 0
    | false, false ->
      Logger.log "Skip installing templates"
      Checks.SaveSetup() |> ignore
      return 0
  }

  let runNew(options: ProjectOptions) = cancellableTask {
    let! cancellationToken = CancellableTask.getCancellationToken()
    Logger.log "Creating new project..."
    let mutable mentionQuickCommand = false

    let inline byId() =
      options.byId
      |> Option.map UMX.tag<TemplateGroup>
      |> Option.map QuickAccessSearch.Group

    let queryParam =
      options.byShortName
      |> Option.map QuickAccessSearch.ShortName
      |> Option.orElseWith byId

    let foundRepo = result {
      let! query = queryParam |> Result.requireSome Templates.NoQueryParams

      match query with
      | QuickAccessSearch.Name name ->
        let user, template, child = getTemplateAndChild name

        let! found =
          (match user, child with
           | Some user, _ ->
             Templates.FindOne(TemplateSearchKind.FullName(user, template))
           | None, _ ->
             Templates.FindOne(TemplateSearchKind.Repository template))
          |> Result.requireSome Templates.ParentTemplateNotFound

        return Templates.Repository found
      | others ->
        let! found =
          Templates.FindTemplateItems(others)
          |> Result.requireHead Templates.ChildTemplateNotFound

        return Templates.Existing found
    }

    let inline TemplateItemPromptConverter(item: TemplateItem) =
      let description =
        item.description |> Option.defaultValue "No description provided."

      $"{item.name} - {item.shortName}: {description[0..30]}"

    let inline TemplateConfigPromptConverter(item: TemplateConfigurationItem) =
      $"{item.name} - {item.shortName}: {item.description[0..30]}"

    let selectedItem =
      match foundRepo with
      | Ok(Templates.Repository repo) ->
        mentionQuickCommand <- true

        let selection =
          SelectionPrompt(
            Title = $"Available templates for {repo.name}",
            Converter = TemplateConfigPromptConverter
          )
            .AddChoices(repo.templates)

        task {
          let! result =
            selection.ShowAsync(AnsiConsole.Console, cancellationToken)

          return
            Templates.FindTemplateItems(QuickAccessSearch.Id result.childId)
            |> List.tryHead
        }
      | Ok(Templates.Existing item) -> Task.FromResult(Some item)
      | Error Templates.NoQueryParams ->
        mentionQuickCommand <- true

        let selection =
          SelectionPrompt(
            Title = "Welcome to Perla, please select a template to start with",
            Converter = TemplateItemPromptConverter
          )
            .AddChoices(Templates.ListTemplateItems())

        task {
          try
            let! result =
              selection.ShowAsync(AnsiConsole.Console, cancellationToken)

            return Some result
          with :? TaskCanceledException ->
            return None
        }
      | Error Templates.ChildTemplateNotFound
      | Error Templates.ParentTemplateNotFound ->
        if
          AnsiConsole.Ask(
            "We were not able to find the template you were looking for, Would you like to check the existing templates?",
            false
          )
        then
          mentionQuickCommand <- true

          let selection =
            SelectionPrompt(
              Title = "Please select a template to start with",
              Converter = TemplateItemPromptConverter
            )
              .AddChoices(Templates.ListTemplateItems())

          task {
            try
              let! result =
                selection.ShowAsync(AnsiConsole.Console, cancellationToken)

              return Some result
            with :? TaskCanceledException ->
              return None
          }
        else
          Task.FromResult None

    let! item = selectedItem

    match item with
    | Some item ->
      let scriptContent =
        Templates.GetTemplateScriptContent(TemplateScriptKind.Template item)
        |> Option.orElseWith(fun () -> option {
          let! repo = Templates.FindOne(TemplateSearchKind.Id item.parent)

          return!
            TemplateScriptKind.Repository repo
            |> Templates.GetTemplateScriptContent
        })

      let targetPath =
        $"./{options.projectName}" |> Path.GetFullPath |> UMX.tag<UserPath>

      FileSystem.WriteTplRepositoryToDisk(
        item.fullPath,
        targetPath,
        ?payload = scriptContent
      )


      if mentionQuickCommand then
        let ffCmd =
          $"perla new [blue]<my-project-name>[/] [yellow]-t {item.shortName}[/]"

        let groupCmd =
          $"perla new [blue]<my-project-name>[/] [yellow]-id {item.group}[/]"

        Logger.log(
          $"You can run this template directly with:\n{ffCmd}\n{groupCmd}",
          escape = false
        )

      let chdir = $"cd ./{options.projectName}"
      let serve = "perla serve"

      Logger.log(
        $"Project [green]{options.projectName}[/] created!, to get started run:\n{chdir}\n{serve}",
        escape = false
      )

      return 0

    | None ->
      Logger.log "No selection was available..."

      Logger.log
        "please check for typos or run 'perla new <my-project-name>' to run the templating wizard"

      return 1
  }

  let runTemplate(options: TemplateRepositoryOptions) = cancellableTask {
    let template = voption {
      let! username, repository, _ =
        parseFullRepositoryName options.fullRepositoryName

      return!
        TemplateSearchKind.FullName(username, repository) |> Templates.FindOne
    }

    let updateRepo() = cancellableTask {
      match template with
      | ValueSome template ->
        Logger.log $"Template {template.ToFullNameWithBranch} already exists."

        match!
          Templates.AddOrUpdate(Templates.TemplateOperation.Update template)
        with
        | Ok() -> return 0
        | Error err ->
          Logger.log(err, escape = false)
          return 1
      | ValueNone ->
        Logger.log "We were unable to parse the repository name."

        Logger.log(
          "please ensure that the repository name is in the format: [bold blue]username/repository:branch[/]",
          escape = false
        )

        return 1
    }

    match options.operation with
    | RunTemplateOperation.List listFormat ->
      return Templates.List { format = listFormat }
    | RunTemplateOperation.Add ->
      match parseFullRepositoryName options.fullRepositoryName with
      | ValueSome template ->
        match!
          Templates.AddOrUpdate(Templates.TemplateOperation.Add template)
        with
        | Ok() -> return 0
        | Error err ->
          Logger.log(err, escape = false)
          return 1
      | ValueNone ->
        Logger.log "We were unable to parse the repository name."

        Logger.log(
          "please ensure that the repository name is in the format: [bold blue]username/repository:branch[/]",
          escape = false
        )

        return 1
    | RunTemplateOperation.Update -> return! updateRepo()
    | RunTemplateOperation.Update
    | RunTemplateOperation.Add when template.IsSome -> return! updateRepo()
    | RunTemplateOperation.Remove ->
      match template with
      | ValueSome template ->
        Logger.log $"Removing template '{template.ToFullNameWithBranch}'..."

        match Templates.Remove(template) with
        | Ok() ->
          Logger.log "Template removed successfully."
          return 0
        | Error err ->
          Logger.log(err, escape = false)
          return 1
      | ValueNone ->
        Logger.log "We were unable to parse the repository name."

        Logger.log(
          "please ensure that the repository name is in the format: [bold blue]username/repository:branch[/]",
          escape = false
        )

        return 1
  }

  let runBuild(options: BuildOptions) = cancellableTask {
    let! cancellationToken = CancellableTask.getCancellationToken()
    let config = ConfigurationManager.CurrentConfig

    do! Fable.StartFable(config, cancellationToken)

    let outDir = UMX.untag config.build.outDir

    try
      Directory.Delete(outDir, true)
      Directory.CreateDirectory(outDir) |> ignore
    with _ ->
      ()

    match PluginLoader.Load<FileSystem, Esbuild>(config.esbuild) with
    | Ok plugins -> Logger.log $"Loaded {plugins.Length} plugins"
    | Error err ->
      for err in err do
        match err with
        | NoPluginFound name -> Logger.log($"Plugin {name} not found")
        | EvaluationFailed(ex) ->
          Logger.log($"Failed to evaluate plugin", ex = ex)
        | SessionExists
        | BoundValueMissing -> Logger.log "Failed to load plugins"
        | AlreadyLoaded name -> Logger.log($"Plugin {name} already loaded")

    do!
      Logger.spinner(
        "Mounting Virtual File System",
        VirtualFileSystem.Mount config
      )

    let tempDirectory = VirtualFileSystem.CopyToDisk() |> UMX.tag


    Logger.log(
      $"Copying Processed files to {tempDirectory}",
      target = PrefixKind.Build
    )

    if config.build.emitEnvFile then
      Logger.log "Writing Env File"

      Build.EmitEnvFile(config, tempDirectory)

    use fs = new PhysicalFileSystem()

    use browserCtx = new BrowsingContext()

    let externals = Build.GetExternals(config)

    let index = FileSystem.IndexFile(config.index)

    let document = browserCtx.GetService<IHtmlParser>().ParseDocument index

    let tmp = UMX.untag tempDirectory |> fs.ConvertPathFromInternal

    let css, js, standalone = Build.GetEntryPoints(document)

    do!
      Logger.spinner(
        "Transpiling CSS and JS Files",
        Esbuild.Run(
          config,
          tmp,
          fs,
          (css, js, standalone),
          externals,
          cancellationToken
        )
      )
      :> Task

    let css = [
      yield! css
      yield!
        fs.EnumerateFiles(tmp, "*.css", SearchOption.AllDirectories)
        |> Seq.map(fun path -> fs.ConvertPathToInternal path |> UMX.tag)
    ]


    let outDir =
      config.build.outDir
      |> UMX.untag
      |> Path.GetFullPath
      |> fs.ConvertPathFromInternal

    // copy any glob files
    Build.CopyGlobs(config.build, tempDirectory)

    let map =
      FileSystem
        .GetImportMap()
        .AddResolutions(config.paths)
        .AddEnvResolution(config)

    let indexContent = Build.GetIndexFile(document, css, js, map)
    // Always copy the index file at the end to avoid
    // clashing with any index.html file in the root of the virtual file system
    fs.WriteAllText(UPath.Combine(outDir, "index.html"), indexContent)

    Logger.log $"Cleaning up temp dir {tempDirectory}"

    try
      Directory.Delete(UMX.untag tempDirectory, true)
    with ex ->
      Logger.log($"Failed to delete {tempDirectory}", ex = ex)


    if options.enablePreview then
      let app = Server.GetStaticServer(config)
      do! app.StartAsync(cancellationToken)

      app.Urls
      |> Seq.iter(fun url -> Logger.log($"Listening at: {url}", target = Serve))

      while not cancellationToken.IsCancellationRequested do
        do! Async.Sleep(1000)

      do! app.StopAsync(cancellationToken)

    return 0
  }

  let runServe(options: ServeOptions) = cancellableTask {
    let! cancellationToken = CancellableTask.getCancellationToken()

    let cliArgs = [
      match options.port with
      | Some port -> DevServerField.Port port
      | None -> ()
      match options.host with
      | Some host -> DevServerField.Host host
      | None -> ()
      match options.ssl with
      | Some ssl -> DevServerField.UseSSL ssl
      | None -> ()
      // Don't minify sources in dev mode
      DevServerField.MinifySources false
    ]

    ConfigurationManager.UpdateFromCliArgs(serverOptions = cliArgs)


    let config = ConfigurationManager.CurrentConfig

    let fableEvents =
      match config.fable with
      | Some fable ->
        Fable.Observe(fable, cancellationToken = cancellationToken)
      | None ->
        let sub = Subject.replay
        sub.OnNext(FableEvent.WaitingForChanges)
        sub

    use _ =
      fableEvents
      |> Observable.subscribeSafe(fun events ->
        match events with
        | FableEvent.Log msg -> Logger.log(msg.EscapeMarkup())
        | FableEvent.ErrLog msg ->
          Logger.log $"[bold red]{msg.EscapeMarkup()}[/]"
        | FableEvent.WaitingForChanges -> ())

    do! FsMonitor.FirstCompileDone true fableEvents

    match PluginLoader.Load<FileSystem, Esbuild>(config.esbuild) with
    | Ok plugins -> Logger.log $"Loaded {plugins.Length} plugins"
    | Error err ->
      for err in err do
        match err with
        | NoPluginFound name -> Logger.log($"Plugin {name} not found")
        | EvaluationFailed(ex) ->
          Logger.log($"Failed to evaluate plugin", ex = ex)
        | SessionExists
        | BoundValueMissing -> Logger.log "Failed to load plugins"
        | AlreadyLoaded name -> Logger.log($"Plugin {name} already loaded")

    do! VirtualFileSystem.Mount(config)

    let perlaChanges =
      FileSystem.ObservePerlaFiles(UMX.untag config.index, cancellationToken)

    let fileChanges =
      FsMonitor.FileChanges(
        UMX.untag config.index,
        config.mountDirectories,
        perlaChanges,
        config.plugins
      )

    // TODO: Grab these from esbuild
    let compilerErrors = Observable.empty

    let mutable app = Server.GetServerApp(config, fileChanges, compilerErrors)
    do! app.StartAsync(cancellationToken)

    app.Urls
    |> Seq.iter(fun url -> Logger.log($"Listening at: {url}", target = Serve))

    perlaChanges
    |> Observable.throttle(TimeSpan.FromMilliseconds(500.))
    |> Observable.choose (function
      | PerlaFileChange.PerlaConfig -> Some()
      | _ -> None)
    |> Observable.map(fun _ -> app.StopAsync() |> Async.AwaitTask)
    |> Observable.switchAsync
    |> Observable.add(fun _ ->
      ConfigurationManager.UpdateFromFile()
      app <- Server.GetServerApp(config, fileChanges, compilerErrors)
      app.StartAsync(cancellationToken) |> ignore)

    while not cancellationToken.IsCancellationRequested do
      do! Async.Sleep(TimeSpan.FromSeconds(1.))

    return 0
  }

  let runTesting(options: TestingOptions) = cancellableTask {
    let! cancellationToken = CancellableTask.getCancellationToken()

    ConfigurationManager.UpdateFromCliArgs(
      testingOptions = [
        match options.browsers with
        | Some value -> TestingField.Browsers value
        | None -> ()
        match options.files with
        | Some value -> TestingField.Includes value
        | None -> ()
        match options.skip with
        | Some value -> TestingField.Excludes value
        | None -> ()
        match options.watch with
        | Some value -> TestingField.Watch value
        | None -> ()
        match options.headless with
        | Some value -> TestingField.Headless value
        | None -> ()
        match options.browserMode with
        | Some value -> TestingField.BrowserMode value
        | None -> ()
      ]
    )

    let config = {
      ConfigurationManager.CurrentConfig with
          mountDirectories =
            ConfigurationManager.CurrentConfig.mountDirectories
            |> Map.add
              (UMX.tag<ServerUrl> "/tests")
              (UMX.tag<UserPath> "./tests")
    }

    let isWatch = config.testing.watch

    let fableEvents =
      match config.testing.fable with
      | Some fable -> Fable.Observe(fable, isWatch)
      | None -> Observable.single FableEvent.WaitingForChanges

    fableEvents
    |> Observable.add(fun events ->
      match events with
      | FableEvent.Log msg -> Logger.log(msg.EscapeMarkup())
      | FableEvent.ErrLog msg ->
        Logger.log $"[bold red]{msg.EscapeMarkup()}[/]"
      | FableEvent.WaitingForChanges -> ())

    do! FsMonitor.FirstCompileDone isWatch fableEvents

    match PluginLoader.Load<FileSystem, Esbuild>(config.esbuild) with
    | Ok plugins -> Logger.log $"Loaded {plugins.Length} plugins"
    | Error err ->
      for err in err do
        match err with
        | NoPluginFound name -> Logger.log($"Plugin {name} not found")
        | EvaluationFailed(ex) ->
          Logger.log($"Failed to evaluate plugin", ex = ex)
        | SessionExists
        | BoundValueMissing -> Logger.log "Failed to load plugins"
        | AlreadyLoaded name -> Logger.log($"Plugin {name} already loaded")

    do! VirtualFileSystem.Mount config

    let perlaChanges =
      FileSystem.ObservePerlaFiles(UMX.untag config.index, cancellationToken)

    let fileChanges =
      FsMonitor.FileChanges(
        UMX.untag config.index,
        config.mountDirectories,
        perlaChanges,
        config.plugins
      )
    // TODO: Grab these from esbuild
    let compilerErrors = Observable.empty

    let config = {
      config with
          devServer = {
            config.devServer with
                liveReload = isWatch
          }
    }

    let events = Subject<TestEvent>.broadcast

    let! dependencies =
      Dependencies.GetMapAndDependencies Seq.empty
      |> TaskResult.map(fun (deps, map) ->
        let map = map.AddResolutions(config.paths).AddEnvResolution config
        deps, map)
      |> TaskResult.defaultValue(
        Seq.empty,
        FileSystem.GetImportMap().AddResolutions(config.paths).AddEnvResolution
          config
      )

    let mutable app =
      Server.GetTestingApp(
        config,
        dependencies,
        events,
        fileChanges,
        compilerErrors,
        config.testing.includes
      )
    // Keep this before initializing the server
    // otherwise it will always say that the port is occupied
    let http, _ =
      Server.GetServerURLs
        config.devServer.host
        config.devServer.port
        config.devServer.useSSL

    do! app.StartAsync(cancellationToken)

    perlaChanges
    |> Observable.choose (function
      | PerlaFileChange.PerlaConfig -> Some()
      | _ -> None)
    |> Observable.map(fun _ -> app.StopAsync() |> Async.AwaitTask)
    |> Observable.switchAsync
    |> Observable.map(fun _ ->
      ConfigurationManager.UpdateFromFile()
      app <- Server.GetServerApp(config, fileChanges, compilerErrors)
      app.StartAsync(cancellationToken) |> Async.AwaitTask)
    |> Observable.switchAsync
    |> Observable.add ignore

    use! pl = Playwright.CreateAsync()

    let testConfig = config.testing

    if not isWatch then
      do!
        Testing.RunOnce(
          pl,
          testConfig.browserMode,
          testConfig.browsers,
          testConfig.headless,
          http
        )

      events.OnCompleted()

      events
      |> Observable.toEnumerable
      |> Seq.toList
      |> Testing.BuildReport
      |> Print.Report

      return 0
    else
      let browser = config.testing.browsers |> Seq.head
      let fileChanges = fileChanges |> Observable.map ignore

      do!
        Testing.LiveRun(
          pl,
          browser,
          testConfig.headless,
          http,
          fileChanges,
          events,
          cancellationToken
        )

      events.OnCompleted()

      events
      |> Observable.toEnumerable
      |> Seq.toList
      |> Testing.BuildReport
      |> Print.Report

      return 0
  }

  let runSearchPackage(options: SearchOptions) = cancellableTask {
    do! Dependencies.Search(options.package, options.page)
    return 0
  }

  let runShowPackage(options: ShowPackageOptions) = cancellableTask {
    do! Dependencies.Show(options.package)
    return 0
  }

  let runAddResolution(options: PathsOptions) =
    let config = ConfigurationManager.CurrentConfig

    let importMap =
      // remove existing custom resolutions from the import map
      FileSystem.GetImportMap().RemoveResolutions(config.paths)

    let updatedPaths =
      match options.operation with
      // Either adding or updating will overwrite the existing key in the custom resolution's map
      | AddOrUpdate(bareImport, path) -> config.paths |> Map.add bareImport path
      | Remove(removeImport) ->
        config.paths |> Map.remove(UMX.tag<BareImport> removeImport)

    // write updated values to both config and import map to disk
    ConfigurationManager.WriteFieldsToFile(
      [ PerlaWritableField.Paths updatedPaths ]
    )
    // add the updated custom resolutions to the import map
    FileSystem.WriteImportMap(importMap.AddResolutions(updatedPaths)) |> ignore

    CancellableTask.singleton 0

  let runAddPackage(options: AddPackageOptions) = cancellableTask {
    ConfigurationManager.UpdateFromCliArgs(?provider = options.source)

    let config = ConfigurationManager.CurrentConfig
    let package, packageVersion = parsePackageName options.package

    let version =
      match packageVersion with
      | Some version -> $"@{version}"
      | None -> ""

    let looksLikeCustomResolution =
      package
      |> Seq.filter(fun c -> c = '/')
      // This should account for packages like:
      // - @shoelace-style/shoelace/dist/components/button/button.js
      // - lit/directives/join.js
      |> Seq.length > 1

    let addAsResolution =
      if options.alias.IsSome then
        true
      else
        looksLikeCustomResolution
        && AnsiConsole.Confirm(
          $"[bold yellow]{package}[/] looks like a nested or a custom import. Do you want to add it as a custom path?",
          true
        )

    let importMap = FileSystem.GetImportMap().RemoveResolutions(config.paths)

    Logger.log "Updating Import Map..."

    let! map =
      Logger.spinner(
        $"Adding: [bold yellow]{package}{version}[/]",
        Dependencies.Add($"{package}{version}", importMap)
      )

    match map, addAsResolution with
    | Ok map, true ->
      match map.imports |> Map.tryFind package with
      | Some resolution ->
        let name = defaultArg options.alias package

        let deps, devDeps =
          Dependencies.LocateDependenciesFromMapAndConfig(map, config)

        ConfigurationManager.WriteFieldsToFile [
          PerlaWritableField.Dependencies deps
        ]

        return!
          runAddResolution {
            operation =
              AddOrUpdate(
                UMX.tag<BareImport> name,
                UMX.tag<ResolutionUrl> resolution
              )
          }
      | None ->
        Logger.log
          "We were unable to find the package in the import map. This is likely a bug, please report it."

        return 1
    | Ok map, false ->
      let newDep = {
        package = package
        version =
          packageVersion |> Option.defaultValue "0.0.0" |> UMX.tag<Semver>
      }

      let dependencies =
        let config = {
          config with
              dependencies = [ yield! config.dependencies; newDep ] |> Set
        }

        let deps, _ =
          Dependencies.LocateDependenciesFromMapAndConfig(map, config)

        PerlaWritableField.Dependencies deps

      ConfigurationManager.WriteFieldsToFile [ dependencies ]

      FileSystem.WriteImportMap(map.AddResolutions(config.paths)) |> ignore

      return 0
    | Error err, _ ->
      Logger.log($"[bold red]{err}[/]", escape = false)
      return 1
  }

  let runRemovePackage(options: RemovePackageOptions) = cancellableTask {
    let name = options.package
    Logger.log($"Removing: [red]{name}[/]", escape = false)
    let config = ConfigurationManager.CurrentConfig

    let map = FileSystem.GetImportMap().RemoveResolutions(config.paths)

    let dependencies, _ =
      let deps, devDeps =
        Dependencies.LocateDependenciesFromMapAndConfig(
          map,
          {
            config with
                dependencies =
                  config.dependencies |> Set.filter(fun d -> d.package <> name)
          }
        )

      deps, devDeps

    ConfigurationManager.WriteFieldsToFile [
      PerlaWritableField.Dependencies dependencies
    ]

    match! Dependencies.Restore(dependencies |> Seq.map _.package) with
    | Ok map ->
      FileSystem.WriteImportMap(map.AddResolutions(config.paths)) |> ignore

      return 0
    | Error err ->
      Logger.log $"[bold red]{err}[/]"
      return 1
  }

  let runListPackages(options: ListPackagesOptions) = cancellableTask {
    let config = ConfigurationManager.CurrentConfig

    match options.format with
    | ListFormat.HumanReadable ->
      Logger.log(
        "[bold green]Installed packages[/] [yellow](alias: packageName@version)[/]\n",
        escape = false
      )

      let prodTable =
        dependencyTable(config.dependencies, "Production Dependencies")

      AnsiConsole.Write prodTable
      AnsiConsole.WriteLine()

    | ListFormat.TextOnly ->

      config.dependencies |> Json.ToText |> AnsiConsole.Write

    return 0
  }

  let runRestoreImportMap(options: RestoreOptions) = cancellableTask {
    ConfigurationManager.UpdateFromCliArgs(?provider = options.source)

    let config = ConfigurationManager.CurrentConfig

    Logger.log "Regenerating import map..."

    match!
      Logger.spinner(
        "Fetching dependencies...",
        Dependencies.Restore(config.dependencies |> Seq.map _.package)
      )
    with
    | Ok response ->

      FileSystem.WriteImportMap(response.AddResolutions(config.paths)) |> ignore

      return 0
    | Error err ->
      Logger.log(
        $"[bold red]An error happened restoring the import map:[/]",
        escape = false
      )

      Logger.log err
      return 1
  }

  let runDescribePerla(options: DescribeOptions) = cancellableTask {
    let {
          properties = props
          current = current
        } =
      options

    let config = ConfigurationManager.CurrentConfig

    let table = Table().AddColumn("Property")

    AnsiConsole.Write(FigletText("Perla.json"))
    let descriptions = FileSystem.DescriptionsFile

    match props, current with
    | Some props, true ->
      table.AddColumns("Value", "Explanation") |> ignore

      for prop in props do
        let description =
          descriptions.Value |> Map.tryFind prop |> Option.defaultValue ""

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

    | Some props, false ->
      table.AddColumns("Description", "Default Value") |> ignore

      for prop in props do
        let description =
          descriptions.Value |> Map.tryFind prop |> Option.defaultValue ""

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

    | None, false ->
      table.AddColumn("Description") |> ignore

      for KeyValue(key, value) in descriptions.Value do
        table.AddRow(key, value) |> ignore
    | None, true ->
      table.AddColumns("Current Value", "Description") |> ignore

      for KeyValue(key, description) in descriptions.Value do
        match key with
        | TopLevelProp prop ->
          table.AddRow(
            Text(prop),
            Defaults.PerlaConfig[prop] |> Option.defaultValue(Text ""),
            Text(description)
          )
          |> ignore
        | NestedProp props ->
          table.AddRow(
            Text(key),
            Defaults.PerlaConfig[props] |> Option.defaultValue(Text ""),
            Text(description)
          )
          |> ignore
        | TripleNestedProp props ->
          table.AddRow(
            Text(key),
            Defaults.PerlaConfig[props] |> Option.defaultValue(Text ""),
            Text(description)
          )
          |> ignore
        | InvalidPropPath ->
          table.AddRow(
            Text(
              key,
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
