namespace Perla.Testing

open System
open System.Threading
open Microsoft.Playwright
open Microsoft.Extensions.Logging


open FSharp.Control
open FSharp.Control.Reactive
open FSharp.Data.Adaptive

open Spectre.Console
open Spectre.Console.Rendering

open IcedTasks

open System.Collections.Generic
open Perla.Logger

open Perla
open Perla.Types
open Perla.VirtualFs
open Perla.SuaveService

type ClientTestException(message: string, stack: string) =
  inherit Exception(message)

  override _.StackTrace = stack

type ReportedError = {
  test: Test option
  message: string
  stack: string
}

type ReportResult = {
  Stats: TestStats
  Suites: Suite seq
  Errors: ReportedError seq
  Browser: Browser option
}

module Print =
  let HeaderedPanel(title: string, content: IRenderable) =
    Panel(
      content,
      Header = PanelHeader $"[bold white]{title.EscapeMarkup()}[/]"
    )

  let Test(test: Test, error: (string * string) option) =
    let stateColor =
      match test.state with
      | Some "passed" -> "green"
      | Some "failed" -> "red"
      | _ -> "grey"

    let duration =
      TimeSpan.FromMilliseconds(test.duration |> Option.defaultValue 0.)

    let speedColor =
      match test.speed with
      | Some "slow" -> "orange"
      | Some "medium" -> "yellow"
      | Some "fast" -> "green"
      | _ -> "grey"

    let skipped = if test.pending then "skipped" else ""

    [
      Markup(
        $"[bold yellow]{test.fullTitle.EscapeMarkup()}[/] - [bold {stateColor}]{test.state}[/] [{speedColor}]{duration}[/] [dim blue]{skipped}[/]"
      )
      :> IRenderable
      match error with
      | Some error -> ClientTestException(error).GetRenderable()
      | _ -> ()
    ]

  let Suite(suite: Suite, includeTests: bool) : IRenderable =
    let skipped = if suite.pending then "skipped" else ""

    let rows: IRenderable list = [
      Markup($"{suite.title.EscapeMarkup()} - [dim blue]{skipped}[/]")
      if includeTests then
        yield!
          suite.tests
          |> List.map(fun suiteTest -> Test(suiteTest, None) |> List.head)
    ]

    Panel(
      Rows(rows),
      Header = PanelHeader($"[bold yellow]{suite.fullTitle.EscapeMarkup()}[/]")
    )

  let Stats(stats: TestStats) =

    let content =
      let content: IRenderable seq = [
        Markup($"Started {stats.start}")
        Markup(
          $"[yellow]Total Suites[/] [bold yellow]{stats.suites}[/] - [yellow]Total Tests[/] [bold yellow]{stats.tests}[/]"
        )
        Markup($"[green]Passed Tests[/] [bold green]{stats.passes}[/]")
        Markup($"[red]Failed Tests[/] [bold red]{stats.failures}[/]")
        Markup($"[blue]Skipped Tests[/] [bold blue]{stats.pending}[/]")
        match stats.``end`` with
        | Some endTime -> Markup($"Start {endTime}")
        | None -> ()
      ]

      Rows(content)

    Panel(content, Header = PanelHeader("[bold white]Test Results[/]"))

  let Report(stats: TestStats, suites: Suite seq, errors: ReportedError seq) =
    let suites = Array.ofSeq suites
    let errors = Array.ofSeq errors

    let getChartItem(color, label, value) =
      { new IBreakdownChartItem with
          member _.Color = color
          member _.Label = label
          member _.Value = value
      }

    let chart =
      let chart =
        BreakdownChart(ShowTags = true, ShowTagValues = true).FullSize()

      chart.AddItems(
        [
          getChartItem(Color.Green, "Tests Passed", stats.passes)
          getChartItem(Color.Red, "Tests Failed", stats.failures)
        ]
      )

    let endTime = stats.``end`` |> Option.defaultWith(fun _ -> DateTime.Now)
    let difference = endTime - stats.start

    let rows: IRenderable seq = [
      for suite in suites do
        Suite(suite, true)
      if errors.Length > 0 then
        Rule("Test run errors", Style = Style.Parse("bold red"))

        for error in errors do
          let errorMessage =
            match error.test with
            | Some test -> $"{test.fullTitle} -> {error.message}"
            | None -> error.message

          ClientTestException(errorMessage, error.stack).GetRenderable()

        Rule("", Style = Style.Parse("bold red"))
      Panel(
        chart,
        Header =
          PanelHeader(
            $"[yellow] TestRun of {stats.suites} suites and {stats.tests} tests - Duration:[/] [bold yellow]{difference}[/]"
          )
      )
    ]

    rows |> Rows

