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

  module LiveDashboard =
    type TestResult = {
      Test: Test
      Status: string
      Duration: TimeSpan option
      Timestamp: DateTime
      RunId: Guid
    }

    type TestRunState = {
      // Current activity
      CurrentSuite: string option
      CurrentTest: string option
      Browser: string option
      CurrentRunId: Guid option

      // Statistics (from latest SessionStart/End)
      CurrentStats: TestStats option
      TotalTests: int option

      // Test history (keep last 15 across runs)
      RecentTests: TestResult list

      // Error tracking
      RecentErrors: (string * string * DateTime * Guid) list // message, stack, timestamp, runId

      // Session tracking
      SessionStartTime: DateTime option
      LastActivity: DateTime
      RunCount: int
      CompletedRuns: Guid Set
    }

    module TestRunState =
      let Empty = {
        CurrentSuite = None
        CurrentTest = None
        Browser = None
        CurrentRunId = None
        CurrentStats = None
        TotalTests = None
        RecentTests = []
        RecentErrors = []
        SessionStartTime = None
        LastActivity = DateTime.Now
        RunCount = 0
        CompletedRuns = Set.empty
      }

    let updateState (state: TestRunState) (event: TestEvent) =
      let now = DateTime.Now

      match event with
      | SessionStart sessionEvent ->
        let isNewRun = not(state.CompletedRuns.Contains sessionEvent.RunId)

        {
          state with
              Browser = sessionEvent.Browser |> Option.map(_.AsString)
              CurrentRunId = Some sessionEvent.RunId
              SessionStartTime =
                if state.SessionStartTime.IsNone then
                  Some now
                else
                  state.SessionStartTime
              LastActivity = now
              RunCount = if isNewRun then state.RunCount + 1 else state.RunCount
              CurrentSuite = None
              CurrentTest = None
              CurrentStats = Some sessionEvent.Stats
              TotalTests = Some sessionEvent.TotalTests
        }

      | SuiteStart suiteEvent -> {
          state with
              CurrentSuite = Some suiteEvent.Suite.title
              CurrentTest = None
              LastActivity = now
              CurrentStats = Some suiteEvent.Stats
        }

      | TestPass testEvent ->
        let result = {
          Test = testEvent.Test
          Status = "passed"
          Duration =
            testEvent.Test.duration |> Option.map TimeSpan.FromMilliseconds
          Timestamp = now
          RunId = testEvent.RunId
        }

        {
          state with
              CurrentTest = None
              RecentTests = result :: state.RecentTests |> List.truncate 15
              LastActivity = now
              CurrentStats = Some testEvent.Stats
        }

      | TestFailed testEvent ->
        let result = {
          Test = testEvent.Test
          Status = "failed"
          Duration =
            testEvent.Test.duration |> Option.map TimeSpan.FromMilliseconds
          Timestamp = now
          RunId = testEvent.RunId
        }

        let error = testEvent.Message, testEvent.Stack, now, testEvent.RunId

        {
          state with
              CurrentTest = None
              RecentTests = result :: state.RecentTests |> List.truncate 15
              RecentErrors = error :: state.RecentErrors |> List.truncate 8
              LastActivity = now
              CurrentStats = Some testEvent.Stats
        }

      | SuiteEnd suiteEvent -> {
          state with
              CurrentSuite = None
              LastActivity = now
              CurrentStats = Some suiteEvent.Stats
        }

      | SessionEnd sessionEvent -> {
          state with
              CurrentStats = Some sessionEvent.Stats
              CurrentSuite = None
              CurrentTest = None
              LastActivity = now
              CompletedRuns = state.CompletedRuns.Add sessionEvent.RunId
        }

      | TestImportFailed importEvent ->
        let error =
          importEvent.Message, importEvent.Stack, now, importEvent.RunId

        {
          state with
              RecentErrors = error :: state.RecentErrors |> List.truncate 8
              LastActivity = now
        }

      | TestRunFinished finishedEvent ->
          {
            state with
                LastActivity = now
                CompletedRuns = state.CompletedRuns.Add finishedEvent.RunId
          }

    let createStatusPanel(state: TestRunState) =
      let sessionDuration =
        match state.SessionStartTime with
        | Some start -> DateTime.Now - start
        | None -> TimeSpan.Zero

      let browserInfo = state.Browser |> Option.defaultValue "Unknown"

      let runCountText =
        if state.RunCount > 1 then
          $" (Run #{state.RunCount})"
        else
          ""

      let runIdText =
        match state.CurrentRunId with
        | Some runId ->
          $"\n[dim]Run ID:[/] {runId.ToString().Substring(0, 8)}..."
        | None -> ""

      let content =
        $"""[bold cyan]🔍 Test Watch Active[/]{runCountText}
[dim]Browser:[/] {browserInfo.EscapeMarkup()}
[dim]Session Duration:[/] {sessionDuration.ToString @"hh\:mm\:ss"}
[dim]Last Activity:[/] {state.LastActivity.ToString "HH:mm:ss"}{runIdText}"""

      Panel(
        Markup(content),
        Header = PanelHeader "[bold white]Session Status[/]",
        Border = BoxBorder.Rounded
      )

    let createActivityPanel(state: TestRunState) =
      let content =
        match state.CurrentSuite, state.CurrentTest with
        | Some suite, Some test ->
          $"""[yellow]📁 Suite:[/] {suite.EscapeMarkup()}
[blue]🧪 Running:[/] {test.EscapeMarkup()}
[dim]⏳ Test in progress...[/]"""
        | Some suite, None ->
          $"""[yellow]📁 Suite:[/] {suite.EscapeMarkup()}
[dim]Preparing next test...[/]"""
        | None, _ ->
          match state.CurrentStats with
          | Some stats when stats.``end``.IsSome ->
            "[green]✅ Test run completed[/]\n[dim]Waiting for file changes...[/]"
          | Some _ ->
            "[blue]⚡ Test run in progress...[/]\n[dim]Loading suites...[/]"
          | None -> "[dim]⏳ Waiting for test run to start...[/]"

      Panel(
        Markup(content),
        Header = PanelHeader "[bold white]Current Activity[/]",
        Border = BoxBorder.Rounded
      )

    let createStatsPanel(state: TestRunState) =
      match state.CurrentStats, state.TotalTests with
      | Some stats, totalTests ->
        let total = stats.passes + stats.failures + stats.pending

        let successRate =
          if total > 0 then
            float stats.passes / float total * 100.0
          else
            0.0

        let progressText =
          match totalTests with
          | Some total -> $"\n[dim]Progress:[/] {total - stats.pending}/{total}"
          | None -> ""

        let durationText =
          match stats.``end`` with
          | Some endTime ->
            let duration = endTime - stats.start
            $"\n[dim]Duration:[/] {duration.TotalSeconds:F1}s"
          | None ->
            let elapsed = DateTime.Now - stats.start
            $"\n[dim]Elapsed:[/] {elapsed.TotalSeconds:F1}s"

        let content =
          $"""[green]✅ Passed:[/] [bold green]{stats.passes}[/]
[red]❌ Failed:[/] [bold red]{stats.failures}[/]
[orange3]⏭️ Pending:[/] [bold orange3]{stats.pending}[/]
[dim]Suites:[/] {stats.suites}
[dim]Success Rate:[/] {successRate:F1}%%{progressText}{durationText}"""

        Panel(
          Markup content,
          Header = PanelHeader "[bold white]Test Results[/]",
          Border = BoxBorder.Rounded
        )
      | None, _ ->
        Panel(
          Markup "[dim]No results yet...[/]",
          Header = PanelHeader "[bold white]Test Results[/]",
          Border = BoxBorder.Rounded
        )

    let createRecentTestsPanel(state: TestRunState) =
      if state.RecentTests.IsEmpty then
        Panel(
          Markup "[dim]No tests run yet...[/]",
          Header = PanelHeader "[bold white]Recent Tests[/]",
          Border = BoxBorder.Rounded
        )
      else
        let recentContent =
          state.RecentTests
          |> List.take(min 10 state.RecentTests.Length)
          |> List.mapi(fun i result ->
            let icon =
              match result.Status with
              | "passed" -> "[green]✅[/]"
              | "failed" -> "[red]❌[/]"
              | _ -> "[orange3]⏭️[/]"

            let duration =
              result.Duration
              |> Option.map(fun d -> $" [dim]({d.TotalMilliseconds:F0}ms)[/]")
              |> Option.defaultValue ""

            let timeAgo = DateTime.Now - result.Timestamp

            let timeText =
              if timeAgo.TotalMinutes < 1.0 then
                "now"
              else
                $"{timeAgo.TotalMinutes:F0}m ago"

            let runMarker =
              if
                state.CurrentRunId.IsSome
                && result.RunId = state.CurrentRunId.Value
              then
                " [bold blue]●[/]" // Current run marker
              else
                ""

            $"{icon} {result.Test.title.EscapeMarkup()}{duration} [dim]{timeText}[/]{runMarker}")
          |> String.concat "\n"

        Panel(
          Markup recentContent,
          Header = PanelHeader "[bold white]Recent Tests[/]",
          Border = BoxBorder.Rounded
        )

    let createErrorsPanel(state: TestRunState) =
      if state.RecentErrors.IsEmpty then
        Panel(
          Markup "[green]🎉 No recent errors![/]",
          Header = PanelHeader "[bold white]Recent Errors[/]",
          Border = BoxBorder.Rounded
        )
      else
        let errorContent =
          state.RecentErrors
          |> List.take(min 5 state.RecentErrors.Length)
          |> List.map(fun (message, stack, timestamp, runId) ->
            let timeAgo = DateTime.Now - timestamp

            let timeText =
              if timeAgo.TotalMinutes < 1.0 then
                "now"
              else
                $"{timeAgo.TotalMinutes:F0}m ago"

            let runMarker =
              if
                state.CurrentRunId.IsSome && runId = state.CurrentRunId.Value
              then
                " [bold blue]●[/]"
              else
                ""

            // Truncate long error messages
            let shortMessage =
              if message.Length > 60 then
                $"{message.Substring(0, 57)}..."
              else
                message

            $"[red]💥[/] {shortMessage.EscapeMarkup()} [dim]{timeText}[/]{runMarker}")
          |> String.concat "\n"

        Panel(
          Markup errorContent,
          Header = PanelHeader "[bold white]Recent Errors[/]",
          Border = BoxBorder.Rounded
        )

    let createDashboard(state: TestRunState) =
      let headerPanel =
        Columns [
          createStatusPanel state :> IRenderable
          createActivityPanel state
        ]

      let mainLeftPanel = createStatsPanel state

      let mainRightPanel =
        Panel(
          Markup(
            $"""[dim]Watch Mode Features:[/]
• Auto-refresh on file changes
• Multi-run session tracking
• Real-time test progress
• Error history & analysis

[dim]Legend:[/] [bold blue]●[/] Current run"""
          ),
          Header = PanelHeader("[bold white]Info[/]"),
          Border = BoxBorder.Rounded
        )

      let bottomLeftPanel = createRecentTestsPanel state
      let bottomRightPanel = createErrorsPanel state

      let mainSection = Columns [ mainLeftPanel :> IRenderable; mainRightPanel ]

      let bottomSection =
        Columns [ bottomLeftPanel :> IRenderable; bottomRightPanel ]

      Rows [ headerPanel :> IRenderable; mainSection; bottomSection ]


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
    options: RunTestOption Set * cancellationToken: CancellationToken ->
      IAsyncEnumerable<TestEvent>

