namespace Perla.FileSystem

open System
open System.Formats.Tar
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json.Nodes
open Microsoft.Extensions.Logging
open Perla.Types

open CliWrap
open CliWrap.Buffered

open FsHttp

open IcedTasks
open FSharp.UMX

open FsToolkit.ErrorHandling

open FSharp.Data.Adaptive

open Fake.IO.Globbing.Operators
open Fake.IO.Globbing

open Spectre.Console

open Perla
open Perla.Units
open Perla.Json
open Perla.Json.TemplateDecoders

[<RequireQualifiedAccess>]
type PerlaFileChange =
  | Index
  | PerlaConfig
  | ImportMap

[<Measure>]
type Repository

[<Measure>]
type Branch

[<Interface>]
type PerlaDirectories =
  abstract AssemblyRoot: string<SystemPath> with get
  abstract PerlaArtifactsRoot: string<SystemPath> with get
  abstract Database: string<SystemPath> with get
  abstract Templates: string<SystemPath> with get
  abstract OfflineTemplates: string<SystemPath> with get
  abstract PerlaConfigPath: string<SystemPath> with get
  abstract OriginalCwd: string<SystemPath> with get
  abstract CurrentWorkingDirectory: string<SystemPath> with get
  abstract SetCwdToProject: ?fromPath: string<SystemPath> -> unit

[<Interface>]
type PerlaFsManager =


  abstract PerlaConfiguration: Types.PerlaConfig aval

  abstract ResolveIndexPath: string<SystemPath> aval

  abstract ResolveIndex: string aval

  abstract DotEnvContents: Map<string, string> aval

  abstract ResolveImportMap: PkgManager.ImportMap aval

  abstract ResolveTsConfig: string option aval

  abstract ResolveOfflineTemplatesConfig:
    unit -> CancellableTask<DecodedTemplateConfiguration>

  abstract ResolveDescriptionsFile: unit -> CancellableTask<Map<string, string>>

  abstract ResolvePluginPaths: unit -> (string * string)[]

  abstract ResolveEsbuildPath: unit -> string<SystemPath>

  abstract ResolveLiveReloadScript: unit -> CancellableTask<string>
  abstract ResolveWorkerScript: unit -> CancellableTask<string>
  abstract ResolveTestingHelpersScript: unit -> CancellableTask<string>
  abstract ResolveMochaRunnerScript: unit -> CancellableTask<string>

  abstract SaveImportMap: map: PkgManager.ImportMap -> CancellableTask<unit>

  abstract SavePerlaConfig: config: PerlaConfig -> CancellableTask<unit>

  abstract SavePerlaConfig:
    updates: PerlaConfig.PerlaWritableField seq -> CancellableTask<unit>


  abstract SetupEsbuild: string<Semver> -> CancellableTask<unit>

  abstract SetupFable: unit -> CancellableTask<unit>

  abstract SetupTemplate:
    user: string * repository: string<Repository> * branch: string<Branch> ->
      CancellableTask<
        (string<SystemPath> * TemplateDecoders.DecodedTemplateConfiguration) option
       >

  abstract CopyGlobs:
    buildConfig: BuildConfig * tempDir: string<SystemPath> -> unit

  abstract EmitEnvFile:
    config: PerlaConfig * ?tmpPath: string<SystemPath> -> unit

[<AutoOpen>]
module Operators =
  let inline (/) a b = Path.Combine(a, b)

  let inline (|/) (a: string<SystemPath>) (b: string) =
    Path.Combine(UMX.untag a, UMX.untag b) |> UMX.tag<SystemPath>