module Testing =
  let SetupPlaywright(logger: ILogger) = asyncEx {
    logger.LogInformation("Setting up Playwright...")

    try
      do! Async.SwitchToNewThread()
      let exitCode = Program.Main [| "install" |]

      if exitCode = 0 then
        logger.LogInformation("Playwright setup completed successfully.")
      else
        logger.LogError(
          "Playwright setup failed with exit code {exitCode}.",
          exitCode
        )
    with ex ->
      logger.LogError(
        "An error occurred while setting up Playwright: {message}",
        ex
      )

      logger.LogInformation
        "For more information please visit https://playwright.dev/dotnet/docs/browsers"
  }

  let BuildReport(events: TestEvent seq) : ReportResult =
    let browser =
      events |> Seq.tryHead |> Option.map _.Browser |> Option.flatten

    let suiteEnds =
      events
      |> Seq.choose(fun event ->
        match event with
        | SuiteEnd { Suite = suite } -> Some suite
        | _ -> None)

    let errors =
      events
      |> Seq.choose(fun event ->
        match event with
        | TestFailed {
                       Test = test
                       Message = message
                       Stack = stack
                     } ->
          Some {
            test = Some test
            message = message
            stack = stack
          }
        | TestImportFailed { Message = message; Stack = stack } ->
          Some {
            test = None
            message = message
            stack = stack
          }
        | _ -> None)

    let stats =
      events
      |> Seq.tryPick(fun event ->
        match event with
        | SessionEnd { Stats = stats } -> Some stats
        | _ -> None)
      |> Option.defaultWith(fun _ -> {
        suites = 0
        tests = 0
        passes = 0
        pending = 0
        failures = 0
        start = DateTime.Now
        ``end`` = None
      })

    {
      Stats = stats
      Suites = suiteEnds
      Errors = errors
      Browser = browser
    }

  let GetBrowser(browser: Browser, headless: bool, pl: IPlaywright) = task {
    let options = BrowserTypeLaunchOptions(Headless = headless)

    match browser with
    | Browser.Chrome ->
      options.Channel <- "chrome"
      return! pl.Chromium.LaunchAsync(options)
    | Browser.Edge ->
      options.Channel <- "edge"
      return! pl.Chromium.LaunchAsync(options)
    | Browser.Chromium -> return! pl.Chromium.LaunchAsync(options)
    | Browser.Firefox -> return! pl.Firefox.LaunchAsync(options)
    | Browser.Webkit -> return! pl.Webkit.LaunchAsync(options)
  }

  let LogBrowserLog (logger: ILogger) (e: IConsoleMessage) =
    match e.Type with
    | Debug -> ()
    | Info -> logger.LogBrowser(e.Text, LogLevel.Information)
    | Err -> logger.LogBrowser(e.Text, LogLevel.Error)
    | Warning -> logger.LogBrowser(e.Text, LogLevel.Warning)
    | Clear ->
      logger.LogBrowser(
        "Browser Console cleared at: {link}",
        LogLevel.Warning,
        e.Location
      )
    | _ -> logger.LogBrowser(e.Text, LogLevel.Information)

  let GetExecutorForBrowser
    (logger: ILogger, url: string)
    : IBrowser -> Tasks.Task<IPage> =
    fun (iBrowser: IBrowser) -> task {
      let! page =
        iBrowser.NewPageAsync(BrowserNewPageOptions(IgnoreHTTPSErrors = true))

      use _ = page.Console |> Observable.subscribe(LogBrowserLog logger)

      do! page.GotoAsync url |> Task.ignore

      do!
        page.WaitForConsoleMessageAsync(
          PageWaitForConsoleMessageOptions(
            Predicate = fun event -> event.Text = "__perla-test-run-finished"
          )
        )
        |> Task.ignore

      return page
    }

type RunTestOption =
  | Browser of Browser
  | BrowserMode of BrowserMode
  | Headless of bool
  | FileGlobs of Fake.IO.Globbing.LazyGlobbingPattern

[<Interface>]
type TestingService =

  /// Run tests once with specified configuration
  abstract RunOnce:
    options: RunTestOption Set ->
      // change this type to return a list of "ReportResult" which will have these
      // fields plus the browser it was run on
      CancellableTask<ReportResult seq>

  /// Run tests in watch mode with file change monitoring
  abstract RunWatch:
    options: RunTestOption Set * ?cancellationToken: CancellationToken ->
      IAsyncEnumerable<TestEvent>