type TestingServiceArgs = {
  config: PerlaConfig aval
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  FsManager: FileSystem.PerlaFsManager
  ReqHandler: RequestHandler.RequestHandler
  Directories: PerlaDirectories
  Playwright: IPlaywright
}

module TestingService =
  open System.Threading.Tasks
  open System.Threading.Channels

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
                Directories = args.Directories
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

        member _.RunWatch(options, cancellationToken) = taskSeq {
          // gather the testing options - extract single browser
          let browsers, _, headless, _ = gatherOptions options

          let browser =
            browsers |> List.tryHead |> Option.defaultValue Browser.Chromium

          // Create a channel for test events
          let eventChannel = Channel.CreateUnbounded<TestEvent>()
          let eventWriter = eventChannel.Writer

          let inline notifyTestEvent(e: Result<TestEvent, JDeck.DecodeError>) =
            match e with
            | Ok e ->
              if not(eventWriter.TryWrite e) then
                args.Logger.LogWarning "Failed to write test event to channel"
            | Error ex ->
              args.Logger.LogError(
                "Error in decoding test event: {message}",
                ex.message
              )

          // Start the server once and keep it running
          let serverTask = async {
            let! token = Async.CancellationToken

            let context =
              SuaveTestingContext {
                Config = args.config
                FsManager = args.FsManager
                Logger = args.Logger
                NotifyTestEvent = notifyTestEvent
                Directories = args.Directories
                VirtualFileSystem = args.VirtualFileSystem
              }

            try
              SuaveServer.startServer context token
            with
            // we've likely stopped the server on our own with the provided cancellation token
            | :? OperationCanceledException -> ()
            | ex ->
              args.Logger.LogError(
                "An error occurred at the testing server: {message}",
                ex.Message
              )

              eventWriter.Complete()
          }

          // Start server in background
          Async.Start(serverTask, cancellationToken)

          let port, host =
            args.config
            |> AVal.map(fun config ->
              config.devServer.port, config.devServer.host)
            |> AVal.force

          let url = $"http://{host}:{port}/"

          args.Logger.LogInformation(
            "Checking if the server is alive {url}",
            url
          )

          let! ready = pingUntilPong(args.ReqHandler, url)

          if not ready then
            failwith "Testing server did not start successfully."

          let! plBrowser =
            Testing.GetBrowser(browser, headless, args.Playwright)

          let url = $"{url}?browser={browser.AsString}"

          args.Logger.LogInformation(
            "Starting browser session for {browser} at {url}",
            browser.AsString,
            url
          )

          let executor = Testing.GetExecutorForBrowser(args.Logger, url)

          // Start the browser session - it will stay connected and handle reloads
          let! _page = executor plBrowser

          // Continuously yield events from the channel as they arrive
          let reader = eventChannel.Reader

          while not cancellationToken.IsCancellationRequested do
            try
              let! hasEvent = reader.WaitToReadAsync cancellationToken

              if hasEvent then
                match reader.TryRead() with
                | true, event -> yield event
                | false, _ -> ()
              else
                ()
            with :? OperationCanceledException ->
              ()

          eventWriter.Complete()
        }
    }
