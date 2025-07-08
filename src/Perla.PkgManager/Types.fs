namespace Perla.PkgManager

open System
open System.Collections.Generic

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
  open Thoth.Json.Net

  let Empty = {
    imports = Map.empty
    scopes = Map.empty
    integrity = Map.empty
  }

  let Decoder: Decoder<ImportMap> =
    Decode.object(fun get ->
      let imports =
        get.Optional.Field "imports" (Decode.dict Decode.string)
        |> Option.defaultValue Map.empty

      let scopes =
        get.Optional.Field "scopes" (Decode.dict(Decode.dict Decode.string))
        |> Option.defaultValue Map.empty

      let integrity =
        get.Optional.Field "integrity" (Decode.dict Decode.string)
        |> Option.defaultValue Map.empty

      {
        imports = imports
        scopes = scopes
        integrity = integrity
      })

  let Encoder: Encoder<ImportMap> =
    fun map ->

      Encode.object [
        if map.imports |> Map.isEmpty |> not then
          "imports",
          map.imports |> Map.map(fun _ v -> Encode.string v) |> Encode.dict
        if map.scopes |> Map.isEmpty |> not then
          "scopes",
          map.scopes
          |> Map.map(fun _ v ->
            v |> Map.map(fun _ v -> Encode.string v) |> Encode.dict)
          |> Encode.dict
        if map.integrity |> Map.isEmpty |> not then
          "integrity",
          map.integrity |> Map.map(fun _ v -> Encode.string v) |> Encode.dict
      ]

type ImportMap with

  member this.ToJson(?indentSize: int) =
    let indentSize = defaultArg indentSize 0
    ImportMap.Encoder this |> Thoth.Json.Net.Encode.toString indentSize

module GeneratorResponse =
  open Thoth.Json.Net

  let Decoder: Decoder<GeneratorResponse> =
    Decode.Auto.generateDecoderCached<GeneratorResponse>(
      CamelCase,
      Extra.empty |> Extra.withCustom ImportMap.Encoder ImportMap.Decoder
    )

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
  open Thoth.Json.Net

  let private downloadPackageDecoder: Decoder<DownloadPackage> =
    Decode.object(fun get ->
      let pkgUrl = get.Required.Field "pkgUrl" Decode.string
      let files = get.Required.Field "files" (Decode.array Decode.string)
      { pkgUrl = Uri pkgUrl; files = files })

  let private downloadResponseErrorDecoder: Decoder<DownloadResponseError> =
    Decode.object(fun get -> {
      error = get.Required.Field "error" Decode.string
    })

  let private downloadResponseSuccessDecoder
    : Decoder<Map<string, DownloadPackage>> =
    Decode.object(fun get ->
      let filesMap =
        get.Required.Field "filesMap" (Decode.dict downloadPackageDecoder)

      filesMap)

  let Decoder: Decoder<DownloadResponse> =
    Decode.oneOf [
      downloadResponseSuccessDecoder |> Decode.map DownloadSuccess
      downloadResponseErrorDecoder |> Decode.map DownloadError
    ]
