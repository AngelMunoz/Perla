namespace Perla.Logger

open System.Threading.Tasks
open Spectre.Console
open Spectre.Console.Extensions
open System
open System.Runtime.InteropServices
open Serilog
open Serilog.Core
open Serilog.Events


[<Struct>]
type PrefixKind =
  | Log
  | Scaffold
  | Build
  | Serve
  | Esbuild
  | Browser

[<Struct>]
type LogEnding =
  | NewLine
  | SameLine

[<RequireQualifiedAccess>]
module Constants =

  [<Literal>]
  let LogPrefix = "Perla:"

  [<Literal>]
  let EsbuildPrefix = "Esbuild:"

  [<Literal>]
  let ScaffoldPrefix = "Scaffolding:"

  [<Literal>]
  let BuildPrefix = "Build:"

  [<Literal>]
  let ServePrefix = "Serve:"

  [<Literal>]
  let BrowserPrefix = "Browser:"

type PrefixKind with

  member this.AsString =
    match this with
    | PrefixKind.Log -> Constants.LogPrefix
    | PrefixKind.Scaffold -> Constants.ScaffoldPrefix
    | PrefixKind.Build -> Constants.BuildPrefix
    | PrefixKind.Serve -> Constants.ServePrefix
    | PrefixKind.Esbuild -> Constants.EsbuildPrefix
    | PrefixKind.Browser -> Constants.BrowserPrefix

module Internals =
  let format (prefix: PrefixKind list) (message: string) : FormattableString =
    let prefix =
      prefix |> List.fold (fun cur next -> $"{cur}{next.AsString}") ""

    $"[yellow]{prefix}[/] {message}"

type Logger =

  static member logCustom
    (
      message,
      [<Optional>] ?ex: exn,
      [<Optional>] ?prefixes: PrefixKind list,
      [<Optional>] ?ending: LogEnding,
      [<Optional>] ?escape: bool
    ) =
    let prefixes =
      let prefixes = defaultArg prefixes [ Log ]

      if prefixes.Length = 0 then [ Log ] else prefixes

    let escape = defaultArg escape true
    let formatted = Internals.format prefixes message

    match (defaultArg ending NewLine) with
    | NewLine ->
      if escape then
        AnsiConsole.MarkupLineInterpolated formatted
      else
        AnsiConsole.MarkupLine(formatted.ToString())
    | SameLine ->
      if escape then
        AnsiConsole.MarkupInterpolated formatted
      else
        AnsiConsole.Markup(formatted.ToString())

    match ex with
    | Some ex ->
#if DEBUG
      AnsiConsole.WriteException(
        ex,
        ExceptionFormats.ShortenEverything ||| ExceptionFormats.ShowLinks
      )
#else
      AnsiConsole.WriteException(
        ex,
        ExceptionFormats.ShortenPaths ||| ExceptionFormats.ShowLinks
      )
