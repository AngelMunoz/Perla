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


module Print =

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

  let Report(stats: TestStats, suites: Suite list, errors: ReportedError list) =
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

  let BuildReport
    (events: TestEvent list)
    : TestStats * Suite list * ReportedError list =
    let suiteEnds =
      events
      |> List.choose(fun event ->
        match event with
        | SuiteEnd(_, _, suite) -> Some suite
        | _ -> None)

    let errors =
      events
      |> List.choose(fun event ->
        match event with
        | TestFailed(_, _, test, message, stack) ->
          Some {
            test = Some test
            message = message
            stack = stack
          }
        | TestImportFailed(_, message, stack) ->
          Some {
            test = None
            message = message
            stack = stack
          }
        | _ -> None)

    let stats =
      events
      |> List.tryPick(fun event ->
        match event with
        | SessionEnd(_, stats) -> Some stats
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

    stats, suiteEnds, errors

  let LiveReport(events: IAsyncEnumerable<TestEvent>) = asyncEx {
    let suites = ResizeArray()
    let errors = ResizeArray()

    let mutable overallStats = {
      suites = 0
      tests = 0
      passes = 0
      pending = 0
      failures = 0
      start = DateTime.Now
      ``end`` = None
    }

    Console.Clear()
    AnsiConsole.Clear()

    for event in events do
      match event with
      | SessionStart(_, stats, totalTests) ->
        overallStats <- {
          overallStats with
              tests = totalTests
              start = DateTime.Now
        }

        AnsiConsole.Clear()
        AnsiConsole.Write(Print.Stats(stats))
      | SessionEnd(_, stats) -> overallStats <- stats
      | TestPass _ -> ()
      | TestFailed(_, _, test, message, stack) ->
        errors.Add {
          test = Some test
          message = message
          stack = stack
        }
      | SuiteStart(_, stats, _) -> overallStats <- stats
      | SuiteEnd(_, stats, suite) ->
        overallStats <- stats
        suites.Add suite
      | TestImportFailed(_, message, stack) ->
        errors.Add {
          test = None
          message = message
          stack = stack
        }
      | TestRunFinished _ ->
        Console.Clear()
        AnsiConsole.Clear()

    return overallStats
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
    : IBrowser -> Async<IPage> =
    fun (iBrowser: IBrowser) -> asyncEx {
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
      CancellableTask<TestStats * Suite list * ReportedError list>

  /// Run tests in watch mode with file change monitoring
  abstract RunWatch:
    options: RunTestOption Set * ?cancellationToken: CancellationToken ->
      IAsyncEnumerable<TestEvent>

type TestingServiceArgs = {
  config: PerlaConfig aval
  Logger: ILogger
  VirtualFileSystem: VirtualFileSystem
  FsManager: FileSystem.PerlaFsManager
  Playwright: IPlaywright
}

module TestingService =
  let Create(args: TestingServiceArgs) : TestingService =
    // create testing server context
    // start testing server here
    { new TestingService with
        member _.RunOnce(options) =
          failwith "RunOnce is not implemented yet"

        member _.RunWatch(options, ?cancellationToken) =
          failwith "RunWatch is not implemented yet"
    }
