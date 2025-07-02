namespace Medusa

open System
open System.Text.RegularExpressions

module Provider =
  open Microsoft.Extensions.Logging

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
