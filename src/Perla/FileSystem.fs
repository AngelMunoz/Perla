namespace Perla.FileSystem

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

open Perla.Types
open Spectre.Console

open CliWrap
open CliWrap.Buffered

open FsHttp

open IcedTasks
open FSharp.UMX

open FsToolkit.ErrorHandling

open FSharp.Control.Reactive
open FSharp.Data.Adaptive

open Fake.IO.Globbing.Operators

open Perla
open Perla.Units
open Perla.Json
open Perla.Json.TemplateDecoders
open Perla.Logger
open Perla.PackageManager.Types

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
  abstract PerlaConfigPath: string<SystemPath> with get
  abstract CurrentWorkingDirectory: string<SystemPath> with get
  abstract SetCwdToProject: ?fromPath: string<SystemPath> -> unit

[<Interface>]
type PerlaFsManager =

  abstract PerlaConfiguration: Types.PerlaConfig aval

  abstract ResolveIndexPath: string<SystemPath> aval

  abstract ResolveIndex: string aval

  abstract DotEnvContents: Map<string, string> aval

  abstract ResolveImportMap: PkgManager.ImportMap aval

  abstract ResolveDescriptionsFile: unit -> CancellableTask<Map<string, string>>

  abstract ResolvePluginPaths: unit -> (string * string)[]

  abstract ResolveEsbuildPath: unit -> string<SystemPath>

  abstract ResolveLiveReloadScript: unit -> CancellableTask<string>
  abstract ResolveWorkerScript: unit -> CancellableTask<string>
  abstract ResolveTestingHelpersScript: unit -> CancellableTask<string>
  abstract ResolveMochaRunnerScript: unit -> CancellableTask<string>

  abstract SetupEsbuild: string<Semver> -> CancellableTask<unit>

  abstract SetupFable: unit -> CancellableTask<unit>

  abstract SetupTemplate:
    user: string * repository: string<Repository> * branch: string<Branch> ->
      CancellableTask<
        (string<SystemPath> *
        DecodedTemplateConfiguration *
        string *
        string<Repository> *
        string<Branch>) option
       >