[<RequireQualifiedAccess>]
module FileSystem =

  [<TailCall>]
  let rec findConfig filename (directory: DirectoryInfo | null) =
    match directory with
    | null -> None
    | directory ->
      let found =
        directory.GetFiles(filename, SearchOption.TopDirectoryOnly)
        |> Array.tryHead

      match found with
      | Some found -> Some found
      | None -> findConfig filename directory.Parent

  let GetDirectories() =

    let findPerlaConfig = findConfig "perla.json"

    let originalcwd = Directory.GetCurrentDirectory() |> UMX.tag<SystemPath>

    { new PerlaDirectories with
        member _.AssemblyRoot = UMX.tag<SystemPath> AppContext.BaseDirectory

        member _.CurrentWorkingDirectory =
          UMX.tag<SystemPath>(Directory.GetCurrentDirectory())

        member _.PerlaArtifactsRoot =
          Environment.GetFolderPath
            Environment.SpecialFolder.LocalApplicationData
          / Constants.ArtifactsDirectoryname
          |> UMX.tag<SystemPath>

        member this.Database =
          this.PerlaArtifactsRoot |/ Constants.TemplatesDatabase

        member this.Templates =
          this.PerlaArtifactsRoot |/ Constants.TemplatesDirectory

        member this.OfflineTemplates =
          this.PerlaArtifactsRoot |/ Constants.OfflineTemplatesDirectory

        member this.PerlaConfigPath =
          let cwd = DirectoryInfo(UMX.untag this.CurrentWorkingDirectory)

          findPerlaConfig cwd
          |> Option.defaultWith(fun () -> FileInfo(cwd.FullName / "perla.json"))
          |> _.FullName
          |> UMX.tag<SystemPath>

        member _.OriginalCwd = originalcwd

        member this.SetCwdToProject(?fromPath) =
          let path =
            option {
              let! path = fromPath
              let! file = findPerlaConfig(DirectoryInfo(UMX.untag path))
              return file.FullName
            }
            |> function
              | Some path -> path
              | None -> UMX.untag this.OriginalCwd

          Directory.SetCurrentDirectory path
    }

  let GetManager(logger: ILogger, env: PlatformOps, dirs: PerlaDirectories) =
    { new PerlaFsManager with

        member _.PerlaConfiguration = adaptive {
          let path = UMX.untag dirs.PerlaConfigPath
          let! content = AdaptiveFile.TryReadAllText path

          match content with
          | None -> return Defaults.PerlaConfig
          | Some content -> return PerlaConfig.FromString content
        }

        member this.ResolveIndexPath =
          this.PerlaConfiguration |> AVal.map _.index

        member this.ResolveIndex = adaptive {
          let! indexPath = this.ResolveIndexPath
          let! content = AdaptiveFile.TryReadAllText(UMX.untag indexPath)
          return defaultArg content ""
        }

        member _.DotEnvContents = adaptive {

          let envVarRegex =
            RegularExpressions.Regex
              "PERLA_(?<envvarname>[a-zA-Z0-9_]+)\\s*=\\s*(?<content>.+)"

          let path = UMX.untag dirs.CurrentWorkingDirectory
          let dotEnvFiles = AdaptiveDirectory.GetFiles(path, "*.env")

          let parseEnvLine line =
            let matchResult = envVarRegex.Match line

            if matchResult.Success then
              Some(
                matchResult.Groups.["envvarname"].Value,
                matchResult.Groups.["content"].Value
              )
            else
              None

          let reduction =
            AdaptiveReduction.fold Map.empty<string, string>
            <| fun acc next ->
              Map.fold (fun acc k v -> Map.add k v acc) acc next

          let! dotEnvFilesContent =
            dotEnvFiles
            |> ASet.mapA(fun file -> adaptive {
              let! fileContent = AdaptiveFile.TryReadAllLines file.FullName
              let fileContent = fileContent |> Option.defaultValue Array.empty

              return fileContent |> Array.choose parseEnvLine |> Map.ofArray
            })
            |> ASet.reduce reduction

          let! envVars =
            Environment.GetEnvironmentVariables()
            |> Seq.cast<Collections.DictionaryEntry>
            |> Seq.filter(fun entry ->
              (nonNull(entry.Key.ToString())).StartsWith("PERLA_"))
            |> Seq.map(fun entry ->
              (nonNull(entry.Key.ToString())).Replace("PERLA_", ""),
              (nonNull entry.Value).ToString() |> nonNull)
            |> Map.ofSeq
            |> AVal.constant

          let allVars =
            dotEnvFilesContent
            |> Map.fold (fun acc k v -> Map.add k v acc) envVars

          return allVars
        }

        member _.ResolveImportMap = adaptive {
          let path = dirs.CurrentWorkingDirectory |/ Constants.ImportMapName

          let! content = AdaptiveFile.TryReadAllText(UMX.untag path)

          let importMap =
            content
            |> Option.map(
              Thoth.Json.Net.Decode.fromString PkgManager.ImportMap.Decoder
              >> Result.toOption
            )
            |> Option.flatten

          return defaultArg importMap PkgManager.ImportMap.Empty
        }

        member _.ResolveTsConfig = adaptive {
          let path = dirs.CurrentWorkingDirectory |/ "tsconfig.json"
          let! content = AdaptiveFile.TryReadAllText(UMX.untag path)
          return content
        }

        member _.ResolveDescriptionsFile() = cancellableTask {
          let path =
            UMX.untag dirs.AssemblyRoot / "descriptions.json"
            |> UMX.tag<SystemPath>

          try
            let! token = CancellableTask.getCancellationToken()
            let! content = File.ReadAllBytesAsync(UMX.untag path, token)
            let descriptions = Json.FromBytes<Map<string, string>> content
            return descriptions
          with _ ->
            return Map.empty<string, string>
        }

        member _.ResolveOfflineTemplatesConfig() = cancellableTask {
          let! token = CancellableTask.getCancellationToken()
          let path = UMX.untag dirs.OfflineTemplates / "perla.config.json"
          let! content = File.ReadAllTextAsync(path, token)

          let decoded =
            Thoth.Json.Net.Decode.fromString
              TemplateDecoders.TemplateConfigurationDecoder
              content

          match decoded with
          | Ok config -> return config
          | Error error ->
            logger.LogWarning(
              "Failed to decode offline templates configuration: {error}",
              error
            )
            // This should not happen at all.
            return
              failwith
                $"Failed to decode offline templates configuration: {error}"
        }


        member _.ResolvePluginPaths() =
          let path = dirs.CurrentWorkingDirectory |/ ".perla" / "plugins"

          !! $"{path}/**/*.fsx"
          |> Seq.toArray
          |> Array.Parallel.map(fun path -> path, File.ReadAllText path)

        member this.ResolveEsbuildPath() =
          let bin = if env.IsWindows() then "" else "bin"
          let exec = if env.IsWindows() then ".exe" else ""

          let esbuildVersion =
            this.PerlaConfiguration |> AVal.map _.esbuild.version |> AVal.force

          dirs.PerlaArtifactsRoot
          |/ UMX.untag esbuildVersion
          |/ "package"
          |/ bin
          |/ $"esbuild{exec}"
          |> UMX.untag
          |> Path.GetFullPath
          |> UMX.tag<SystemPath>

        member _.ResolveLiveReloadScript() = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let! content =
            File.ReadAllTextAsync(
              UMX.untag dirs.AssemblyRoot / "livereload.js",
              token
            )

          return content
        }

        member _.ResolveMochaRunnerScript() = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let! content =
            File.ReadAllTextAsync(
              UMX.untag dirs.AssemblyRoot / "mocha-runner.js",
              token
            )

          return content
        }

        member _.ResolveTestingHelpersScript() = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let! content =
            File.ReadAllTextAsync(
              UMX.untag dirs.AssemblyRoot / "testing-helpers.js",
              token
            )

          return content
        }

        member _.ResolveWorkerScript() = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let! content =
            File.ReadAllTextAsync(
              UMX.untag dirs.AssemblyRoot / "worker.js",
              token
            )

          return content
        }

        member _.SaveImportMap(map) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()
          let path = dirs.CurrentWorkingDirectory |/ Constants.ImportMapName
          let content = Json.ToText(map)
          do! File.WriteAllTextAsync(UMX.untag path, content, token)
        }

        member _.SavePerlaConfig(config: PerlaConfig) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()
          let path = dirs.CurrentWorkingDirectory |/ Constants.PerlaConfigName
          let content = Json.ToText(config)
          do! File.WriteAllTextAsync(UMX.untag path, content, token)
        }

        member _.SavePerlaConfig(updates: PerlaConfig.PerlaWritableField seq) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()
          let path = dirs.CurrentWorkingDirectory |/ Constants.PerlaConfigName

          let! mutableConfig = taskOption {
            try
              let! content = File.ReadAllTextAsync(UMX.untag path, token)

              if String.IsNullOrWhiteSpace content then
                return! None
              else
                return
                  (JsonObject.Parse(
                    content,
                    nodeOptions = DefaultJsonNodeOptions(),
                    documentOptions = DefaultJsonDocumentOptions()
                   )
                   |> nonNull)
                    .AsObject()
            with :? FileNotFoundException ->
              return! None
          }

          let updatedContent =
            PerlaConfig.UpdateFileFields mutableConfig updates
            |> _.ToJsonString(Json.DefaultJsonOptions())

          do! File.WriteAllTextAsync(UMX.untag path, updatedContent, token)
        }

        member this.SetupEsbuild(version) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let esbuildVersion = UMX.untag version

          let binString = $"{env.PlatformString()}-{env.ArchString()}"

          let compressedFile =
            dirs.PerlaArtifactsRoot |/ esbuildVersion / "esbuild.tgz"


          let url =
            $"https://registry.npmjs.org/@esbuild/{binString}/-/{binString}-{esbuildVersion}.tgz"

          let dir =
            DirectoryInfo(
              UMX.untag compressedFile |> Path.GetDirectoryName |> nonNull
            )

          dir.Create()

          let extractDir = UMX.untag dirs.PerlaArtifactsRoot / UMX.untag version

          try
            let! req =
              get url |> Config.cancellationToken token |> Request.sendTAsync

            use! stream = req |> Response.toStreamTAsync token
            use source = new GZipStream(stream, CompressionMode.Decompress)

            do!
              TarFile.ExtractToDirectoryAsync(
                source,
                UMX.untag extractDir,
                true,
                token
              )
          // extracts $"./{extractDir}/package/bin/esbuild"
          // extracts $"./{extractDir}/package/esbuild.exe" in windows
          with ex ->
            logger.LogWarning("Failed to extract esbuild from {url}", url, ex)
            return ()

          if not(env.IsWindows()) then
            let esbuildBinaryPath = this.ResolveEsbuildPath() |> UMX.untag

            logger.LogInformation(
              "Executing: chmod +x on \"{esbuildBinaryPath}\"",
              esbuildBinaryPath
            )

            let command =
              Cli
                .Wrap("chmod")
                .WithStandardOutputPipe(PipeTarget.ToDelegate logger.LogDebug)
                .WithStandardErrorPipe(PipeTarget.ToDelegate logger.LogDebug)
                .WithArguments
                $"+x {esbuildBinaryPath}"

            let! _ = command.ExecuteAsync token

            logger.LogInformation(
              "Successfully set executable permissions for {esbuildBinaryPath}",
              esbuildBinaryPath
            )

            logger.LogInformation
              "This setup should happen once per machine. If you see it often please report a bug."

            return ()
          else

            logger.LogInformation
              "This setup should happen once per machine. If you see it often please report a bug."

            return ()
        }

        member _.SetupFable() = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let ext = if env.IsWindows() then ".exe" else ""
          let dotnet = $"dotnet{ext}"

          let dotnetCmd = Cli.Wrap(dotnet)


          let! fableExists =
            dotnetCmd
              .WithArguments([ "fable"; "--version" ])
              .WithValidation(CommandResultValidation.None)
              .ExecuteBufferedAsync(token)

          if fableExists.ExitCode = 0 then
            logger.LogInformation "Fable is already installed, skipping setup."
            return ()

          logger.LogInformation "Fable is not installed, installing..."

          let! installCmd =
            dotnetCmd
              .WithArguments(
                [ "tool"; "install"; "fable"; "--create-manifest-if-needed" ]
              )
              .WithValidation(CommandResultValidation.None)
              .ExecuteBufferedAsync
              token

          if installCmd.ExitCode <> 0 then
            logger.LogError(
              "Failed to install Fable: {Error}",
              installCmd.StandardError
            )

            return ()

          logger.LogInformation "Fable installed successfully."
          return ()
        }

        member _.SetupTemplate(user, repository, branch) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let! url =
            get
              $"https://github.com/{user}/{repository}/archive/refs/heads/{branch}.zip"
            |> Config.cancellationToken token
            |> Request.sendTAsync

          use! stream = url |> Response.toStreamTAsync token

          let targetPath =
            Path.Combine(
              UMX.untag dirs.Templates,
              $"{user}-{repository}-{branch}"
            )

          try
            Directory.Delete(targetPath, true)
          with ex ->
            ()

          use zip = new ZipArchive(stream)
          zip.ExtractToDirectory(UMX.untag dirs.Templates, true)

          Directory.Move(
            Path.Combine(UMX.untag dirs.Templates, $"{repository}-{branch}"),
            targetPath
          )

          let! config = cancellableTask {
            try
              let! content =
                File.ReadAllTextAsync(targetPath / "perla.config.json")

              return Some content
            with _ ->
              return None
          }

          match config with
          | Some config ->
            let decoded =
              Thoth.Json.Net.Decode.fromString
                TemplateDecoders.TemplateConfigurationDecoder
                config
              |> Result.teeError(fun error ->
                logger.LogWarning(
                  "Failed to decode template configuration: {error}",
                  error
                ))
              |> Result.toOption

            return
              decoded
              |> Option.map(fun config ->
                UMX.tag<SystemPath> targetPath, config)
          | None ->
            logger.LogWarning(
              "No Configuration File found in template {user}/{repository}@{branch}",
              user,
              repository,
              branch
            )

            return None
        }

        member _.CopyGlobs
          (buildConfig: BuildConfig, tempDir: string<SystemPath>)
          =
          let outDir = UMX.untag buildConfig.outDir |> Path.GetFullPath

          let chooseGlobs
            (startsWith: string)
            (contains: string)
            (glob: string)
            =
            if glob.StartsWith startsWith then
              Some(glob.Substring startsWith.Length)
            elif not(glob.Contains contains) then
              Some(glob)
            else
              None

          let lfsGlob =
            let localIncludes =
              buildConfig.includes
              |> Seq.choose(chooseGlobs "lfs:" "vfs:")
              |> Seq.toList

            let localExcludes =
              buildConfig.excludes
              |> Seq.choose(chooseGlobs "lfs:" "vfs:")
              |> Seq.toList

            {
              BaseDirectory = dirs.CurrentWorkingDirectory |> UMX.untag
              Includes = localIncludes
              Excludes = localExcludes
            }

          let vfsGlob =
            let virtualIncludes =
              buildConfig.includes
              |> Seq.choose(chooseGlobs "vfs:" "lfs:")
              |> Seq.toList

            let virtualExcludes =
              buildConfig.excludes
              |> Seq.choose(chooseGlobs "vfs:" "lfs:")
              |> Seq.toList

            {
              BaseDirectory = UMX.untag tempDir
              Includes = virtualIncludes
              Excludes = virtualExcludes
            }

          let copyAndIncrement
            (cwd: string)
            (tsk: ProgressTask)
            (file: string)
            =
            tsk.Increment 1
            let targetPath = file.Replace(cwd, outDir)

            try
              Path.GetDirectoryName targetPath
              |> nonNull
              |> Directory.CreateDirectory
              |> ignore
            with _ ->
              ()

            File.Copy(file, targetPath, true)

          AnsiConsole
            .Progress()
            .Start(fun ctx ->
              let lfsTask =
                ctx.AddTask(
                  "Copy Local Files to Output",
                  true,
                  lfsGlob |> Seq.length |> float
                )

              let vfsTask =
                ctx.AddTask(
                  "Copy virtual files to Output",
                  true,
                  vfsGlob |> Seq.length |> float
                )

              let copyLocal =
                copyAndIncrement
                  (UMX.untag dirs.CurrentWorkingDirectory)
                  lfsTask

              let copyVirtual = copyAndIncrement (UMX.untag tempDir) vfsTask

              vfsGlob |> Seq.toArray |> Array.Parallel.iter copyVirtual

              lfsGlob |> Seq.toArray |> Array.Parallel.iter copyLocal)

        member this.EmitEnvFile
          (config: PerlaConfig, ?tmpPath: string<SystemPath>)
          =
          let tmpPath = defaultArg tmpPath config.build.outDir |> UMX.untag
          let content = this.DotEnvContents |> AVal.force

          if Map.isEmpty content then
            logger.LogInformation("No environment variables to emit, skipping.")
          else
            logger.LogInformation(
              "Emitting environment variables to {Path}",
              UMX.untag config.envPath
            )

          // ensure the directory exists
          Directory.CreateDirectory tmpPath |> ignore

          let content =
            content
            |> Map.fold
              (fun (sb: StringBuilder) key value ->
                sb.AppendLine $"export const {key} = \"{value}\"")
              (StringBuilder())
            |> _.ToString()

          // remove the leading slash
          let targetFile = (UMX.untag config.envPath)[1..]

          let path = Path.Combine(tmpPath, targetFile) |> Path.GetFullPath
          File.WriteAllText(path, content)
    }
