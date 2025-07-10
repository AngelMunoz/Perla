namespace Perla.VirtualFs

open System
open System.Collections.Concurrent
open System.IO

open Perla.Units
open Perla.Logger
open Perla.Plugins
open Perla.Extensibility

open FSharp.UMX
open FSharp.Control
open FSharp.Control.Reactive

open Fake.IO.Globbing.Operators
open Microsoft.Extensions.Logging
open AngleSharp.Io


// New types for refactored VFS
type MountedDirectories = Map<string<ServerUrl>, string<UserPath>>
type ApplyPluginsFn = FileTransform -> Async<FileTransform>

// Existing types
[<Struct>]
type ChangeKind =
  | Created
  | Deleted
  | Renamed
  | Changed

type FileChangedEvent = {
  serverPath: string<ServerUrl>
  userPath: string<UserPath>
  oldPath: string<SystemPath> option
  oldName: string<SystemPath> option
  changeType: ChangeKind
  path: string<SystemPath>
  name: string<SystemPath>
}

type FileContent = {
  filename: string
  mimetype: string
  content: string
  source: string<SystemPath>
}

type BinaryFileInfo = {
  filename: string
  mimetype: string
  source: string<SystemPath>
}

type FileKind =
  | TextFile of FileContent
  | BinaryFile of BinaryFileInfo

type VirtualFileEntry = {
  kind: FileKind
  lastModified: DateTime
}

type VirtualFileSystem =
  inherit IDisposable
  abstract member Resolve: string<ServerUrl> -> FileKind option
  abstract member Load: MountedDirectories -> Async<unit>

  abstract member ToDisk:
    ?location: string<SystemPath> -> Async<string<SystemPath>>

  abstract member FileChanges: IObservable<FileChangedEvent>

type VirtualFileSystemArgs = {
  Extensibility: ExtensibilityService
  Logger: ILogger
}