[<RequireQualifiedAccess>]
module FileSystem =
  open Microsoft.Extensions.Logging
  open System.Formats.Tar

  [<AutoOpen>]
  module Operators =
    let inline (/) a b = Path.Combine(a, b)

    let inline (|/) (a: string<SystemPath>) (b: string) =
      Path.Combine(UMX.untag a, UMX.untag b) |> UMX.tag<SystemPath>

  let GetDirectories() =
    let rec findConfig filename (directory: DirectoryInfo | null) =
      if isNull directory then
        None
      else
        let found =
          directory.GetFiles(filename, SearchOption.TopDirectoryOnly)
          |> Array.tryHead

        match found with
        | Some found -> Some found
        | None -> findConfig filename directory.Parent

    let findPerlaConfig = findConfig "perla.json"

    { new PerlaDirectories with
        member _.AssemblyRoot = UMX.tag<SystemPath> AppContext.BaseDirectory

        member _.CurrentWorkingDirectory =
          UMX.tag<SystemPath> Environment.CurrentDirectory

        member _.PerlaArtifactsRoot =
          Environment.GetFolderPath
            Environment.SpecialFolder.LocalApplicationData
          / Constants.ArtifactsDirectoryname
          |> UMX.tag<SystemPath>

        member this.Database =
          $"{this.PerlaArtifactsRoot}" / Constants.TemplatesDatabase |> UMX.tag

        member this.Templates =
          $"{this.PerlaArtifactsRoot}" / Constants.TemplatesDirectory |> UMX.tag

        member this.PerlaConfigPath =
          let cwd = DirectoryInfo(UMX.untag this.CurrentWorkingDirectory)

          findPerlaConfig cwd
          |> Option.defaultWith(fun () -> FileInfo(cwd.FullName / "perla.json"))
          |> _.FullName
          |> UMX.tag<SystemPath>

        member this.SetCwdToProject(?fromPath) =
          let path =
            option {
              let! path = fromPath
              let! file = findPerlaConfig(DirectoryInfo(UMX.untag path))
              return file.FullName
            }
            |> function
              | Some path -> path
              | None -> UMX.untag this.PerlaConfigPath

          Directory.SetCurrentDirectory path
    }

  let GetManager
    (logger: ILogger, env: Env.PlatformOps, dirs: PerlaDirectories)
    =
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

          return dotEnvFilesContent
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

        member this.SetupEsbuild(version) = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let esbuildVersion = UMX.untag version

          let binString = $"{env.PlatformString()}-{env.ArchString()}"

          let compressedFile =
            dirs.PerlaArtifactsRoot |/ esbuildVersion / "esbuild.tgz"


          let url =
            $"https://registry.npmjs.org/@esbuild/{binString}/-/{binString}-{esbuildVersion}.tgz"

          let dir =
            DirectoryInfo(UMX.untag compressedFile |> Path.GetDirectoryName)

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
                TemplateConfigurationDecoder
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
                UMX.tag<SystemPath> targetPath,
                config,
                user,
                repository,
                branch)
          | None ->
            logger.LogWarning(
              "No Configuration File found in template {user}/{repository}@{branch}",
              user,
              repository,
              branch
            )

            return None
        }
    }

  let AssemblyRoot: string<SystemPath> =
    UMX.tag<SystemPath> AppContext.BaseDirectory

  let CurrentWorkingDirectory() : string<SystemPath> =
    UMX.tag<SystemPath> Environment.CurrentDirectory

  let PerlaArtifactsRoot: string<SystemPath> =
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    / Constants.ArtifactsDirectoryname
    |> UMX.tag<SystemPath>

  let Database: string<SystemPath> =
    (UMX.untag PerlaArtifactsRoot) / Constants.TemplatesDatabase |> UMX.tag

  let Templates: string<SystemPath> =
    (UMX.untag PerlaArtifactsRoot) / Constants.TemplatesDirectory |> UMX.tag

  let rec findConfigFile(directory: string, fileName: string) =
    let config = directory / fileName

    if File.Exists config then
      Some config
    else
      try
        let parent = Path.GetDirectoryName(directory) |> Path.GetFullPath

        if parent <> directory then
          findConfigFile(parent, fileName)
        else
          None
      with :? ArgumentNullException ->
        None

  let GetConfigPath
    (fileName: string)
    (fromDirectory: string<SystemPath> option)
    : string<SystemPath> =
    let workDir =
      match fromDirectory with
      | Some dir -> UMX.untag dir |> Path.GetFullPath |> UMX.tag
      | None -> CurrentWorkingDirectory() |> UMX.untag

    findConfigFile(workDir, UMX.tag fileName)
    |> Option.defaultValue((CurrentWorkingDirectory() |> UMX.untag) / fileName)
    |> UMX.tag<SystemPath>

  let PerlaConfigPath: string<SystemPath> =
    GetConfigPath Constants.PerlaConfigName None

  let LiveReloadScript =
    lazy File.ReadAllText((UMX.untag AssemblyRoot) / "livereload.js")

  let WorkerScript =
    lazy File.ReadAllText((UMX.untag AssemblyRoot) / "worker.js")

  let TestingHelpersScript =
    lazy File.ReadAllText((UMX.untag AssemblyRoot) / "testing-helpers.js")

  let MochaRunnerScript =
    lazy File.ReadAllText((UMX.untag AssemblyRoot) / "mocha-runner.js")

  let DescriptionsFile =
    lazy
      (File.ReadAllBytes((UMX.untag AssemblyRoot) / "descriptions.json")
       |> Json.FromBytes<Map<string, string>>)

  let ensureFileContent<'T> (path: string<SystemPath>) (content: 'T) =
    try
      File.WriteAllBytes(UMX.untag path, content |> Json.ToBytes)
    with ex ->
      Logger.log(
        $"[bold red]Unable to write file at[/][bold yellow]{path}[/]",
        ex = ex,
        escape = false
      )

      exit 1

    content

  let ExtractTemplateZip
    (username: string, repository: string, branch: string)
    stream
    =
    let targetPath =
      Path.Combine(UMX.untag Templates, $"{username}-{repository}-{branch}")

    try
      Directory.Delete(targetPath, true)
    with ex ->
      ()

    use zip = new ZipArchive(stream)
    zip.ExtractToDirectory(UMX.untag Templates, true)

    Directory.Move(
      Path.Combine(UMX.untag Templates, $"{repository}-{branch}"),
      targetPath
    )

    let config =
      let config =
        try
          File.ReadAllText(targetPath / "perla.config.json") |> Some
        with e ->
          None

      match config with
      | Some config ->
        Thoth.Json.Net.Decode.fromString
          TemplateDecoders.TemplateConfigurationDecoder
          config
      | None -> Error "No Configuration File found"

    UMX.tag<SystemPath> targetPath, config

  let RemoveTemplateDirectory(path: string<SystemPath>) =
    try
      Directory.Delete(UMX.untag path)
    with ex ->
      Logger.log($"There was an error removing: {path}", ex = ex)

  let EsbuildBinaryPath(version: string<Semver> option) : string<SystemPath> =
    let bin = if Env.IsWindows then "" else "bin"
    let exec = if Env.IsWindows then ".exe" else ""

    let version =
      match version with
      | Some version -> UMX.untag version
      | None -> Perla.Constants.Esbuild_Version

    (UMX.untag PerlaArtifactsRoot)
    / version
    / "package"
    / bin
    / $"esbuild{exec}"
    |> Path.GetFullPath
    |> UMX.tag

  let chmodBinCmd(esbuildVersion: string<Semver> option) =
    Cli
      .Wrap("chmod")
      .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
      .WithStandardOutputPipe(PipeTarget.ToStream(Console.OpenStandardOutput()))
      .WithArguments($"+x {EsbuildBinaryPath(esbuildVersion)}")

  let tryDownloadEsBuild
    (esbuildVersion: string, cancellationToken: CancellationToken option)
    : Task<string option> =
    let binString = $"{Env.PlatformString}-{Env.ArchString}"

    let compressedFile =
      (UMX.untag PerlaArtifactsRoot) / esbuildVersion / "esbuild.tgz"

    let url =
      $"https://registry.npmjs.org/@esbuild/{binString}/-/{binString}-{esbuildVersion}.tgz"

    compressedFile
    |> Path.GetDirectoryName
    |> Directory.CreateDirectory
    |> ignore

    task {
      try
        let! req = get url |> Request.sendTAsync
        let token = defaultArg cancellationToken CancellationToken.None
        use! stream = req |> Response.toStreamTAsync token
        Logger.log $"Downloading esbuild from: {url}"
        use file = File.Create(compressedFile)

        do! stream.CopyToAsync(file, cancellationToken = token)

        Logger.log $"Downloaded esbuild to: {file.Name}"

        return Some(file.Name)
      with ex ->
        Logger.log($"Failed to download esbuild from: {url}", ex)
        return None
    }

  let decompressEsbuild
    (esbuildVersion: string<Semver> option)
    (path: Task<string option>)
    =
    task {
      match! path with
      | Some path ->
        let extract() =
          use stream =
            new ICSharpCode.SharpZipLib.GZip.GZipInputStream(File.OpenRead path)

          use archive =
            ICSharpCode.SharpZipLib.Tar.TarArchive.CreateInputTarArchive(
              stream,
              Encoding.UTF8
            )

          path |> Path.GetDirectoryName |> archive.ExtractContents

        extract()

        if Env.IsWindows |> not then
          Logger.log
            $"Executing: chmod +x on \"{EsbuildBinaryPath esbuildVersion}\""

          let res = chmodBinCmd(esbuildVersion).ExecuteAsync()
          do! res.Task :> Task

        Logger.log "Cleaning up!"

        File.Delete(path)

        Logger.log "This setup should happen once per machine"
        Logger.log "If you see it often please report a bug."
      | None -> ()

      return ()
    }

  let TryReadTsConfig() =
    let path = Path.Combine($"{CurrentWorkingDirectory()}", "tsconfig.json")

    try
      File.ReadAllText path |> Some
    with _ ->
      None

  let GetTempDir() =
    let tmp = Path.GetTempPath()
    let path = Path.Combine(tmp, Guid.NewGuid().ToString())
    Directory.CreateDirectory(path) |> ignore
    path |> Path.GetFullPath

  let TplRepositoryChildTemplates
    (path: string<SystemPath>)
    : string<SystemPath> seq =
    try
      UMX.untag path
      |> Directory.EnumerateDirectories
      |> Seq.map(Path.GetDirectoryName >> UMX.tag<SystemPath>)
    with :? DirectoryNotFoundException ->
      Logger.log
        $"Directories not found at {path}, this might be a bad template or a possible bug"

      Seq.empty

  let collectRepositoryFiles(path: string<SystemPath>) =
    let foldFilesAndTemplates (files, templates) (next: FileInfo) =
      if next.FullName.Contains(".tpl.") then
        (files, next :: templates)
      else
        (next :: files, templates)

    let opts = EnumerationOptions()
    opts.RecurseSubdirectories <- true

    try
      DirectoryInfo(UMX.untag path)
        .EnumerateFiles("*.*", SearchOption.AllDirectories)
      |> Seq.filter(fun file -> file.Extension <> ".fsx")
      |> Seq.fold
        foldFilesAndTemplates
        (List.empty<FileInfo>, List.empty<FileInfo>)
    with :? DirectoryNotFoundException ->
      Logger.log(
        "[bold red]While the repository was found, the chosen template was not[/]",
        escape = false
      )

      Logger.log(
        $"Please ensure you chose the correct template and [bold red]{DirectoryInfo(UMX.untag path).Name}[/] exists",
        escape = false
      )

      (List.empty, List.empty)

  let getPerlaFilesMonitor() =
    let fsw =
      new FileSystemWatcher(
        UMX.untag PerlaConfigPath |> Path.GetDirectoryName |> Path.GetFullPath,
        "*",
        IncludeSubdirectories = false,
        NotifyFilter =
          (NotifyFilters.Size
           ||| NotifyFilters.LastWrite
           ||| NotifyFilters.FileName),
        EnableRaisingEvents = true
      )

    fsw.Filters.Add(".html")
    fsw.Filters.Add(".json")
    fsw

  let DotNetToolRestore
    (cancellationToken: CancellationToken)
    : Task<Result<unit, string>> =
    task {
      let ext = if Env.IsWindows then ".exe" else ""
      let dotnet = $"dotnet{ext}"
      let err = StringBuilder()

      let cmd =
        Cli
          .Wrap(dotnet)
          .WithArguments("tool restore")
          .WithStandardErrorPipe(PipeTarget.ToStringBuilder(err))

      try
        let! result = cmd.ExecuteAsync(cancellationToken)

        if result.ExitCode <> 0 then
          return Error(err.ToString())
        else
          return Ok()
      with ex ->
        return Error $"{ex.Message}\n{err.ToString()}"
    }

  let CheckFableExists(cancellationToken: CancellationToken) : Task<bool> = task {
    let ext = if Env.IsWindows then ".exe" else ""
    let dotnet = $"dotnet{ext}"

    let cmd = Cli.Wrap(dotnet).WithArguments("fable --version")

    try
      let! result = cmd.ExecuteAsync(cancellationToken)

      if result.ExitCode <> 0 then return false else return true
    with _ ->
      return false
  }

