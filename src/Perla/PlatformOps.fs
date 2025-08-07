namespace Perla

open System.IO
open System.Text
open System.Runtime.InteropServices
open IcedTasks
open FSharp.UMX
open Perla.Units
open Perla.Types
open Perla.Logger
open Microsoft.Extensions.Logging
open CliWrap
open CliWrap.Buffered
open CliWrap.EventStream
open FSharp.Control
open System.Collections.Generic
open System.Threading

[<RequireQualifiedAccess>]
type ProcessEvent =
  | Started of processId: int
  | StandardOutput of text: string
  | StandardError of text: string
  | Exited of exitCode: int

type ProcessResult = {
  ExitCode: int
  StandardOutput: string
  StandardError: string
}

type PlatformOps =
  abstract member IsWindows: unit -> bool
  abstract member PlatformString: unit -> string
  abstract member ArchString: unit -> string

  // Process execution methods
  abstract CheckDotnetToolVersion:
    toolName: string -> CancellableTask<ProcessResult>

  abstract InstallDotnetTool: toolName: string -> CancellableTask<ProcessResult>

  abstract RunFable:
    project: string<SystemPath> *
    outDir: string<SystemPath> option *
    extension: string<FileExtension> ->
      CancellableTask<unit>

  abstract StreamFable:
    project: string<SystemPath> *
    outDir: string<SystemPath> option *
    extension: string<FileExtension> *
    ?cancellationToken: CancellationToken ->
      IAsyncEnumerable<ProcessEvent>

  abstract IsFableAvailable: unit -> CancellableTask<bool>

  abstract RunEsbuildTransform:
    esbuildPath: string<SystemPath> *
    sourceCode: string *
    loader: string option *
    target: string *
    jsxAutomatic: bool *
    jsxImportSource: string option *
    tsconfig: string option ->
      CancellableTask<string>

  abstract RunEsbuildCss:
    esbuildPath: string<SystemPath> *
    workingDir: string<SystemPath> *
    entrypoint: string *
    outdir: string *
    minify: bool *
    fileLoaders: Map<string, string> ->
      CancellableTask<unit>

  abstract RunEsbuildJs:
    esbuildPath: string<SystemPath> *
    workingDir: string<SystemPath> *
    entrypoint: string *
    outdir: string *
    tsconfig: string option *
    config: EsbuildConfig ->
      CancellableTask<unit>

