namespace Perla.PackageManager

open System
open System.Runtime.InteropServices
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Text.Json
open System.Text.Json.Serialization
open System.IO

[<Extension>]
type DictionaryExtensions =

  [<Extension>]
  static member ToSeq(dictionary: Dictionary<'TKey, 'TValue>) = seq {
    for KeyValue(key, value) in dictionary -> key, value
  }

module Types =

  [<Obsolete("Use PkgManager.ImportMap instead")>]
  type ImportMap = {
    imports: Map<string, string>
    scopes: Map<string, Map<string, string>> option
  } with

    member this.ToJson([<Optional>] ?indented: bool) =
      let opts =
        JsonSerializerOptions(
          WriteIndented = defaultArg indented true,
          DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        )

      JsonSerializer.Serialize(this, opts)

    static member CreateMap
      (
        imports: Dictionary<string, string>,
        [<Optional>] ?scopes: Dictionary<string, Dictionary<string, string>>
      ) =
      let imports = imports.ToSeq() |> Map.ofSeq

      let scopes =
        match scopes with
        | Some scopes ->
          seq {
            for KeyValue(scopeKey, scopeValue) in scopes ->
              scopeKey, scopeValue.ToSeq() |> Map.ofSeq
          }
          |> Map.ofSeq
          |> Some
        | None -> None

      { imports = imports; scopes = scopes }

    static member FromString(content: string) =
      try
        JsonSerializer.Deserialize<ImportMap>(content) |> Ok
      with ex ->
        Error ex.Message

    static member FromStringAsync(content: Stream) = task {
      try
        let! result = JsonSerializer.DeserializeAsync<ImportMap>(content)
        return Ok result
      with ex ->
        return Error ex.Message
    }

  [<Struct; RequireQualifiedAccess>]
  type GeneratorEnv =
    | Browser
    | Development
    | Production
    | Module
    | Node
    | Deno

    member this.AsString =
      match this with
      | Browser -> "browser"
      | Development -> "development"
      | Production -> "production"
      | Module -> "module"
      | Node -> "node"
      | Deno -> "deno"

  [<Struct; RequireQualifiedAccess>]
  type Provider =
    | Jspm
    | Skypack
    | Unpkg
    | Jsdelivr
    | JspmSystem
    | EsmSh

    member this.AsString =
      match this with
      | Jspm -> "jspm"
      | Skypack -> "skypack"
      | Unpkg -> "unpkg"
      | Jsdelivr -> "jsdelivr"
      | JspmSystem -> "jspm#system"
      | EsmSh -> "esm.sh"

    static member FromString(value: string) =
      match value.ToLowerInvariant() with
      | "jspm" -> Jspm
      | "skypack" -> Skypack
      | "unpkg" -> Unpkg
      | "jsdelivr" -> Jsdelivr
      | "jspm.system" -> JspmSystem
      | "jspm#system" -> JspmSystem
      | "esm.sh" -> EsmSh
      | "esmsh" -> EsmSh
      | _ -> Jspm

module Constants =
  [<Literal>]
  let JSPM_API = "https://api.jspm.io/generate"

  [<Literal>]
  let SKYPACK_CDN = "https://cdn.skypack.dev"

  [<Literal>]
  let SKYPACK_API = "https://api.skypack.dev/v1"

[<AutoOpen>]
module TypeExtensions =
  open Types

  [<Extension>]
  type ImportMapExtensions =

    [<Extension>]
    static member WithImports
      (map: ImportMap, imports: Dictionary<string, string>)
      =
      {
        map with
            imports = imports.ToSeq() |> Map.ofSeq
      }

    [<Extension>]
    static member WithScopes
      (
        map: ImportMap,
        [<Optional>] ?scopes: Dictionary<string, Dictionary<string, string>>
      ) =
      let scopes =
        match scopes with
        | Some scopes ->
          seq {
            for KeyValue(scopeKey, scopeValue) in scopes ->
              scopeKey, scopeValue.ToSeq() |> Map.ofSeq
          }
          |> Map.ofSeq
          |> Some
        | None -> None

      { map with scopes = scopes }