type FileSystem =

  static member PerlaConfigText
    ([<Optional>] ?fromDirectory: string<SystemPath>)
    =

    let path = FileSystem.GetConfigPath Constants.PerlaConfigName fromDirectory

    try
      File.ReadAllText(UMX.untag path) |> Some
    with :? FileNotFoundException ->
      None

  static member SetCwdToPerlaRoot([<Optional>] ?fromPath) =
    FileSystem.GetConfigPath Constants.PerlaConfigName fromPath
    |> UMX.untag
    |> Path.GetDirectoryName
    |> Path.GetFullPath
    |> Directory.SetCurrentDirectory

  static member GetImportMap([<Optional>] ?fromDirectory: string<SystemPath>) =
    let path = FileSystem.GetConfigPath Constants.ImportMapName fromDirectory

    try
      File.ReadAllBytes(UMX.untag path) |> Json.FromBytes
    with :? FileNotFoundException ->
      { imports = Map.empty; scopes = None }
      |> FileSystem.ensureFileContent path

  static member IndexFile(fromConfig: string<SystemPath>) =
    File.ReadAllText(UMX.untag fromConfig)

  static member PluginFiles() =
    let path =
      Path.Combine(
        UMX.untag(FileSystem.CurrentWorkingDirectory()),
        ".perla",
        "plugins"
      )

    !! $"{path}/**/*.fsx"
    |> Seq.toArray
    |> Array.Parallel.map(fun path -> path, File.ReadAllText(path))


  static member WriteImportMap(map: ImportMap, ?fromDirectory) =
    let path = FileSystem.GetConfigPath Constants.ImportMapName fromDirectory
    FileSystem.ensureFileContent path map

  static member WritePerlaConfig(?config: JsonObject, ?fromDirectory) =
    let path = FileSystem.GetConfigPath Constants.PerlaConfigName fromDirectory

    match config with
    | Some config ->
      try
        File.WriteAllText(
          UMX.untag path,
          config.ToJsonString(DefaultJsonOptions())
        )
      with ex ->
        Logger.log(
          $"[bold red]Unable to write file at[/][bold yellow]{path}[/]",
          ex = ex,
          escape = false
        )

        exit 1
    | None -> ()


  static member ObservePerlaFiles
    (indexPath: string, [<Optional>] ?cancellationToken: CancellationToken)
    =
    let notifier = FileSystem.getPerlaFilesMonitor()

    match cancellationToken with
    | Some cancel ->
      cancel.UnsafeRegister((fun _ -> notifier.Dispose()), ()) |> ignore
    | None -> ()

    [ notifier.Changed :> IObservable<FileSystemEventArgs>; notifier.Created ]
    |> Observable.mergeSeq
    |> Observable.throttle(TimeSpan.FromMilliseconds(400))
    |> Observable.filter(fun event ->
      indexPath.ToLowerInvariant().Contains(event.Name)
      || event.Name.Contains(Constants.PerlaConfigName)
      || event.Name.Contains(Constants.ImportMapName))
    |> Observable.map(fun event ->
      match event.Name.ToLowerInvariant() with
      | value when value.Contains(Constants.PerlaConfigName) ->
        PerlaFileChange.PerlaConfig
      | value when value.Contains(Constants.ImportMapName) ->
        PerlaFileChange.ImportMap
      | _ -> PerlaFileChange.Index)

  static member SetupEsbuild
    (
      esbuildVersion: string<Semver>,
      [<Optional>] ?cancellationToken: CancellationToken
    ) =
    Logger.log "Checking whether esbuild is present..."

    if
      File.Exists(
        FileSystem.EsbuildBinaryPath(Some esbuildVersion) |> UMX.untag
      )
    then
      Logger.log "esbuild is present."
      Task.FromResult(())
    else
      Logger.log "esbuild is not present, setting esbuild..."

      Logger.spinner(
        "esbuild is not present, setting esbuild...",
        fun context ->
          context.Status <- "Downloading esbuild..."

          FileSystem.tryDownloadEsBuild(
            UMX.untag esbuildVersion,
            cancellationToken
          )
          |> (fun path ->
            context.Status <- "Extracting esbuild..."
            path)
          |> FileSystem.decompressEsbuild(Some esbuildVersion)
          |> (fun path ->
            context.Status <- "Cleaning up extra files..."
            path)
      )


  static member WriteTplRepositoryToDisk
    (origin: string<SystemPath>, target: string<UserPath>, ?payload: obj)
    =
    let originDirectory = UMX.cast origin |> Path.GetFullPath
    let targetDirectory = UMX.cast target |> Path.GetFullPath

    let files, templates = FileSystem.collectRepositoryFiles origin


    let copyFiles(ctx: ProgressTask) =
      files
      |> Array.ofList
      |> Array.Parallel.iter(fun file ->
        file.Directory.Create()
        let target = file.FullName.Replace(originDirectory, targetDirectory)
        Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
        file.CopyTo(target, true) |> ignore
        ctx.Increment(1.))

    let copyTemplates(ctx: ProgressTask) =
      let compileFiles (payload: obj option) (file: string) =
        let tpl = Scriban.Template.Parse(file)
        tpl.Render(payload |> Option.toObj)

      templates
      |> Array.ofList
      |> Array.Parallel.iter(fun file ->
        file.Directory.Create()

        let target =
          file.FullName
            .Replace(originDirectory, targetDirectory)
            .Replace(".tpl", "")

        let content = compileFiles payload (File.ReadAllText file.FullName)

        File.WriteAllText(target, content)
        ctx.Increment(1.))

    DirectoryInfo(UMX.untag target).Create()
    let progress = AnsiConsole.Progress()

    progress.Start(fun ctx ->
      let copyTask =
        ctx.AddTask(
          "Copy Files",
          ProgressTaskSettings(AutoStart = true, MaxValue = files.Length)
        )


      copyFiles copyTask
      copyTask.StopTask()

      if templates.Length > 0 then
        let processTemplates =
          ctx.AddTask(
            "Process Templates",
            ProgressTaskSettings(AutoStart = true, MaxValue = templates.Length)
          )

        copyTemplates processTemplates
        processTemplates.StopTask())

  static member GetDotEnvFilePaths(?fromDirectory) =
    let path =
      FileSystem.GetConfigPath Constants.PerlaConfigName fromDirectory
      |> UMX.untag
      |> Path.GetDirectoryName

    !! $"{path}/*.env" ++ $"{path}/*.*.env" |> Seq.map UMX.tag<SystemPath>
