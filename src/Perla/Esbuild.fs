namespace Perla.Esbuild

open System
open System.IO
open System.Runtime.InteropServices
open CliWrap

open IcedTasks
open FSharp.UMX
open FSharp.Data.Adaptive

open Perla
open Perla.Types
open Perla.Units
open Perla.Logger
open Perla.FileSystem
open Perla.Plugins
open System.Text

[<RequireQualifiedAccess; Struct>]
type LoaderType =
  | Typescript
  | Tsx
  | Jsx
  | Css

type EsbuildServiceArgs = {
  Cwd: string<SystemPath>
  PerlaFsManager: PerlaFsManager
  Logger: Microsoft.Extensions.Logging.ILogger
}

[<Interface>]
type EsbuildService =

  abstract ProcessJS:
    entrypoint: string * outdir: string * config: EsbuildConfig ->
      CancellableTask<unit>

  abstract ProcessCss:
    entrypoint: string * outdir: string * config: EsbuildConfig ->
      CancellableTask<unit>

  abstract GetPlugin: config: EsbuildConfig -> PluginInfo

[<RequireQualifiedAccess>]
module Esbuild =
  open Microsoft.Extensions.Logging
  open System.Threading.Tasks

  let addEsExternals (externals: string seq) (args: Builders.ArgumentsBuilder) =
    externals |> Seq.map(fun ex -> $"--external:{ex}") |> args.Add

  let addIsBundle (isBundle: bool) (args: Builders.ArgumentsBuilder) =

    if isBundle then args.Add("--bundle") else args

  let addMinify (minify: bool) (args: Builders.ArgumentsBuilder) =

    if minify then args.Add("--minify") else args

  let addFormat (format: string) (args: Builders.ArgumentsBuilder) =
    args.Add $"--format={format}"

  let addTarget (target: string) (args: Builders.ArgumentsBuilder) =
    args.Add $"--target={target}"

  let addOutDir (outDir: string) (args: Builders.ArgumentsBuilder) =
    args.Add $"--outdir={outDir}"

  let addOutFile
    (outfile: string<SystemPath>)
    (args: Builders.ArgumentsBuilder)
    =
    args.Add $"--outfile={outfile}"

  /// This is used for known file types when compiling on the fly or at build time
  let addLoader (loader: LoaderType option) (args: Builders.ArgumentsBuilder) =
    match loader with
    | Some loader ->
      let loader =
        match loader with
        | LoaderType.Typescript -> "ts"
        | LoaderType.Tsx -> "tsx"
        | LoaderType.Jsx -> "jsx"
        | LoaderType.Css -> "css"

      args.Add $"--loader={loader}"
    | None -> args

  /// This one is used for unknown file assets like pngs, svgs font files and similar assets
  let addDefaultFileLoaders
    (loaders: Map<string, string>)
    (args: Builders.ArgumentsBuilder)
    =
    let loaders = loaders |> Map.toSeq

    for extension, loader in loaders do
      args.Add $"--loader:{extension}={loader}" |> ignore

    args

  let addJsxAutomatic (addAutomatic: bool) (args: Builders.ArgumentsBuilder) =
    if addAutomatic then args.Add "--jsx=automatic" else args

  let addJsxImportSource
    (importSource: string option)
    (args: Builders.ArgumentsBuilder)
    =
    match importSource with
    | Some source -> args.Add $"--jsx-import-source={source}"
    | None -> args

  let addJsxFragment
    (fragment: string option)
    (args: Builders.ArgumentsBuilder)
    =
    match fragment with
    | Some fragment -> args.Add $"--jsx-fragment={fragment}"
    | None -> args

  let addInlineSourceMaps(args: Builders.ArgumentsBuilder) =
    args.Add "--sourcemap=inline"

  let addInjects (injects: string seq) (args: Builders.ArgumentsBuilder) =

    injects |> Seq.map(fun inject -> $"--inject:{inject}") |> args.Add

  let addTsconfigRaw
    (tsconfig: string option)
    (args: Builders.ArgumentsBuilder)
    =
    match tsconfig with
    | Some tsconfig ->
      let tsconfig = tsconfig.Replace("\n", "").Replace("\u0022", "\"")

      args.Add $"""--tsconfig-raw={tsconfig} """
    | None -> args

  let addAliases
    (aliases: Map<string<BareImport>, string<ResolutionUrl>>)
    (args: Builders.ArgumentsBuilder)
    =
    for KeyValue(alias, path) in aliases do
      if (UMX.untag path).StartsWith("./") then
        args.Add $"--alias:{alias}={path}" |> ignore

    args

  let addKeepNames(args: Builders.ArgumentsBuilder) = args.Add "--keep-names"

  let singleFileCmd
    (
      source: string,
      loader: LoaderType option,
      config: EsbuildConfig,
      esbuildPath: string<SystemPath>,
      tsconfig: string option,
      logger: ILogger
    ) =
    cancellableTask {
      let execBin = esbuildPath |> UMX.untag
      use writer = new MemoryStream()

      let command =
        Cli
          .Wrap(execBin)
          .WithStandardInputPipe(PipeSource.FromString source)
          .WithStandardOutputPipe(PipeTarget.ToStream writer)
          .WithStandardErrorPipe(PipeTarget.ToDelegate logger.LogError)
          .WithArguments(fun args ->
            args
            |> addTarget config.ecmaVersion
            |> addFormat "esm"
            |> addLoader loader
            |> addMinify config.minify
            |> addJsxAutomatic config.jsxAutomatic
            |> addJsxImportSource config.jsxImportSource
            |> addTsconfigRaw tsconfig
            |> addKeepNames
            |> ignore)
          .WithValidation
          CommandResultValidation.None

      do! (command.ExecuteAsync()).Task :> Task
      return Encoding.UTF8.GetString(writer.ToArray())
    }

  let Create(serviceArgs: EsbuildServiceArgs) =
    { new EsbuildService with
        member this.GetPlugin(config: EsbuildConfig) : PluginInfo =
          let shouldTransform: FilePredicate =
            fun extension ->
              [ ".jsx"; ".tsx"; ".ts"; ".css"; ".js" ]
              |> List.contains extension

          let transform: TransformAsync =
            fun args -> async {

              let loader =
                match args.extension with
                | ".css" -> Some LoaderType.Css
                | ".jsx" -> Some LoaderType.Jsx
                | ".tsx" -> Some LoaderType.Tsx
                | ".ts" -> Some LoaderType.Typescript
                | ".js" -> None
                | _ -> None

              let tsConfig =
                serviceArgs.PerlaFsManager.ResolveTsConfig |> AVal.force

              let! result =
                singleFileCmd(
                  args.content,
                  loader,
                  config,
                  serviceArgs.PerlaFsManager.ResolveEsbuildPath(),
                  tsConfig,
                  serviceArgs.Logger
                )

                |> Async.AwaitCancellableTask

              return {
                content = result
                extension = if args.extension = ".css" then ".css" else ".js"
              }
            }

          plugin Constants.PerlaEsbuildPluginName {
            should_process_file shouldTransform
            with_transform transform
          }

        member this.ProcessCss
          (entrypoint, outdir, config: EsbuildConfig)
          : CancellableTask<unit> =
          cancellableTask {
            let execBin =
              serviceArgs.PerlaFsManager.ResolveEsbuildPath() |> UMX.untag

            let fileLoaders = config.fileLoaders

            let command =
              Cli
                .Wrap(execBin)
                // ensure esbuild is called where the actual sources are
                .WithWorkingDirectory(serviceArgs.Cwd |> UMX.untag)
                .WithStandardOutputPipe(
                  PipeTarget.ToDelegate serviceArgs.Logger.LogInformation
                )
                .WithStandardErrorPipe(
                  PipeTarget.ToDelegate serviceArgs.Logger.LogError
                )
                .WithArguments(fun args ->
                  args.Add(entrypoint)
                  |> addIsBundle true
                  |> addMinify config.minify
                  |> addOutDir outdir
                  |> addDefaultFileLoaders fileLoaders
                  |> ignore)

            do! (command.ExecuteAsync()).Task :> Task
            return ()
          }

        member this.ProcessJS
          (entrypoint, outdir, config: EsbuildConfig)
          : CancellableTask<unit> =
          cancellableTask {
            let execBin =
              serviceArgs.PerlaFsManager.ResolveEsbuildPath() |> UMX.untag

            let fileLoaders = config.fileLoaders

            let command =
              Cli
                .Wrap(execBin)
                // ensure esbuild is called where the actual sources are
                .WithWorkingDirectory(UMX.untag serviceArgs.Cwd)
                .WithStandardOutputPipe(
                  PipeTarget.ToDelegate serviceArgs.Logger.LogInformation
                )
                .WithStandardErrorPipe(
                  PipeTarget.ToDelegate serviceArgs.Logger.LogInformation
                )
                .WithArguments(fun args ->
                  args.Add entrypoint
                  |> addEsExternals config.externals
                  |> addIsBundle true
                  |> addTarget config.ecmaVersion
                  |> addDefaultFileLoaders fileLoaders
                  |> addMinify config.minify
                  |> addFormat "esm"
                  |> addJsxAutomatic config.jsxAutomatic
                  |> addJsxImportSource config.jsxImportSource
                  |> addOutDir outdir
                  |> addAliases config.aliases
                  |> ignore)

            do! (command.ExecuteAsync()).Task :> Task
            return ()
          }

    }