#endif
    | None -> ()

  static member log
    (
      message,
      [<Optional>] ?ex: exn,
      [<Optional>] ?target: PrefixKind,
      [<Optional>] ?escape: bool
    ) =
    let target =
      defaultArg target Log
      |> function
        | PrefixKind.Log -> [ Log ]
        | PrefixKind.Scaffold -> [ Log; Scaffold ]
        | PrefixKind.Build -> [ Log; Build ]
        | PrefixKind.Serve -> [ Log; Serve ]
        | PrefixKind.Esbuild -> [ Log; Esbuild ]
        | PrefixKind.Browser -> [ Log; Browser ]

    Logger.logCustom(message, prefixes = target, ?ex = ex, ?escape = escape)

  static member spinner<'Operation>
    (title: string, task: Task<'Operation>)
    : Task<'Operation> =
    let status = AnsiConsole.Status()
    status.Spinner <- Spinner.Known.Dots
    status.StartAsync(title, (fun _ -> task))

  static member spinner<'Operation>
    (title: string, task: Async<'Operation>)
    : Task<'Operation> =
    let status = AnsiConsole.Status()
    status.Spinner <- Spinner.Known.Dots
    status.StartAsync(title, (fun _ -> task |> Async.StartAsTask))


  static member inline spinner<'Operation>
    (
      title: string,
      [<InlineIfLambda>] operation: StatusContext -> Task<'Operation>,
      ?target: PrefixKind
    ) : Task<'Operation> =
    let prefix =
      defaultArg target Log
      |> function
        | PrefixKind.Log -> [ Log ]
        | PrefixKind.Scaffold -> [ Log; Scaffold ]
        | PrefixKind.Build -> [ Log; Build ]
        | PrefixKind.Serve -> [ Log; Serve ]
        | PrefixKind.Esbuild -> [ Log; Esbuild ]
        | PrefixKind.Browser -> [ Log; Browser ]

    let title = Internals.format prefix title
    let status = AnsiConsole.Status()
    status.Spinner <- Spinner.Known.Dots
    status.StartAsync(title.ToString(), operation)

  static member inline spinner<'Operation>
    (
      title: string,
      [<InlineIfLambda>] operation: StatusContext -> Async<'Operation>,
      ?target: PrefixKind
    ) : Task<'Operation> =
    let prefix =
      defaultArg target Log
      |> function
        | PrefixKind.Log -> [ Log ]
        | PrefixKind.Scaffold -> [ Log; Scaffold ]
        | PrefixKind.Build -> [ Log; Build ]
        | PrefixKind.Serve -> [ Log; Serve ]
        | PrefixKind.Esbuild -> [ Log; Esbuild ]
        | PrefixKind.Browser -> [ Log; Browser ]

    let title = Internals.format prefix title
    let status = AnsiConsole.Status()
    status.Spinner <- Spinner.Known.Dots

    status.StartAsync(
      title.ToString(),
      (fun ctx -> operation ctx |> Async.StartAsTask)
    )

module Logger =
  open Microsoft.Extensions.Logging

  let getPerlaLogger() =
    { new ILogger with
        member _.Log(logLevel, eventId, state, ex, formatter) =
          let format = formatter.Invoke(state, ex)
          Logger.log format

        member _.IsEnabled(level) = true

        member _.BeginScope(state) =
          { new IDisposable with
              member _.Dispose() = ()
          }
    }

// Enricher to add prefix information to log events
type PerlaPrefixEnricher(prefixes: PrefixKind Set) =
  interface ILogEventEnricher with
    member _.Enrich(logEvent, propertyFactory) =
      for prefix in prefixes do
        match prefix with
        | Log ->
          propertyFactory.CreateProperty($"PlLog", Constants.LogPrefix)
          |> logEvent.AddPropertyIfAbsent
        | Scaffold ->
          propertyFactory.CreateProperty(
            $"PlScaffold",
            Constants.ScaffoldPrefix
          )
          |> logEvent.AddPropertyIfAbsent
        | Build ->
          propertyFactory.CreateProperty($"PlBuild", Constants.BuildPrefix)
          |> logEvent.AddPropertyIfAbsent
        | Serve ->
          propertyFactory.CreateProperty($"PlServe", Constants.ServePrefix)
          |> logEvent.AddPropertyIfAbsent
        | Esbuild ->
          propertyFactory.CreateProperty($"PlEsbuild", Constants.EsbuildPrefix)
          |> logEvent.AddPropertyIfAbsent
        | Browser ->
          propertyFactory.CreateProperty($"PlBrowser", Constants.BrowserPrefix)
          |> logEvent.AddPropertyIfAbsent

// Custom sink to write to Spectre.Console
type SpectreSink() =
  interface ILogEventSink with
    member _.Emit(logEvent: LogEvent) =
      let timestamp = logEvent.Timestamp.ToString("HH:mm:ss")

      let levelString =
        match logEvent.Level with
        | LogEventLevel.Verbose -> "VRB"
        | LogEventLevel.Debug -> "DBG"
        | LogEventLevel.Information -> "INF"
        | LogEventLevel.Warning -> "WRN"
        | LogEventLevel.Error -> "ERR"
        | LogEventLevel.Fatal -> "FTL"
        | _ -> "???"

      let levelColor =
        match logEvent.Level with
        | LogEventLevel.Verbose
        | LogEventLevel.Debug -> "grey"
        | LogEventLevel.Information -> "blue"
        | LogEventLevel.Warning -> "yellow"
        | LogEventLevel.Error
        | LogEventLevel.Fatal -> "red"
        | _ -> "white"

      let prefix =
        logEvent.Properties
        |> Seq.filter(fun kvp -> kvp.Key.StartsWith "Pl")
        |> Seq.map(fun kvp ->
          let color =
            match kvp.Key with
            | "PlLog" -> "teal"
            | "PlScaffold" -> "deepskyblue4"
            | "PlBuild" -> "green"
            | "PlServe" -> "purple3"
            | "PlEsbuild" -> "dodgerblue3"
            | "PlBrowser" -> "darkgoldenrod"
            | _ -> "white"

          let prefixText = kvp.Value.ToString().Trim('"')
          $"[{color}]{prefixText}[/]")
        |> String.concat ""

      let message = logEvent.RenderMessage()

      AnsiConsole.MarkupLine
        $"[[grey]{timestamp}[/] [{levelColor}]{levelString}[/] {prefix}]  {message}"

      match logEvent.Exception with
      | null -> ()
      | ex -> AnsiConsole.WriteException ex

[<AutoOpen>]
module SerilogExtensions =
  open Serilog.Configuration


  type LoggerSinkConfiguration with
    member this.Spectre() = this.Sink(SpectreSink())

  type LoggerEnrichmentConfiguration with
    member this.WithPerlaPrefix(prefixes: PrefixKind Set) =
      this.With(PerlaPrefixEnricher prefixes)

[<RequireQualifiedAccess>]
module PerlaSeriLogger =
  let create(prefixes: PrefixKind Set) =
    LoggerConfiguration()
      .Enrich.WithPerlaPrefix(prefixes)
      .Enrich.FromLogContext()
      .WriteTo.Spectre()
      .CreateLogger()

  let createFromConfig(config: LoggerConfiguration) = config.CreateLogger()

open Microsoft.Extensions.Logging
open Serilog.Extensions.Logging

[<AutoOpen>]
module ILoggerExtensions =
  open Serilog.Context

  type Microsoft.Extensions.Logging.ILogger with


    member _.Spinner<'Operation>
      (title: string, task: Task<'Operation>)
      : Task<'Operation> =
      let status = AnsiConsole.Status()
      status.Spinner <- Spinner.Known.Dots
      status.StartAsync(title, (fun _ -> task))

    member _.Spinner<'Operation>
      (title: string, task: Async<'Operation>)
      : Async<'Operation> =
      let status = AnsiConsole.Status()
      status.Spinner <- Spinner.Known.Dots

      status.StartAsync(title, (fun _ -> task |> Async.StartAsTask))
      |> Async.AwaitTask

    member inline this.LogScaffold
      (message: string, logLevel: LogLevel, [<ParamArray>] args: obj[])
      =
      use _ = LogContext.PushProperty("PlScaffold", Constants.ScaffoldPrefix)

      this.Log(logLevel, message, args)

    member inline this.LogBuild
      (message: string, logLevel: LogLevel, [<ParamArray>] args: obj[])
      =
      use _ = LogContext.PushProperty("PlBuild", Constants.BuildPrefix)
      this.Log(logLevel, message, args)

    member inline this.LogServe
      (message: string, logLevel: LogLevel, [<ParamArray>] args: obj[])
      =
      use _ = LogContext.PushProperty("PlServe", Constants.ServePrefix)
      this.Log(logLevel, message, args)

    member inline this.LogEsbuild
      (message: string, logLevel: LogLevel, [<ParamArray>] args: obj[])
      =
      use _ = LogContext.PushProperty("PlEsbuild", Constants.EsbuildPrefix)

      this.Log(logLevel, message, args)

    member inline this.LogBrowser
      (message: string, logLevel: LogLevel, [<ParamArray>] args: obj[])
      =
      use _ = LogContext.PushProperty("PlBrowser", Constants.BrowserPrefix)

      this.Log(logLevel, message, args)

  type ILoggingBuilder with
    member this.AddPerlaLogger(?prefixes: PrefixKind list) =
      let prefixes = defaultArg prefixes [ Log ]
      let serilogLogger = PerlaSeriLogger.create(Set.ofList prefixes)
      this.AddProvider(new SerilogLoggerProvider(serilogLogger))
