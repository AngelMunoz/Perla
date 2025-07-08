namespace Perla.PkgManager

open System
open System.IO
open System.Text.Json
open System.Threading
open Microsoft.Extensions.Logging
open FsHttp
open IcedTasks
open System.Threading.Tasks

type PkgManagerConfiguration = { GlobalCachePath: string; cwd: string }

type PkgManagerServiceArgs = {
  reqHandler: RequestHandler.JspmService
  logger: ILogger
  config: PkgManagerConfiguration
}

type PkgManager =

  abstract member Install:
    packages: string seq *
    ?options: GeneratorOption seq *
    ?cancellationToken: CancellationToken ->
      Task<GeneratorResponse>

  abstract member Update:
    map: ImportMap *
    packages: string seq *
    ?options: GeneratorOption seq *
    ?cancellationToken: CancellationToken ->
      Task<GeneratorResponse>

  abstract member Uninstall:
    map: ImportMap *
    packages: string seq *
    ?options: GeneratorOption seq *
    ?cancellationToken: CancellationToken ->
      Task<GeneratorResponse>

  abstract member GoOffline:
    map: ImportMap *
    ?options: DownloadOption seq *
    ?cancellationToken: CancellationToken ->
      Task<ImportMap>

module Result =
  let toOption result =
    match result with
    | Ok value -> Some value
    | Error _ -> None

