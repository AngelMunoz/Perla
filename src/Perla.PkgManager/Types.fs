namespace Perla.PkgManager

open System
open System.Collections.Generic
open System.Text.Json

module Constants =
  [<Literal>]
  let JSPM_API_URL = "https://api.jspm.io/"

type ImportMap = {
  imports: Map<string, string>
  scopes: Map<string, Map<string, string>>
  integrity: Map<string, string>
}

type CacheOption =
  | Enabled of bool
  | Offline

type ExportCondition =
  | Development
  | Browser
  | Module
  | Custom of string

module ExportCondition =

  let asString(condition: ExportCondition) =
    match condition with
    | Custom customCondition -> customCondition.ToLowerInvariant()
    | Development -> (nameof Development).ToLowerInvariant()
    | Browser -> (nameof Browser).ToLowerInvariant()
    | Module -> (nameof Module).ToLowerInvariant()

type Provider =
  | JspmIo
  | JspmIoSystem
  | NodeModules
  | Skypack
  | JsDelivr
  | Unpkg
  | EsmSh
  | Custom of string

module Provider =

  let asString(provider: Provider) =
    match provider with
    | JspmIo -> "jspm.io"
    | JspmIoSystem -> "jspm.io#system"
    | NodeModules -> "node_modules"
    | Skypack -> "skypack"
    | JsDelivr -> "jsdelivr"
    | Unpkg -> "unpkg"
    | EsmSh -> "esm.sh"
    | Custom customProvider -> customProvider

/// These options are gathered from the serializable properties in this interface
/// https://jspm.org/docs/generator/interfaces/GeneratorOptions.html
type GeneratorOption =
  | BaseUrl of Uri
  | MapUrl of Uri
  | RootUrl of Uri
  | InputMap of ImportMap
  | DefaultProvider of Provider
  | Providers of Map<string, string>
  | ProviderConfig of Map<string, Map<string, string>>
  | Resolutions of Map<string, string>
  | Env of Set<ExportCondition>
  | Cache of CacheOption
  | Ignore of Set<string>
  | FlattenScopes of bool
  | CombineSubPaths of bool

type DownloadProvider =
  | JspmIo
  | JsDelivr
  | Unpkg

module DownloadProvider =

  let asString(provider: DownloadProvider) =
    match provider with
    | JspmIo -> "jspm.io"
    | JsDelivr -> "jsdelivr"
    | Unpkg -> "unpkg"

  let fromString(value: string) =
    match value.ToLowerInvariant() with
    | "jspm" // Legacy support for "jspm" as a value
    | "jspm.io" -> JspmIo
    | "jsdelivr" -> JsDelivr
    | "unpkg" -> Unpkg
    | _ -> JspmIo // Default to JspmIo if the value is not recognized

type ExcludeOption =
  | Unused
  | Types
  | SourceMaps
  | Readme
  | License

module ExcludeOption =
  let asString(option: ExcludeOption) =
    match option with
    | Unused -> (nameof Unused).ToLowerInvariant()
    | Types -> (nameof Types).ToLowerInvariant()
    | SourceMaps -> (nameof SourceMaps).ToLowerInvariant()
    | Readme -> (nameof Readme).ToLowerInvariant()
    | License -> (nameof License).ToLowerInvariant()

type DownloadOption =
  | Provider of DownloadProvider
  | Exclude of Set<ExcludeOption>

type DownloadPackage = { pkgUrl: Uri; files: string array }

type DownloadResponseError = { error: string }

type DownloadResponse =
  | DownloadError of DownloadResponseError
  | DownloadSuccess of Map<string, DownloadPackage>

type GeneratorResponse = {
  staticDeps: string array
  dynamicDeps: string array
  map: ImportMap
}


