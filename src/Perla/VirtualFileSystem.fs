namespace Perla.VirtualFs

open System
open System.Collections.Concurrent
open System.IO

open Perla
open Perla.Units
open Perla.Plugins
open Perla.Extensibility

open FSharp.UMX
open FSharp.Control
open FSharp.Control.Reactive

open IcedTasks

open Fake.IO.Globbing.Operators
open Microsoft.Extensions.Logging
open AngleSharp.Io

// Types
type MountedDirectories = Map<string<ServerUrl>, string<UserPath>>
type ApplyPluginsFn = FileTransform -> Async<FileTransform>

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

module FileKind =
  let source(file: FileKind) =
    match file with
    | TextFile content -> content.source
    | BinaryFile info -> info.source

  let mimetype(file: FileKind) =
    match file with
    | TextFile content -> content.mimetype
    | BinaryFile info -> info.mimetype

  let filename(file: FileKind) =
    match file with
    | TextFile content -> content.filename
    | BinaryFile info -> info.filename

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
  // Service dependencies
  type VfsServiceDeps = {
    logger: ILogger
    extensibility: ExtensibilityService
    files: ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>
    nodeModulesFiles: ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>
  }

  // Helper functions
  let getMimeType(filename: string) =
    filename
    |> Path.GetExtension
    |> defaultIfNull ""
    |> function
      | ".json" -> MimeTypeNames.ApplicationJson
      | ".jsx"
      | ".ts"
      | ".tsx" -> MimeTypeNames.DefaultJavaScript
      | others -> MimeTypeNames.FromExtension others

  let shouldIgnoreFile(path: string) =
    let normalized = path.Replace("\\", "/")
    let filename = Path.GetFileName(normalized)

    // Check if it's a directory
    if Directory.Exists(path) || isNull filename then
      true
    else
      let filename = nonNull filename

      normalized.Contains("/bin/")
      || normalized.Contains("/obj/")
      || normalized.EndsWith(".fsproj")
      || normalized.EndsWith(".fs")
      || normalized.EndsWith(".fsx")
      // Ignore common temp/backup files
      || filename.EndsWith("~")
      || filename.EndsWith(".tmp")
      || filename.EndsWith(".swp")
      || filename.EndsWith(".swx")
      || filename.EndsWith(".bak")
      || filename.StartsWith(".#")
      || filename.Contains("___jb_tmp___")
      || filename.Contains("___jb_old___")

  let collectSourceFiles (logger: ILogger) (mountedDirs: MountedDirectories) = [
    for KeyValue(serverPath, userPath) in mountedDirs do
      let fullPath = Path.GetFullPath(UMX.untag userPath)

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
        |> Seq.filter File.Exists // Only include actual files, not directories

      let fileList = files |> Seq.toList

      logger.LogDebug(
        "Found {FileCount} files in {Directory}",
        fileList.Length,
        fullPath
      )

      for file in fileList do
        UMX.tag<SystemPath> file, serverPath, UMX.tag<UserPath> fullPath
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

    let cleanRelativePath =
      if relativePath = "." then
        ""
      else
        relativePath.Replace("\\", "/")

    let serverFilePath =
      if cleanRelativePath = "" then targetBase
      elif targetBase = "/" then "/" + cleanRelativePath
      else targetBase.TrimEnd('/') + "/" + cleanRelativePath

    UMX.tag<ServerUrl> serverFilePath

  // Simplified plugin application with reduced logging
  let applyPlugins
    (logger: ILogger)
    (extensibility: ExtensibilityService)
    (content: string)
    (fileLocation: string)
    (extension: string)
    =
    async {
      let fileTransform = {
        content = content
        extension = extension
        fileLocation = fileLocation
      }

      if extensibility.HasPluginsForExtension(extension) then
        logger.LogDebug("Applying plugins for extension {Extension}", extension)

        let allPlugins = extensibility.GetAllPlugins()
        let pluginOrder = allPlugins |> List.map(_.name)

        logger.LogTrace(
          "Running {PluginCount} plugins: {PluginNames}",
          pluginOrder.Length,
          pluginOrder
        )

        let! result = extensibility.RunPlugins pluginOrder fileTransform

        logger.LogDebug(
          "Plugin processing completed, content length: {ResultLength}",
          result.content.Length
        )

        return result
      else
        logger.LogTrace(
          "No plugins available for extension {Extension}",
          extension
        )

        return fileTransform
    }

  // Simplified file reading with retry logic
  let readFileContent
    (sourcePath: string)
    (targetPath: string<ServerUrl>)
    (files: ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>)
    (logger: ILogger)
    =
    async {
      let rec tryReadWithRetry retryCount = async {
        try
          use fs =
            new FileStream(
              sourcePath,
              FileMode.Open,
              FileAccess.Read,
              FileShare.ReadWrite
            )

          use sr = new StreamReader(fs)
          let! content = sr.ReadToEndAsync() |> Async.AwaitTask

          // If the file is empty and we have retries left, wait and retry
          if content.Length = 0 && retryCount > 0 then
            logger.LogDebug(
              "File {FilePath} is empty, retrying... (attempts left: {RetryCount})",
              sourcePath,
              retryCount
            )

            do! Async.Sleep 100
            return! tryReadWithRetry(retryCount - 1)
          else
            return content
        with ex ->
          if retryCount > 0 then
            logger.LogDebug(
              "File read failed for {FilePath}, retrying... (attempts left: {RetryCount})",
              sourcePath,
              retryCount
            )

            do! Async.Sleep 100
            return! tryReadWithRetry(retryCount - 1)
          else
            logger.LogWarning(
              "Could not read file {FilePath} after all retries",
              sourcePath
            )

            // Do not return the contents, at this point
            // the user will report anything if this is not desired.
            return ""
      }

      return! tryReadWithRetry 5
    }

  // Process node_modules files
  let processNodeModulesFile
    (deps: VfsServiceDeps)
    (systemPath: string<SystemPath>)
    (targetPath: string<ServerUrl>)
    (filename: string)
    (extension: string)
    =
    async {
      let sourcePath = UMX.untag systemPath

      deps.logger.LogTrace(
        "Registering node_modules file: {FilePath}",
        sourcePath
      )

      let entry = {
        kind =
          TextFile {
            filename = Path.GetFileName sourcePath |> nonNull
            mimetype = getMimeType(filename + extension)
            content = "" // Content is empty for node_modules
            source = systemPath
          }
        lastModified = File.GetLastWriteTime(sourcePath)
      }

      deps.nodeModulesFiles[targetPath] <- entry
    }

  // Process binary files
  let processBinaryFile
    (deps: VfsServiceDeps)
    (systemPath: string<SystemPath>)
    (targetPath: string<ServerUrl>)
    (filename: string)
    (mimeType: string)
    =
    async {
      let sourcePath = UMX.untag systemPath

      try
        let binaryInfo = {
          filename = filename
          mimetype = mimeType
          source = systemPath
        }

        let entry = {
          kind = BinaryFile binaryInfo
          lastModified = File.GetLastWriteTime sourcePath
        }

        deps.files[targetPath] <- entry
        deps.logger.LogTrace("Processed binary file {FilePath}", sourcePath)
      with ex ->
        deps.logger.LogError(
          "Error processing binary file {FilePath}: {Error}",
          sourcePath,
          ex.Message
        )
    }

  // Process text files with plugins
  let processTextFileWithPlugins
    (deps: VfsServiceDeps)
    (systemPath: string<SystemPath>)
    (targetPath: string<ServerUrl>)
    (filename: string)
    (extension: string)
    (content: string)
    =
    async {
      let sourcePath = UMX.untag systemPath

      deps.logger.LogDebug(
        "Processing text file {FilePath} (content length: {ContentLength})",
        sourcePath,
        content.Length
      )

      let! transform =
        applyPlugins deps.logger deps.extensibility content sourcePath extension
        |> Async.Catch

      let finalTransform, finalExtension =
        match transform with
        | Choice2Of2 ex ->
          deps.logger.LogError(
            "Error applying plugins to file {FilePath}: {Error}",
            sourcePath,
            ex.Message
          )
          // Use original content as fallback when plugins fail
          {
            content = content
            extension = extension
            fileLocation = sourcePath
          },
          extension
        | Choice1Of2 transform -> transform, transform.extension

      let fileContent = {
        filename = Path.GetFileName sourcePath |> nonNull
        mimetype = getMimeType(filename + finalExtension)
        content = finalTransform.content
        source = systemPath
      }

      let finalPath =
        if finalExtension <> extension then
          let newName =
            $"{Path.GetFileNameWithoutExtension(sourcePath)}{finalExtension}"

          let dir = Path.GetDirectoryName(UMX.untag targetPath) |> nonNull

          let newPath =
            UMX.tag<ServerUrl>(Path.Combine(dir, newName).Replace("\\", "/"))

          deps.logger.LogDebug(
            "File extension transformed from {OldExt} to {NewExt}",
            extension,
            finalExtension
          )

          newPath
        else
          targetPath

      try
        let entry = {
          kind = TextFile fileContent
          lastModified = File.GetLastWriteTime(sourcePath)
        }

        deps.files[finalPath] <- entry

        deps.logger.LogDebug(
          "Stored file content for {FilePath}: length={ContentLength}",
          sourcePath,
          fileContent.content.Length
        )
      with ex ->
        deps.logger.LogError(
          "Error storing processed text file {FilePath}: {Error}",
          sourcePath,
          ex.Message
        )
    }

  // Main file processing function
  let processFile
    (deps: VfsServiceDeps)
    (systemPath: string<SystemPath>)
    (userPath: string<UserPath>)
    (serverPath: string<ServerUrl>)
    =
    async {
      let sourcePath = UMX.untag systemPath
      let extension = Path.GetExtension(sourcePath) |> defaultIfNull ""
      let filename = Path.GetFileName(sourcePath) |> nonNull
      let mimeType = getMimeType filename
      let targetPath = transformSourcePath systemPath userPath serverPath

      if sourcePath.Contains("node_modules") then
        do! processNodeModulesFile deps systemPath targetPath filename extension
      else
        deps.logger.LogDebug(
          "Processing file {FilePath} -> {TargetPath}",
          sourcePath,
          UMX.untag targetPath
        )

        if mimeType = MimeTypeNames.Binary then
          do! processBinaryFile deps systemPath targetPath filename mimeType
        else
          let! content =
            readFileContent sourcePath targetPath deps.files deps.logger

          do!
            processTextFileWithPlugins
              deps
              systemPath
              targetPath
              filename
              extension
              content
    }

  // Process file change events
  let processFileChangeEvent (deps: VfsServiceDeps) (event: FileChangedEvent) = async {
    try
      deps.logger.LogInformation(
        "Processing file change event {ChangeType} for {FilePath}",
        event.changeType,
        UMX.untag event.path
      )

      let isNodeModules = (UMX.untag event.path).Contains("node_modules")

      if isNodeModules then
        // We do not watch or process node_modules file changes
        ()
      else
        match event.changeType with
        | Deleted ->
          let targetPath =
            transformSourcePath event.path event.userPath event.serverPath

          let removed = deps.files.TryRemove targetPath

          if fst removed then
            deps.logger.LogDebug(
              "Removed file {FilePath} from virtual file system",
              UMX.untag targetPath
            )
          else
            deps.logger.LogWarning(
              "Attempted to remove non-existent file {FilePath}",
              UMX.untag targetPath
            )
        | Created
        | Changed ->
          do! processFile deps event.path event.userPath event.serverPath
        | Renamed ->
          // Remove the old entry if present
          match event.oldPath, event.oldName with
          | Some oldSystemPath, Some _ ->
            let oldTargetPath =
              transformSourcePath oldSystemPath event.userPath event.serverPath

            let removed = deps.files.TryRemove oldTargetPath

            if fst removed then
              deps.logger.LogDebug(
                "Removed old file {FilePath} due to rename",
                UMX.untag oldTargetPath
              )
            else
              deps.logger.LogWarning(
                "Attempted to remove non-existent old file {FilePath} during rename",
                UMX.untag oldTargetPath
              )
          | _ -> ()

          // Add/update the new file
          do! processFile deps event.path event.userPath event.serverPath
    with ex ->
      deps.logger.LogError(
        ex,
        "Error processing file change event for {FilePath}",
        UMX.untag event.path
      )
  }

  // Load all files initially
  let loadAllFiles (deps: VfsServiceDeps) (mountedDirs: MountedDirectories) = async {
    let sourceFiles = collectSourceFiles deps.logger mountedDirs

    deps.logger.LogInformation(
      "Loading {FileCount} files from {DirCount} mounted directories",
      sourceFiles.Length,
      mountedDirs.Count
    )

    do!
      sourceFiles
      |> List.map(fun (systemPath, serverPath, userPath) ->
        processFile deps systemPath userPath serverPath)
      |> Async.Parallel
      |> Async.Ignore

    deps.logger.LogInformation(
      "Successfully loaded all files into virtual file system"
    )
  }

  // Create file change stream with watchers
  let createFileChangeStream
    (deps: VfsServiceDeps)
    (watchers: ResizeArray<FileSystemWatcher>)
    (mountedDirs: MountedDirectories)
    : IObservable<FileChangedEvent> =
    let fileChangeObservables = ResizeArray<IObservable<FileChangedEvent>>()
    let mountedDirs = mountedDirs |> Map.remove(UMX.tag "/node_modules")

    deps.logger.LogDebug(
      "Creating file change stream for {DirCount} mounted directories",
      mountedDirs.Count
    )

    for KeyValue(serverPath, userPath) in mountedDirs do
      let fullPath = Path.GetFullPath(UMX.untag userPath)

      if Directory.Exists fullPath then
        deps.logger.LogDebug(
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
          |> Observable.filter(fun e ->
            not(shouldIgnoreFile e.FullPath)
            && not(e.FullPath.Contains("node_modules")))
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
          |> Observable.filter(fun e ->
            not(shouldIgnoreFile e.FullPath)
            && not(e.FullPath.Contains("node_modules")))
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
        deps.logger.LogWarning(
          "Directory {Directory} does not exist, skipping file watcher setup",
          fullPath
        )

    // Set up the processing pipeline
    fileChangeObservables
    |> Observable.mergeSeq
    // map to task/async and then using switchmap drops events
    // we'd rather use flatmapAsync for this case
    |> Observable.flatmapAsync(fun event -> async {
      do! processFileChangeEvent deps event
      return event
    })

  // Stop watching files
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

    let nodeModulesFiles =
      ConcurrentDictionary<string<ServerUrl>, VirtualFileEntry>()

    let watchers = ResizeArray<FileSystemWatcher>()
    let fileChangedSubject = Subject<FileChangedEvent>.broadcast
    let mutable connection: IDisposable option = None

    let tryJsFallback
      (lookup: string<ServerUrl> -> 'a option)
      (url: string<ServerUrl>)
      =
      let s = UMX.untag url

      if s.EndsWith(".ts") || s.EndsWith(".tsx") || s.EndsWith(".jsx") then
        let jsUrl =
          s.Substring(0, s.LastIndexOf(".")) + ".js" |> UMX.tag<ServerUrl>

        match lookup jsUrl with
        | Some v -> Some v
        | None -> lookup url
      else
        lookup url

    let lookup(u: string<ServerUrl>) =
      match u with
      | Found files entry ->
        args.Logger.LogTrace("Resolved file {Url}", UMX.untag u)
        Some entry.kind
      | _ ->
        args.Logger.LogTrace("File not found {Url}", UMX.untag u)
        None

    args.Logger.LogDebug("Creating new Virtual File System instance")

    { new VirtualFileSystem with
        member _.Resolve(url: string<ServerUrl>) =
          if (UMX.untag url).Contains("node_modules") then
            match url with
            | Found nodeModulesFiles entry ->
              match entry.kind with
              | TextFile content ->
                let filePath = UMX.untag content.source

                try
                  let fileContent = File.ReadAllText(filePath)
                  let updatedContent = { content with content = fileContent }
                  Some(TextFile updatedContent)
                with _ ->
                  Some(TextFile content)
              | _ -> Some entry.kind
            | _ -> None
          else
            tryJsFallback lookup url

        member _.Load(mountedDirs: MountedDirectories) = async {
          args.Logger.LogDebug(
            "Loading virtual file system with {DirCount} mounted directories",
            mountedDirs.Count
          )

          // Load all files initially and create the file change stream with watchers
          do!
            loadAllFiles
              {
                logger = args.Logger
                extensibility = args.Extensibility
                files = files
                nodeModulesFiles = nodeModulesFiles
              }
              mountedDirs

          let connectable =
            createFileChangeStream
              {
                logger = args.Logger
                extensibility = args.Extensibility
                files = files
                nodeModulesFiles = nodeModulesFiles
              }
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

          Directory.CreateDirectory outputDir |> ignore

          // Local function to copy a single entry
          let copyEntry(KeyValue(serverUrl: string<ServerUrl>, entry)) =
            let serverUrl = UMX.untag serverUrl
            let relativePath = serverUrl.TrimStart('/')
            let targetPath = Path.Combine(outputDir, relativePath)
            let targetDir = Path.GetDirectoryName targetPath |> nonNull

            Directory.CreateDirectory targetDir |> ignore

            args.Logger.LogTrace(
              "Copying file {File} to {TargetPath}",
              serverUrl,
              targetPath
            )

            match entry.kind with
            | TextFile content ->
              let fileSource = UMX.untag content.source

              if fileSource.Contains("node_modules") then
                File.Copy(fileSource, targetPath, true)
              else
                File.WriteAllText(targetPath, content.content)
            | BinaryFile info ->
              File.Copy(UMX.untag info.source, targetPath, true)

          // Merge all entries into a single array
          let allEntries = [|
            yield! files |> Seq.toArray
            yield! nodeModulesFiles |> Seq.toArray
          |]

          // Copy all files in parallel
          allEntries |> Array.Parallel.iter copyEntry

          args.Logger.LogInformation(
            "Successfully exported {FileCount} files to {OutputDir}",
            allEntries.Length,
            outputDir
          )

          return UMX.tag<SystemPath> outputDir
        }

        member _.FileChanges = fileChangedSubject

        member _.Dispose() =
          args.Logger.LogTrace("Disposing virtual file system")
          connection |> Option.iter(_.Dispose())
          stopWatching args.Logger watchers ()
          args.Logger.LogTrace("Virtual file system disposed")
    }