module PlatformOps =
  open System
  open System.Runtime.ExceptionServices
  open System.Threading.Tasks

  let private getDotnetExecutable(isWindows: bool) =
    let ext = if isWindows then ".exe" else ""
    $"dotnet{ext}"

  let private addProject
    (project: string<SystemPath>)
    (args: Builders.ArgumentsBuilder)
    =
    args.Add $"{project}"

  let private addOutDir
    (outdir: string<SystemPath> option)
    (args: Builders.ArgumentsBuilder)
    =
    match outdir with
    | Some outdir -> args.Add([ "-o"; $"{outdir}" ])
    | None -> args

  let private addExtension
    (extension: string<FileExtension>)
    (args: Builders.ArgumentsBuilder)
    =
    args.Add([ "-e"; $"{extension}" ])

  let private addWatch (watch: bool) (args: Builders.ArgumentsBuilder) =
    if watch then args.Add "watch" else args

  let private buildFableCommand
    (isWindows: bool)
    (project: string<SystemPath>)
    (outDir: string<SystemPath> option)
    (extension: string<FileExtension>)
    (watch: bool)
    =
    let execBin = getDotnetExecutable isWindows

    Cli
      .Wrap(execBin)
      .WithArguments(fun args ->
        args.Add "fable"
        |> addWatch watch
        |> addProject project
        |> addOutDir outDir
        |> addExtension extension
        |> ignore)

  let private buildEsbuildArguments
    (target: string)
    (jsxAutomatic: bool)
    (jsxImportSource: string option)
    (tsconfig: string option)
    (loader: string option)
    =
    fun (args: Builders.ArgumentsBuilder) ->
      let args = args.Add $"--target={target}"
      let args = args.Add "--format=esm"
      let args = if jsxAutomatic then args.Add "--jsx=automatic" else args

      let args =
        match jsxImportSource with
        | Some source -> args.Add $"--jsx-import-source={source}"
        | None -> args

      let args =
        match tsconfig with
        | Some config -> args.Add $"--tsconfig-raw={config}"
        | None -> args

      let args =
        match loader with
        | Some l -> args.Add $"--loader={l}"
        | None -> args

      args.Add "--keep-names"

  let private buildEsbuildFileLoaders(fileLoaders: Map<string, string>) =
    fun (args: Builders.ArgumentsBuilder) ->
      fileLoaders
      |> Map.fold
        (fun (acc: Builders.ArgumentsBuilder) ext loader ->
          acc.Add $"--loader:{ext}={loader}")
        args

  let private buildEsbuildConfig(config: EsbuildConfig) =
    fun (args: Builders.ArgumentsBuilder) ->
      let args = args.Add $"--target={config.ecmaVersion}"
      let args = args.Add "--format=esm"
      let args = args.Add "--bundle"
      let args = if config.minify then args.Add "--minify" else args

      let args =
        if config.jsxAutomatic then
          args.Add "--jsx=automatic"
        else
          args

      let args =
        match config.jsxImportSource with
        | Some source -> args.Add $"--jsx-import-source={source}"
        | None -> args

      let args =
        config.externals
        |> Seq.fold
          (fun (acc: Builders.ArgumentsBuilder) ext ->
            acc.Add $"--external:{ext}")
          args

      let args =
        config.aliases
        |> Map.fold
          (fun (acc: Builders.ArgumentsBuilder) alias target ->
            acc.Add $"--alias:{alias}={target}")
          args

      args

  let Create(logger: ILogger) =
    { new PlatformOps with
        member _.IsWindows() =
          RuntimeInformation.IsOSPlatform OSPlatform.Windows

        member _.PlatformString() =
          if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            "win32"
          else if RuntimeInformation.IsOSPlatform OSPlatform.Linux then
            "linux"
          else if RuntimeInformation.IsOSPlatform OSPlatform.OSX then
            "darwin"
          else if RuntimeInformation.IsOSPlatform OSPlatform.FreeBSD then
            "freebsd"
          else
            failwith "Unsupported OS"

        member _.ArchString() =
          match RuntimeInformation.OSArchitecture with
          | Architecture.Arm -> "arm"
          | Architecture.Arm64 -> "arm64"
          | Architecture.X64 -> "x64"
          | Architecture.X86 -> "ia32"
          | _ -> failwith "Unsupported Architecture"

        member this.CheckDotnetToolVersion(toolName) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let dotnetCmd =
            Cli
              .Wrap(getDotnetExecutable(this.IsWindows()))
              .WithArguments([ toolName; "--version" ])
              .WithValidation(CommandResultValidation.None)

          let! result = dotnetCmd.ExecuteBufferedAsync(token)

          return {
            ExitCode = result.ExitCode
            StandardOutput = result.StandardOutput
            StandardError = result.StandardError
          }
        }

        member this.InstallDotnetTool(toolName) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let dotnetCmd =
            Cli
              .Wrap(getDotnetExecutable(this.IsWindows()))
              .WithArguments(
                [ "tool"; "install"; toolName; "--create-manifest-if-needed" ]
              )
              .WithValidation(CommandResultValidation.None)

          let! result = dotnetCmd.ExecuteBufferedAsync(token)

          return {
            ExitCode = result.ExitCode
            StandardOutput = result.StandardOutput
            StandardError = result.StandardError
          }
        }

        member this.RunFable(project, outDir, extension) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let command =
            (buildFableCommand (this.IsWindows()) project outDir extension false)
              .WithStandardErrorPipe(PipeTarget.ToDelegate logger.LogError)
              .WithStandardOutputPipe(
                PipeTarget.ToDelegate logger.LogInformation
              )
              .ExecuteAsync(cancellationToken = token)

          do! command.Task :> System.Threading.Tasks.Task
          return ()
        }

        member this.StreamFable
          (project, outDir, extension, ?cancellationToken)
          =
          taskSeq {
            let token = defaultArg cancellationToken CancellationToken.None

            let command =
              buildFableCommand (this.IsWindows()) project outDir extension true

            try
              for event: CommandEvent in command.ListenAsync(token) do
                match event with
                | :? StartedCommandEvent as started ->
                  logger.LogInformation(
                    "Fable started with pid: [{ProcessId}]",
                    started.ProcessId
                  )

                  ProcessEvent.Started started.ProcessId
                | :? StandardOutputCommandEvent as stdout ->
                  logger.LogInformation stdout.Text
                  ProcessEvent.StandardOutput stdout.Text
                | :? StandardErrorCommandEvent as stderr ->
                  logger.LogError stderr.Text
                  ProcessEvent.StandardError stderr.Text
                | :? ExitedCommandEvent as exited ->
                  logger.LogInformation(
                    "Fable exited with code: [{ExitCode}]",
                    exited.ExitCode
                  )

                  ProcessEvent.Exited exited.ExitCode
                | _ -> ()
            with
            | :? OperationCanceledException ->
              logger.LogWarning "Fable streaming was cancelled."
              ()
            | :? System.AggregateException as ex when
              ex.InnerExceptions
              |> Seq.findIndex(fun innerEx ->
                innerEx.GetType() = typeof<TaskCanceledException>)
              >= 0
              ->
              logger.LogWarning "Fable streaming was cancelled."
              ()
            | ex -> ExceptionDispatchInfo.Throw ex
          }

        member this.IsFableAvailable() = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let command =
            Cli
              .Wrap(getDotnetExecutable(this.IsWindows()))
              .WithArguments([ "fable"; "--version" ])
              .WithValidation(CommandResultValidation.None)
              .ExecuteAsync(cancellationToken = token)

          let! result = command.Task
          return result.ExitCode = 0
        }

        member _.RunEsbuildTransform
          (
            esbuildPath,
            sourceCode,
            loader,
            target,
            jsxAutomatic,
            jsxImportSource,
            tsconfig
          ) =
          cancellableTask {
            let! token = CancellableTask.getCancellationToken()
            use writer = new MemoryStream()

            let command =
              Cli
                .Wrap(esbuildPath |> UMX.untag)
                .WithStandardInputPipe(PipeSource.FromString sourceCode)
                .WithStandardOutputPipe(PipeTarget.ToStream writer)
                .WithStandardErrorPipe(PipeTarget.ToDelegate logger.LogError)
                .WithArguments(fun args ->
                  buildEsbuildArguments
                    target
                    jsxAutomatic
                    jsxImportSource
                    tsconfig
                    loader
                    args
                  |> _.Add("--log-level=warning")
                  |> ignore)
                .WithValidation(CommandResultValidation.None)

            let! _ = command.ExecuteAsync(cancellationToken = token)
            return Encoding.UTF8.GetString(writer.ToArray())
          }

        member _.RunEsbuildCss
          (esbuildPath, workingDir, entrypoint, outdir, minify, fileLoaders)
          =
          cancellableTask {
            let! token = CancellableTask.getCancellationToken()
            let output = Path.Combine(outdir, entrypoint)

            let command =
              Cli
                .Wrap(esbuildPath |> UMX.untag)
                .WithWorkingDirectory(workingDir |> UMX.untag)
                .WithStandardOutputPipe(
                  PipeTarget.ToDelegate logger.LogInformation
                )
                .WithStandardErrorPipe(PipeTarget.ToDelegate logger.LogError)
                .WithArguments(fun argsBuilder ->
                  argsBuilder.Add(entrypoint).Add("--bundle")
                  |> _.Add("--log-level=warning")
                  |> (if minify then _.Add("--minify") else id)
                  |> _.Add($"--outfile={output}")
                  |> _.Add("--preserve-symlinks")
                  |> buildEsbuildFileLoaders fileLoaders
                  |> ignore)

            let! _ = command.ExecuteAsync(cancellationToken = token)
            return ()
          }

        member _.RunEsbuildJs
          (esbuildPath, workingDir, entrypoint, outdir, tsconfig, config)
          =
          cancellableTask {
            let! token = CancellableTask.getCancellationToken()

            logger.LogDebug(
              "Starting Esbuild for entrypoint '{entrypoint}': cwd: {cwd} - outdir: {outdir}",
              entrypoint,
              workingDir,
              outdir
            )

            let output = Path.Combine(outdir, entrypoint)

            let addTsConfig(args: Builders.ArgumentsBuilder) =
              match tsconfig with
              | Some tsConfig -> args.Add($"--tsconfig-raw={tsConfig}")
              | None -> args

            let command =
              Cli
                .Wrap(esbuildPath |> UMX.untag)
                .WithWorkingDirectory(workingDir |> UMX.untag)
                .WithStandardOutputPipe(
                  PipeTarget.ToDelegate logger.LogInformation
                )
                .WithStandardErrorPipe(PipeTarget.ToDelegate logger.LogError)
                .WithArguments(fun argsBuilder ->
                  argsBuilder.Add entrypoint
                  |> buildEsbuildConfig config
                  |> _.Add("--log-level=warning")
                  |> _.Add($"--outfile={output}")
                  |> _.Add("--preserve-symlinks")
                  |> addTsConfig
                  |> buildEsbuildFileLoaders config.fileLoaders
                  |> ignore)

            logger.LogTrace(
              "Executing Esbuild command: {Command} {Arguments}",
              command.TargetFilePath,
              command.Arguments
            )

            let! _ = command.ExecuteAsync(cancellationToken = token)
            return ()
          }
    }