module ImportMap =
  open JDeck

  let Empty = {
    imports = Map.empty
    scopes = Map.empty
    integrity = Map.empty
  }

  let Decoder: Decoder<ImportMap> =
    fun element -> decode {
      let! imports =
        element |> Optional.Property.map("imports", Required.string)

      let! scopes =
        element |> Optional.Property.map("scopes", Required.map Required.string)

      let! integrity =
        element |> Optional.Property.map("integrity", Required.string)

      return {
        imports = defaultArg imports Map.empty
        scopes = defaultArg scopes Map.empty
        integrity = defaultArg integrity Map.empty
      }
    }

  let mapEncoder: Encoder<Map<string, string>> =
    fun map ->
      Json.object [
        for KeyValue(key, value) in map -> key, Encode.string value
      ]

  let scopesEncoder: Encoder<Map<string, Map<string, string>>> =
    fun scopes ->
      Json.object [
        for KeyValue(scopeKey, scopeMap) in scopes ->
          scopeKey, mapEncoder scopeMap
      ]

  let Encoder: Encoder<ImportMap> =
    fun map ->

      Json.object [
        if Map.isEmpty map.imports |> not then
          "imports", mapEncoder map.imports
        if Map.isEmpty map.scopes |> not then
          ("scopes", scopesEncoder map.scopes)
        if Map.isEmpty map.integrity |> not then
          ("integrity", mapEncoder map.integrity)
      ]

type ImportMap with

  member this.ToJson(?jsonSerializerOptions: JsonSerializerOptions) =

    ImportMap.Encoder this |> _.ToJsonString(?options = jsonSerializerOptions)

module GeneratorOption =

  let toDict(options: GeneratorOption seq) =
    let finalOptions = Dictionary<string, obj>()

    for option in options do
      match option with
      | BaseUrl uri -> finalOptions.TryAdd("baseUrl", uri.ToString()) |> ignore
      | MapUrl uri -> finalOptions.TryAdd("mapUrl", uri.ToString()) |> ignore
      | RootUrl value ->
        finalOptions.TryAdd("rootUrl", value.ToString()) |> ignore
      | InputMap value -> finalOptions.TryAdd("inputMap", value) |> ignore
      | DefaultProvider value ->
        let providerString =
          match value with
          | Provider.JspmIo -> "jspm.io"
          | JspmIoSystem -> "jspm.io#system"
          | NodeModules -> "nodemodles"
          | Skypack -> "skypack"
          | Provider.JsDelivr -> "jsdelivr"
          | Provider.Unpkg -> "unpkg"
          | EsmSh -> "esm.sh"
          | Custom customProvider -> customProvider

        finalOptions.TryAdd("defaultProvider", providerString) |> ignore
      | Providers value -> finalOptions.TryAdd("providers", value) |> ignore
      | ProviderConfig value ->
        finalOptions.TryAdd("providerConfig", value) |> ignore
      | Resolutions value -> finalOptions.TryAdd("resolutions", value) |> ignore
      | Env value ->
        let envStrings =
          value
          |> Set.map (function
            | Development -> "development"
            | Browser -> "browser"
            | Module -> "module"
            | ExportCondition.Custom customEnv -> customEnv)

        finalOptions.TryAdd("env", envStrings) |> ignore
      | Cache value ->
        match value with
        | Enabled enabled -> finalOptions.TryAdd("cache", enabled) |> ignore
        | Offline -> finalOptions.TryAdd("cache", "offline") |> ignore
      | Ignore value -> finalOptions.TryAdd("ignore", value) |> ignore
      | FlattenScopes value ->
        finalOptions.TryAdd("flattenScopes", value) |> ignore
      | CombineSubPaths value ->
        finalOptions.TryAdd("combineSubPaths", value) |> ignore

    finalOptions

module DownloadResponse =
  open JDeck
  open JDeck.Encoding

  let private downloadPackageDecoder: Decoder<DownloadPackage> =
    fun element -> decode {
      let! pkgUrl = element |> Required.Property.get("pkgUrl", Required.string)
      let! files = element |> Required.Property.array("files", Required.string)

      return { pkgUrl = Uri(pkgUrl); files = files }
    }


  let private downloadResponseErrorDecoder: Decoder<_> =
    fun element -> decode {
      let! error = element |> Required.Property.get("error", Required.string)
      return DownloadResponse.DownloadError { error = error }
    }

  let private downloadResponseSuccessDecoder: Decoder<_> =
    fun element -> decode {
      let! filesMap =
        element |> Required.Property.map("filesMap", downloadPackageDecoder)

      return DownloadResponse.DownloadSuccess filesMap
    }

  let Decoder: Decoder<DownloadResponse> =
    Decode.oneOf [
      downloadResponseErrorDecoder
      downloadResponseSuccessDecoder
    ]