type TestingServiceArgs = {
  config: PerlaConfig aval
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  FsManager: FileSystem.PerlaFsManager
  ReqHandler: RequestHandler.RequestHandler
  Playwright: IPlaywright
}

module TestingService =
  open System.Threading.Tasks

  let private gatherOptions(options: RunTestOption Set) =
    options
    |> Set.fold
      (fun (browsers, mode, headless, files) option ->
        match option with
        | Browser browser -> browser :: browsers, mode, headless, files
        | BrowserMode m -> browsers, m, headless, files
        | Headless h -> browsers, mode, h, files
        | FileGlobs f ->
          let files = [
            for file in f do
              file
              yield! files
          ]

          browsers, mode, headless, files)
      ([], BrowserMode.Parallel, true, [])


  let pingUntilPong(reqHandler: RequestHandler.RequestHandler, baseUrl) = asyncEx {
    let! cancellationToken = CancellableTask.getCancellationToken()
    let tcs = TaskCompletionSource<bool>()
    let mutable retries = 10

    while not cancellationToken.IsCancellationRequested
          && not tcs.Task.IsCompleted do
      try
        let! ready = reqHandler.PingTestServer baseUrl

        if ready then
          tcs.SetResult true
      with _ ->
        if retries <= 0 then
          tcs.SetResult false
        else
          retries <- retries - 1
          do! Async.Sleep 500

    return! tcs.Task
  }

  let Create(args: TestingServiceArgs) : TestingService =
    // create testing server context
    // start testing server here
    { new TestingService with
        member _.RunOnce(options) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          // gather the testing options
          let browsers, mode, headless, files = gatherOptions options
          // prepare the server testing context

          let events = ResizeArray<TestEvent>()
          use cts = CancellationTokenSource.CreateLinkedTokenSource token

          let inline notifyTestEvent(e: Result<TestEvent, JDeck.DecodeError>) =
            match e with
            | Ok e -> events.Add e
            | Error ex ->
              args.Logger.LogError(
                "Error in decoding test event: {message}",
                ex.message
              )

          // start the server in a background task
          let serverTask = async {
            let! token = Async.CancellationToken

            let context =
              SuaveTestingContext {
                Config = args.config
                FsManager = args.FsManager
                Logger = args.Logger
                NotifyTestEvent = notifyTestEvent
                VirtualFileSystem = args.VirtualFileSystem
              }

            try
              SuaveServer.startServer context token
            with
            // we've likely stopped the server on our own with the cts
            | :? OperationCanceledException -> ()
            | ex ->
              args.Logger.LogError(
                "An error occurred at the testing server: {message}",
                ex.Message
              )
          }

          Async.Start(serverTask, cts.Token)

          let port, host =
            args.config
            |> AVal.map(fun config ->
              config.devServer.port, config.devServer.host)
            |> AVal.force

          let url = $"http://{host}:{port}/"
          let! ready = pingUntilPong(args.ReqHandler, url)

          if not ready then
            return failwith "Testing server did not start successfully."
          else
            let reports = ResizeArray()

            // Local function to run tests for a single browser and collect the report
            let runTestsForBrowser browser = cancellableTask {
              // start the playwright browser
              let! plBrowser =
                Testing.GetBrowser(browser, headless, args.Playwright)
              // create the executor for the browser
              let executor =
                Testing.GetExecutorForBrowser(
                  args.Logger,
                  $"{url}?browser={browser.AsString}"
                )
              // run the tests in the browser
              let! _page = executor plBrowser
              return ()
            }

            match mode with
            | BrowserMode.Parallel ->
              // run tests in parallel for all browsers
              let tasks = browsers |> List.map runTestsForBrowser
              let! _ = CancellableTask.whenAll tasks
              ()
            | BrowserMode.Sequential ->
              for browser in browsers do
                let! _ = runTestsForBrowser browser
                ()

            events
            |> Seq.groupBy _.RunId
            |> Seq.iter(fun (_, events) ->
              let report = Testing.BuildReport events

              reports.Add report)

            // stop the server
            cts.Cancel()
            return reports :> IEnumerable<ReportResult>
        }

        member _.RunWatch(options, ?cancellationToken) =
          failwith "RunWatch is not implemented yet"
    }
