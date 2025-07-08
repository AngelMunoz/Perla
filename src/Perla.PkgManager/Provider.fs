namespace Perla.PkgManager

open System
open System.Text.RegularExpressions
open Microsoft.Extensions.Logging

module ProviderOps =

  let private jspmRegex = lazy Regex "npm:((?:@[^/]+/)?[^@/]+@[^/]+)"
  let private esmRegex = lazy Regex "\*((?:@[^/]+/)?[^@/]+@[^/]+)"
  let private jsdelivrRegex = lazy Regex "npm/((?:@[^/]+/)?[^@/]+@[^/]+)"
  let private unpkgRegex = lazy Regex "/((?:@[^/]+/)?[^@/]+@[^/]+)"

  type ExtractionError = { host: string; url: Uri }

  type FilePathExtractionError =
    | UnsupportedProvider of host: string * url: Uri
    | MissingPrefix of expectedPrefix: string * url: Uri
    | InvalidPackagePath of packagePath: string * url: Uri * reason: string

  let private (|IsJSpm|IsEsmSh|IsJsDelivr|IsUnpkg|NotSupported|)(url: Uri) =
    match url.Host with
    | "ga.jspm.io" -> IsJSpm
    | "esm.sh" -> IsEsmSh
    | "cdn.jsdelivr.net" -> IsJsDelivr
    | "unpkg.com" -> IsUnpkg
    | host -> NotSupported host

  let extractFromUri(uri: Uri) =
    match uri with
    | IsJSpm ->
      let m = jspmRegex.Value.Match uri.PathAndQuery

      if m.Success then
        m.Groups[1].Value |> Ok
      else
        Error { host = uri.Host; url = uri }
    | IsEsmSh ->
      let m = esmRegex.Value.Match uri.PathAndQuery

      if m.Success then
        m.Groups[1].Value |> Ok
      else
        Error { host = uri.Host; url = uri }
    | IsJsDelivr ->
      let m = jsdelivrRegex.Value.Match uri.PathAndQuery

      if m.Success then
        m.Groups[1].Value |> Ok
      else
        Error { host = uri.Host; url = uri }
    | IsUnpkg ->
      let m = unpkgRegex.Value.Match uri.PathAndQuery

      if m.Success then
        m.Groups[1].Value |> Ok
      else
        Error { host = uri.Host; url = uri }
    | NotSupported host -> Error { host = host; url = uri }

  let extractFilePath (logger: ILogger) (uri: Uri) =
    // Common helper to extract file path after package@version part
    let extractAfterPackage(packagePath: string) =
      if packagePath.StartsWith("@") then
        // Scoped packages: @scope/package@version/file.js -> need second slash
        let firstSlash = packagePath.IndexOf '/'

        if firstSlash >= 0 then
          let afterScope = packagePath.Substring(firstSlash + 1)
          let secondSlash = afterScope.IndexOf '/'

          if secondSlash >= 0 then
            Ok(afterScope.Substring(secondSlash + 1))
          else
            Error(
              InvalidPackagePath(
                packagePath,
                uri,
                "missing file path after scoped package@version"
              )
            )
        else
          Error(
            InvalidPackagePath(
              packagePath,
              uri,
              "missing package name separator in scoped package"
            )
          )
      else
        // Regular packages: package@version/file.js -> need first slash
        let firstSlash = packagePath.IndexOf '/'

        if firstSlash >= 0 then
          Ok(packagePath.Substring(firstSlash + 1))
        else
          Error(
            InvalidPackagePath(
              packagePath,
              uri,
              "missing file path after package@version"
            )
          )

    let result =
      match uri with
      | IsJSpm ->
        // Extract after "npm:" prefix
        let pathQuery = uri.PathAndQuery
        let npmIndex = pathQuery.IndexOf("npm:")

        if npmIndex >= 0 then
          pathQuery.Substring(npmIndex + 4) |> extractAfterPackage
        else
          Error(MissingPrefix("npm:", uri))
      | IsJsDelivr ->
        // Extract after "npm/" prefix
        let pathQuery = uri.PathAndQuery
        let npmIndex = pathQuery.IndexOf("npm/")

        if npmIndex >= 0 then
          pathQuery.Substring(npmIndex + 4) |> extractAfterPackage
        else
          Error(MissingPrefix("npm/", uri))
      | IsEsmSh
      | IsUnpkg ->
        // Extract after root slash
        uri.PathAndQuery.TrimStart '/' |> extractAfterPackage
      | NotSupported host -> Error(UnsupportedProvider(host, uri))

    match result with
    | Ok filePath -> filePath
    | Error error ->
      let errorMsg =
        match error with
        | UnsupportedProvider(host, url) ->
          $"Unsupported URL provider '{host}' for URL '{url}'"
        | MissingPrefix(expectedPrefix, url) ->
          $"Unable to find '{expectedPrefix}' prefix in URL '{url}'"
        | InvalidPackagePath(packagePath, url, reason) ->
          $"Invalid package path '{packagePath}' in URL '{url}': {reason}"

      logger.LogWarning("Unable to extract file path: {Error}", errorMsg)
      uri.ToString()

  /// Extract package name and version from a package string
  /// Returns Some(packageName, version option) if a valid package is found, None if invalid
  /// Examples:
  /// - "package@1.0.0" -> Some("package", Some "1.0.0")
  /// - "@scope/package@1.0.0" -> Some("@scope/package", Some "1.0.0")
  /// - "package" -> Some("package", None)
  /// - "@scope/package" -> Some("@scope/package", None)
  /// - "" -> None
  /// - "@" -> None
  let extractPkgAndVersion(package: string) : (string * string option) option =
    if String.IsNullOrWhiteSpace(package) then
      None
    elif package.StartsWith("@") then
      // Scoped package: @scope/package@version -> (@scope/package, Some version)
      let parts = package.Split('@')

      if parts.Length > 2 then
        let packageName = "@" + parts[1]
        let version = parts[2]
        // Validate that we have a proper scoped package name
        if String.IsNullOrWhiteSpace(parts[1]) then
          None
        else
          Some(packageName, Some version)
      elif parts.Length = 2 && not(String.IsNullOrWhiteSpace(parts[1])) then
        // @scope/package without version
        let packageName = "@" + parts[1]
        Some(packageName, None)
      else
        None
    else
      // Regular package: package@version -> (package, Some version)
      let parts = package.Split('@')

      if parts.Length > 1 then
        let packageName = parts[0]
        let version = parts[1]
        // Validate that we have a proper package name
        if String.IsNullOrWhiteSpace(packageName) then
          None
        else
          Some(packageName, Some version)
      elif not(String.IsNullOrWhiteSpace(parts[0])) then
        // Regular package without version
        Some(package, None)
      else
        None

  /// Extract package name with version for flat directory structure
  /// For scoped packages, keeps the full string; for regular packages, removes version
  /// This is used specifically for creating flat directory structures in node_modules
  let extractPackageNameForFlatStructure(package: string) : string =
    if package.StartsWith("@") then
      // Scoped package: @scope/package@version -> @scope/package@version (keep full)
      let parts = package.Split('@')

      if parts.Length > 2 then
        "@" + parts[1] + "@" + parts[2]
      else
        package
    else
      // Regular package: package@version -> package (remove version)
      match extractPkgAndVersion package with
      | Some(packageName, _) -> packageName
      | None -> package // fallback to original string if parsing fails

  let extractPackageNameFromKeys (packageName: string) (map: Map<string, _>) =
    // Find the first key that matches the package name
    map
    |> Map.tryFindKey(fun key _ ->
      key.Equals(packageName, StringComparison.InvariantCultureIgnoreCase))

  /// Given a key (e.g., solid-js/web) and a packageWithVersion (e.g., solid-js@1.9.7),
  /// returns solid-js@1.9.7/web if key is a deep import, otherwise returns packageWithVersion
  let combineDeepImport (key: string) (packageWithVersion: string) : string =
    if key.Contains("/") then
      let idx = key.IndexOf("/")
      let deepPath = key.Substring(idx)
      // Remove any deep path from packageWithVersion if present
      let basePkg =
        match packageWithVersion.IndexOf("/") with
        | i when i > 0 -> packageWithVersion.Substring(0, i)
        | _ -> packageWithVersion

      basePkg + deepPath
    else
      packageWithVersion