module PkgManager =

  /// strictly speaking, these are not node modules; however, I think
  /// it might help with existing tooling trying to discover the sources
  [<Literal>]
  let LOCAL_CACHE_PREFIX = "/node_modules"

  let extractPackagesWithScopes(map: ImportMap) =
    let imports = map.imports |> Map.values
    let scopeImports = map.scopes |> Map.values |> Seq.collect Map.values

    [
      for value in [| yield! imports; yield! scopeImports |] do
        let uri = Uri(value)

        match ProviderOps.extractFromUri uri with
        | Ok package -> package
        | Error _ -> ()
    ]
    |> Set

  let cacheResponse
    (args: PkgManagerServiceArgs)
    (response: Map<string, DownloadPackage>)
    =
    cancellableTask {
      let { logger = logger; config = config } = args

      let cacheDir = DirectoryInfo(config.GlobalCachePath)

      let localCacheDir =
        DirectoryInfo(Path.Combine(config.cwd, "node_modules"))

      let perlaDir =
        DirectoryInfo(Path.Combine(localCacheDir.FullName, ".perla"))

      localCacheDir.Create()
      localCacheDir.Create()
      perlaDir.Create()

      logger.LogDebug(
        "Caching downloaded packages to: {cacheDir}",
        cacheDir.FullName
      )

      logger.LogTrace("Working with Download Map: {downloadMap}", response)

      let tasks =
        response
        |> Map.toArray
        |> Array.map(fun (package, content) -> asyncEx {
          let! token = Async.CancellationToken
          let medusaPkgPath = Path.Combine(perlaDir.FullName, package)
          let localPkgTarget = Path.Combine(cacheDir.FullName, package)

          // Extract the package name without a version for flat structure
          let packageName =
            ProviderOps.extractPackageNameForFlatStructure package

          let flatPkgPath = Path.Combine(localCacheDir.FullName, packageName)

          // Create Parent Directories for the medusa path
          Path.GetDirectoryName medusaPkgPath
          |> Directory.CreateDirectory
          |> ignore

          // Create Parent Directories for flat path
          Path.GetDirectoryName flatPkgPath
          |> Directory.CreateDirectory
          |> ignore

          // If the medusa store already has the package, skip creating the symbolic link
          if Directory.Exists medusaPkgPath then
            logger.LogDebug(
              "Package '{package}' already exists in medusa store, skipping symbolic link creation.",
              package
            )
          else
            logger.LogDebug(
              "Creating symlink to store: {medusaPkgPath} -> {localPkgTarget}",
              medusaPkgPath,
              localPkgTarget
            )

            Directory.CreateSymbolicLink(medusaPkgPath, localPkgTarget)
            |> ignore

          // Create flat symlink if it doesn't exist
          if Directory.Exists flatPkgPath then
            logger.LogDebug(
              "Flat package '{packageName}' already exists, skipping flat symlink creation.",
              packageName
            )
          else
            logger.LogDebug(
              "Creating flat symlink: {flatPkgPath} -> {medusaPkgPath}",
              flatPkgPath,
              medusaPkgPath
            )

            Directory.CreateSymbolicLink(flatPkgPath, medusaPkgPath) |> ignore

          if Directory.Exists(Path.Combine(cacheDir.FullName, package)) then
            logger.LogDebug(
              "Package '{package}' already exists, skipping download.",
              package
            )

            return ()
          else
            logger.LogDebug("Downloading package '{package}'...", package)

            logger.LogDebug(
              "Working through {content.files.Length} files...",
              content.files.Length
            )

            for file in content.files do
              let filePath = Path.Combine(cacheDir.FullName, package, file)
              let downloadUri = Uri(content.pkgUrl, file)

              let! response =
                get(downloadUri.ToString())
                |> Config.timeoutInSeconds 10
                |> Config.cancellationToken token
                |> Request.sendAsync

              use! content = response |> Response.toStreamAsync

              Directory.CreateDirectory(
                Path.GetDirectoryName filePath |> Path.GetFullPath
              )
              |> ignore

              use file = File.OpenWrite filePath

              do! content.CopyToAsync(file, cancellationToken = token)

              logger.LogTrace("Downloaded file: {filePath}", filePath)

            return ()
        })

      do! Async.Parallel tasks |> Async.Ignore
      return ()
    }

  let download
    (dependencies: PkgManagerServiceArgs)
    (options: DownloadOption seq)
    (map: ImportMap)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let packages = extractPackagesWithScopes map

      let {
            reqHandler = reqHandler
            logger = logger
          } =
        dependencies

      let options = [
        for option in options do
          match option with
          | Provider provider ->
            "provider",
            match provider with
            | JspmIo -> "jspm.io"
            | JsDelivr -> "jsdelivr"
            | Unpkg -> "unpkg"
          | Exclude excludes ->
            "exclude",
            [|
              for exclude in excludes ->
                match exclude with
                | Unused -> "unused"
                | Types -> "types"
                | SourceMaps -> "sourcemaps"
                | Readme -> "readme"
                | License -> "license"
            |]
            |> String.concat ","
      ]

      let packages = packages |> String.concat ","

      logger.LogTrace("Downloading packages: {packages}", packages)
      logger.LogTrace("Download options: {options}", options)

      let! response =
        reqHandler.Download(packages, options, cancellationToken = token)

      match response with
      | DownloadError err ->
        return raise(Exception $"Download failed: {err.error}")
      | DownloadSuccess response ->
        logger.LogDebug(
          "Download Success: {count} packages downloaded",
          response.Count
        )

        return response
    }

  let install
    (dependencies: PkgManagerServiceArgs)
    (options: GeneratorOption seq)
    (packages: Set<string>)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let { reqHandler = reqHandler } = dependencies

      let finalOptions = GeneratorOption.toDict options
      finalOptions.Add("install", packages)

      return! reqHandler.Install(finalOptions, cancellationToken = token)
    }

  let update
    (dependencies: PkgManagerServiceArgs)
    (options: GeneratorOption seq)
    (map: ImportMap)
    (packages: Set<string>)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let { reqHandler = reqHandler } = dependencies

      let finalOptions = GeneratorOption.toDict options
      finalOptions.Add("update", packages)
      finalOptions["inputMap"] <- map

      return! reqHandler.Update(finalOptions, cancellationToken = token)
    }

  let uninstall
    (dependencies: PkgManagerServiceArgs)
    (options: GeneratorOption seq)
    (map: ImportMap)
    (packages: Set<string>)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let { reqHandler = reqHandler } = dependencies

      let finalOptions = GeneratorOption.toDict options
      finalOptions.Add("uninstall", packages)
      finalOptions["inputMap"] <- map

      return! reqHandler.Uninstall(finalOptions, cancellationToken = token)
    }

  /// Generates a map that can be persisted to disk for offline use
  let goOffline
    (dependencies: PkgManagerServiceArgs)
    (options: DownloadOption seq)
    (map: ImportMap)
    =
    cancellableTask {
      let { logger = logger } = dependencies

      let allScopedImports =
        map.scopes |> Map.values |> Seq.collect Map.toSeq |> Map.ofSeq

      let combinedImports =
        map.imports
        |> Map.fold (fun state k v -> state |> Map.add k v) allScopedImports

      let! pkgs =
        download dependencies options { map with imports = combinedImports }

      // Cache the downloaded packages
      do! cacheResponse dependencies pkgs

      let localPrefix = LOCAL_CACHE_PREFIX

      // Helper function to extract the package name from a key
      let extractPackageName(package: string) =
        match ProviderOps.extractPkgAndVersion package with
        | Some(packageName, _) -> packageName
        | None -> package // fallback to original string if parsing fails

      // Helper function to find a matching package key
      let findMatchingKey (pkgName: string) (importUrl: string) =
        pkgs
        |> Map.keys
        |> Seq.tryFind(fun k ->
          let pkgNameFromKey = extractPackageName k
          pkgNameFromKey = pkgName || importUrl.Contains(k))

      // Helper function to convert URL to the local cache path
      let convertToLocalPath importUrl matchingKey isScoped =
        match matchingKey with
        | None -> importUrl
        | Some key ->
          let uri = Uri importUrl
          let filePath = ProviderOps.extractFilePath logger uri
          // If extractFilePath returned the original URL (couldn't extract), keep it as is
          if filePath = importUrl then
            importUrl
          else
            let basePath =
              if isScoped then
                // Scoped packages point to .perla/<package@version>
                Path.Combine(localPrefix, ".perla", key)
              else
                // Non-scoped packages point to a flat structure
                let packageName =
                  ProviderOps.extractPackageNameForFlatStructure key

                Path.Combine(localPrefix, packageName)

            Path.Combine(basePath, filePath).Replace('\\', '/')

      // Helper function to update a scope map
      let updateScopeMap(scopeMap: Map<string, string>) =
        scopeMap
        |> Map.map(fun pkgName importUrl ->
          let matchingKey = findMatchingKey pkgName importUrl

          logger.LogDebug(
            "Updating scope '{pkgName}' with import URL '{importUrl}'",
            pkgName,
            importUrl
          )

          let converted = convertToLocalPath importUrl matchingKey true

          logger.LogDebug(
            "Converted import URL to local path: '{converted}'",
            converted
          )

          converted)

      // Build a new imports map with local paths
      let updatedImports =
        map.imports
        |> Map.map(fun pkgName importUrl ->
          let matchingKey = findMatchingKey pkgName importUrl
          convertToLocalPath importUrl matchingKey false)

      let updatedScopes =
        map.scopes
        |> Seq.map(fun (KeyValue(_, scopeMap)) ->
          localPrefix + "/", updateScopeMap scopeMap)
        |> Map.ofSeq

      let offlineMap = {
        map with
            imports = updatedImports
            scopes = updatedScopes
      }

      logger.LogDebug("Generated offline map {map}", offlineMap)

      return offlineMap
    }


  let create(dependencies: PkgManagerServiceArgs) : PkgManager =
    { new PkgManager with
        member _.Install(packages, options, cancellationToken) =
          install
            dependencies
            (defaultArg options Seq.empty)
            (Set packages)
            (defaultArg cancellationToken CancellationToken.None)

        member _.Update(map, packages, options, cancellationToken) =
          update
            dependencies
            (defaultArg options Seq.empty)
            map
            (Set packages)
            (defaultArg cancellationToken CancellationToken.None)

        member _.Uninstall(map, packages, options, cancellationToken) =
          uninstall
            dependencies
            (defaultArg options Seq.empty)
            map
            (Set packages)
            (defaultArg cancellationToken CancellationToken.None)

        member _.GoOffline(map, options, cancellationToken) =
          goOffline
            dependencies
            (defaultArg options Seq.empty)
            map
            (defaultArg cancellationToken CancellationToken.None)
    }

  type ImportMap with
    member this.ExtractDependencies() =
      // extract the package name and the version from the import map
      this.imports
      |> Map.toSeq
      |> Seq.map(fun (key, value) ->
        let uri = Uri value

        match ProviderOps.extractFromUri uri with
        | Ok package ->
          ProviderOps.extractPkgAndVersion package
          |> Option.defaultWith(fun () ->
            // if we can't extract the package name and version, return the key as is
            key, None)
        | Error _ -> key, None)
      |> Set

    member this.FindDependency(packageName: string) =
      let imports = this.imports |> Map.toSeq

      imports
      |> Seq.tryPick(fun (key, value) ->
        let uri = Uri value

        ProviderOps.extractFromUri uri
        |> Result.toOption
        |> Option.bind(fun package -> ProviderOps.extractPkgAndVersion package)
        |> Option.orElseWith(fun () ->
          if
            key.Equals(
              packageName,
              StringComparison.InvariantCultureIgnoreCase
            )
          then
            Some(key, None)
          else
            None))