module VirtualFs =

  type FileChangeEventData = {
    serverPath: string<ServerUrl>
    userPath: string<UserPath>
    changeType: ChangeKind
    path: string<SystemPath>
    name: string<SystemPath>
  }

  let getMimeType(filename: string) =
    filename
    |> Path.GetExtension
    |> defaultIfNull ""
    |> MimeTypeNames.FromExtension

  let shouldIgnoreFile(path: string) =
    let normalized = path.Replace("\\", "/")

    normalized.Contains("/bin/")
    || normalized.Contains("/obj/")
    || normalized.EndsWith(".fsproj")
    || normalized.EndsWith(".fs")
    || normalized.EndsWith(".fsx")

  let collectSourceFiles (logger: ILogger) (mountedDirs: MountedDirectories) = [
    for KeyValue(serverPath, userPath) in mountedDirs do
      let fullPath = Path.GetFullPath(UMX.untag userPath)

      if Directory.Exists fullPath then
        logger.LogDebug(
          "Collecting source files from {Directory} -> {ServerPath}",
          fullPath,
          UMX.untag serverPath
        )

        let files =
          !! $"{fullPath}/**/*"
          -- $"{fullPath}/**/bin/**"
          -- $"{fullPath}/**/obj/**"
          -- $"{fullPath}/**/*.fs"
          -- $"{fullPath}/**/*.fsproj"
          -- $"{fullPath}/**/*.fsx"

        let fileList = files |> Seq.toList

        logger.LogDebug(
          "Found {FileCount} files in {Directory}",
          fileList.Length,
          fullPath
        )

        for file in fileList do
          UMX.tag<SystemPath> file, serverPath, UMX.tag<UserPath> fullPath
      else
        logger.LogWarning(
          "Directory {Directory} does not exist, skipping",
          fullPath
        )
  ]

  let transformSourcePath
    (systemPath: string<SystemPath>)
    (userPath: string<UserPath>)
    (serverPath: string<ServerUrl>)
    =
    let sourcePath = UMX.untag systemPath
    let basePath = UMX.untag userPath
    let targetBase = UMX.untag serverPath

    let relativePath = Path.GetRelativePath(basePath, sourcePath)

    let serverFilePath =
      if targetBase = "/" then
        "/" + relativePath.Replace("\\", "/")
      else
        targetBase.TrimEnd('/') + "/" + relativePath.Replace("\\", "/")

    UMX.tag<ServerUrl> serverFilePath

  let applyPlugins
    (logger: ILogger)
    (extensibility: ExtensibilityService)
    (content: string)
    (extension: string)
    =
    async {
      // Create FileTransform for plugin processing
      let fileTransform = {
        content = content
        extension = extension
      }

      // Check if there are plugins available for this extension
      if extensibility.HasPluginsForExtension(extension) then
        logger.LogDebug("Applying plugins for extension {Extension}", extension)

        // Get all available plugins and run them
        let allPlugins = extensibility.GetAllPlugins()
        let pluginOrder = allPlugins |> List.map(fun p -> p.name)

        logger.LogTrace(
          "Running {PluginCount} plugins for {Extension}: {PluginNames}",
          pluginOrder.Length,
          extension,
          pluginOrder
        )

        let! result = extensibility.RunPlugins pluginOrder fileTransform

        if result.extension <> extension then
          logger.LogDebug(
            "Plugin transformed file extension from {OldExt} to {NewExt}",
            extension,
            result.extension
          )

        return result
      else
        // No plugins available for this extension, return unchanged
        logger.LogTrace(
          "No plugins available for extension {Extension}",
          extension
        )

        return fileTransform
    }

  let processFile
    (logger: ILogger)
    (extensibility: ExtensibilityService)
    (files: ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>)
    (systemPath: string<SystemPath>)
    (userPath: string<UserPath>)
    (serverPath: string<ServerUrl>)
    =
    async {
      try
        let sourcePath = UMX.untag systemPath
        let extension = Path.GetExtension(sourcePath) |> defaultIfNull ""
        let filename = Path.GetFileName(sourcePath) |> nonNull
        let mimeType = getMimeType filename
        let targetPath = transformSourcePath systemPath userPath serverPath

        logger.LogDebug(
          "Processing file {FilePath} -> {TargetPath}",
          sourcePath,
          UMX.untag targetPath
        )

        if mimeType = MimeTypeNames.Binary then
          let binaryInfo = {
            filename = filename
            mimetype = mimeType
            source = systemPath
          }

          let entry = {
            kind = BinaryFile binaryInfo
            lastModified = File.GetLastWriteTime(sourcePath)
          }

          files.[targetPath] <- entry
          logger.LogTrace("Processed binary file {FilePath}", sourcePath)
        else
          let content = File.ReadAllText(sourcePath)
          let! transform = applyPlugins logger extensibility content extension

          let fileContent = {
            filename = Path.GetFileName(sourcePath) |> nonNull
            mimetype = getMimeType(filename + transform.extension)
            content = transform.content
            source = systemPath
          }

          let finalPath =
            if transform.extension <> extension then
              let newName =
                $"{Path.GetFileNameWithoutExtension(sourcePath)}{transform.extension}"

              let dir = Path.GetDirectoryName(UMX.untag targetPath) |> nonNull

              let newPath =
                UMX.tag<ServerUrl>(
                  Path.Combine(dir, newName).Replace("\\", "/")
                )

              logger.LogDebug(
                "File extension transformed from {OldExt} to {NewExt}, path changed to {NewPath}",
                extension,
                transform.extension,
                UMX.untag newPath
              )

              newPath
            else
              targetPath

          let entry = {
            kind = TextFile fileContent
            lastModified = File.GetLastWriteTime(sourcePath)
          }

          files.[finalPath] <- entry
          logger.LogTrace("Processed text file {FilePath}", sourcePath)
      with ex ->
        logger.LogError(
          ex,
          "Error processing file {FilePath}",
          UMX.untag systemPath
        )
    }

  let loadAllFiles
    (logger: ILogger)
    (extensibility: ExtensibilityService)
    (files: ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>)
    (mountedDirs: MountedDirectories)
    =
    async {
      let sourceFiles = collectSourceFiles logger mountedDirs

      logger.LogInformation(
        "Loading {FileCount} files from {DirCount} mounted directories",
        sourceFiles.Length,
        mountedDirs.Count
      )

      do!
        sourceFiles
        |> List.map(fun (systemPath, serverPath, userPath) ->
          processFile logger extensibility files systemPath userPath serverPath)
        |> Async.Parallel
        |> Async.Ignore

      logger.LogInformation(
        "Successfully loaded all files into virtual file system"
      )
    }

  let processFileChangeEvent
    (logger: ILogger)
    (extensibility: ExtensibilityService)
    (files: ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>)
    (event: FileChangedEvent)
    =
    async {
      try
        let targetPath =
          transformSourcePath event.path event.userPath event.serverPath

        logger.LogInformation(
          "Processing file change event {ChangeType} for {FilePath}",
          event.changeType,
          UMX.untag event.path
        )

        match event.changeType with
        | Deleted ->
          let removed = files.TryRemove targetPath

          if fst removed then
            logger.LogDebug(
              "Removed file {FilePath} from virtual file system",
              UMX.untag targetPath
            )
          else
            logger.LogWarning(
              "Attempted to remove non-existent file {FilePath}",
              UMX.untag targetPath
            )
        | Created
        | Changed
        | Renamed ->
          do!
            processFile
              logger
              extensibility
              files
              event.path
              event.userPath
              event.serverPath
      with ex ->
        logger.LogError(
          ex,
          "Error processing file change event for {FilePath}",
          UMX.untag event.path
        )
    }

  let createFileChangeStream
    (logger: ILogger)
    (extensibility: ExtensibilityService)
    (files: ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>)
    (watchers: ResizeArray<FileSystemWatcher>)
    (mountedDirs: MountedDirectories)
    : IObservable<FileChangedEvent> =

    let fileChangeObservables = ResizeArray<IObservable<FileChangedEvent>>()

    logger.LogDebug(
      "Creating file change stream for {DirCount} mounted directories",
      mountedDirs.Count
    )

    for KeyValue(serverPath, userPath) in mountedDirs do
      let fullPath = Path.GetFullPath(UMX.untag userPath)

      if Directory.Exists fullPath then
        logger.LogDebug(
          "Setting up file watcher for {Directory} -> {ServerPath}",
          fullPath,
          UMX.untag serverPath
        )

        let watcher =
          new FileSystemWatcher(
            fullPath,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
          )

        watchers.Add watcher

        let fileEvents =
          Observable.merge
            (Observable.merge watcher.Changed watcher.Created)
            watcher.Deleted
          |> Observable.filter(fun e -> not(shouldIgnoreFile e.FullPath))
          |> Observable.map(fun (e: FileSystemEventArgs) ->
            let systemPath = UMX.tag<SystemPath> e.FullPath

            let changeKind =
              match e.ChangeType with
              | WatcherChangeTypes.Created -> Created
              | WatcherChangeTypes.Deleted -> Deleted
              | WatcherChangeTypes.Changed -> Changed
              | _ -> Changed

            let relativeUserPath = Path.GetRelativePath(fullPath, e.FullPath)

            let serverUrl =
              UMX.tag<ServerUrl>(
                Path
                  .Combine(UMX.untag serverPath, relativeUserPath)
                  .Replace('\\', '/')
              )

            {
              serverPath = serverUrl
              userPath = UMX.tag<UserPath> e.FullPath
              oldPath = None
              oldName = None
              changeType = changeKind
              path = systemPath
              name = UMX.tag<SystemPath>(Path.GetFileName(e.FullPath))
            })

        let renamedEvents =
          watcher.Renamed
          |> Observable.filter(fun e -> not(shouldIgnoreFile e.FullPath))
          |> Observable.map(fun (e: RenamedEventArgs) ->
            let systemPath = UMX.tag<SystemPath> e.FullPath
            let relativeUserPath = Path.GetRelativePath(fullPath, e.FullPath)

            let serverUrl =
              UMX.tag<ServerUrl>(
                Path
                  .Combine(UMX.untag serverPath, relativeUserPath)
                  .Replace('\\', '/')
              )

            {
              serverPath = serverUrl
              userPath = UMX.tag<UserPath> e.FullPath
              oldPath = Some(UMX.tag<SystemPath> e.OldFullPath)
              oldName =
                Some(UMX.tag<SystemPath>(Path.GetFileName e.OldFullPath))
              changeType = Renamed
              path = systemPath
              name = UMX.tag<SystemPath>(Path.GetFileName e.FullPath)
            })

        fileChangeObservables.Add(Observable.merge fileEvents renamedEvents)
      else
        logger.LogWarning(
          "Directory {Directory} does not exist, skipping file watcher setup",
          fullPath
        )


    // Set up the processing pipeline
    fileChangeObservables
    |> Observable.mergeSeq
    |> Observable.map(fun event -> async {
      do! processFileChangeEvent logger extensibility files event
      return event
    })
    |> Observable.switchAsync

  let stopWatching
    (logger: ILogger)
    (watchers: ResizeArray<FileSystemWatcher>)
    ()
    =
    logger.LogDebug("Stopping {WatcherCount} file watchers", watchers.Count)

    for watcher in watchers do
      watcher.Dispose()

    watchers.Clear()
    logger.LogDebug("All file watchers stopped and disposed")

  /// Create and initialize a new VirtualFileSystem instance
  let Create(args: VirtualFileSystemArgs) =
    let files = ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>()
    let watchers = ResizeArray<FileSystemWatcher>()

    let fileChangedSubject = Subject<FileChangedEvent>.broadcast

    let mutable connection: IDisposable option = None

    args.Logger.LogDebug("Creating new Virtual File System instance")

    { new VirtualFileSystem with
        member _.Resolve(url: string<ServerUrl>) =
          match files.TryGetValue url with
          | true, entry ->
            args.Logger.LogTrace("Resolved file {Url}", UMX.untag url)
            Some entry.kind
          | false, _ ->
            args.Logger.LogTrace("File not found {Url}", UMX.untag url)
            None

        member _.Load(mountedDirs: MountedDirectories) = async {
          args.Logger.LogDebug(
            "Loading virtual file system with {DirCount} mounted directories",
            mountedDirs.Count
          )

          // Load all files initially and create the file change stream with watchers
          do! loadAllFiles args.Logger args.Extensibility files mountedDirs

          let connectable =
            createFileChangeStream
              args.Logger
              args.Extensibility
              files
              watchers
              mountedDirs
            |> Observable.multicast fileChangedSubject


          // Connect to start the stream
          connection <- Some(connectable.Connect())

          args.Logger.LogDebug(
            "Virtual file system loaded and file watching started"
          )
        }

        member _.ToDisk(?location: string<SystemPath>) = async {
          let outputDir =
            match location with
            | Some path -> UMX.untag path
            | None -> Path.GetTempPath() + Path.GetRandomFileName()

          args.Logger.LogInformation(
            "Exporting virtual file system to disk at {OutputDir}",
            outputDir
          )

          Directory.CreateDirectory(outputDir) |> ignore

          let mutable fileCount = 0

          for KeyValue(serverUrl, entry) in files do
            let relativePath = (UMX.untag serverUrl).TrimStart('/')
            let targetPath = Path.Combine(outputDir, relativePath)
            let targetDir = Path.GetDirectoryName(targetPath) |> nonNull

            Directory.CreateDirectory(targetDir) |> ignore

            match entry.kind with
            | TextFile content ->
              File.WriteAllText(targetPath, content.content)
              fileCount <- fileCount + 1
            | BinaryFile info ->
              File.Copy(UMX.untag info.source, targetPath, true)
              fileCount <- fileCount + 1

          args.Logger.LogInformation(
            "Successfully exported {FileCount} files to {OutputDir}",
            fileCount,
            outputDir
          )

          return UMX.tag<SystemPath> outputDir
        }

        member _.FileChanges = fileChangedSubject

        member _.Dispose() =
          args.Logger.LogDebug("Disposing virtual file system")
          connection |> Option.iter(fun c -> c.Dispose())
          stopWatching args.Logger watchers ()
          args.Logger.LogDebug("Virtual file system disposed")
    }
