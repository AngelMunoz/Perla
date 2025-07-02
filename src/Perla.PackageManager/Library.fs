namespace Perla.PackageManager

open System

open System.Runtime.CompilerServices
open System.Runtime.InteropServices

open Perla.PackageManager.Types
open Perla.PackageManager.Jspm
open Perla.PackageManager.Skypack

[<AutoOpen>]
module PackageManager =

  [<Struct; RequireQualifiedAccess>]
  type EnvTarget =
    | Production
    | Development
    | Testing

  type PackageManager =

    static member AddSkypack
      (
        package: string,
        envTarget: EnvTarget,
        [<Optional>] ?importMap: ImportMap,
        [<Optional>] ?esVersion: string
      ) =
      task {
        let importMap =
          defaultArg importMap { imports = Map.empty; scopes = None }

        let isProduction = (envTarget = EnvTarget.Production)

        let! dependencyInfo =
          Skypack.PackageUrls(package, isProduction, ?esVersion = esVersion)

        let uri =
          if envTarget = EnvTarget.Production then
            dependencyInfo.productionUri
          else
            dependencyInfo.packageUri

        let importMap = {
          importMap with
              imports =
                importMap.imports
                |> Map.add dependencyInfo.name (uri.ToString())
        }

        match!
          JspmGenerator.Install(
            Seq.empty,
            provider = Provider.Skypack,
            inputMap = importMap,
            flattenScope = true
          )
        with
        | Ok response -> return Ok response.map
        | Error err -> return Error err
      }

    static member AddSkypack
      (
        packages: string seq,
        envTarget: EnvTarget,
        [<Optional>] ?importMap: ImportMap,
        [<Optional>] ?esVersion: string
      ) =
      task {
        let importMap =
          defaultArg importMap { imports = Map.empty; scopes = None }

        let isProduction = (envTarget = EnvTarget.Production)

        let! dependencyInfos =
          packages
          |> Seq.map(fun package ->
            Skypack.PackageUrls(package, isProduction, ?esVersion = esVersion))
          |> System.Threading.Tasks.Task.WhenAll

        let packages =
          seq {
            for info in dependencyInfos do
              info.name,
              if envTarget = EnvTarget.Production then
                info.productionUri.ToString()
              else
                info.packageUri.ToString()
          }
          |> Map.ofSeq

        let imports =
          Map.fold
            (fun current nextKey nextValue ->
              current |> Map.add nextKey nextValue)
            importMap.imports
            packages


        let importMap = { importMap with imports = imports }

        match!
          JspmGenerator.Install(
            Seq.empty,
            provider = Provider.Skypack,
            inputMap = importMap,
            flattenScope = true
          )
        with
        | Ok response -> return Ok response.map
        | Error err -> return Error err
      }

    static member AddJspm
      (
        package: string,
        environments: GeneratorEnv seq,
        [<Optional>] ?importMap: ImportMap,
        [<Optional>] ?provider: Provider
      ) =
      task {
        match!
          JspmGenerator.Install(
            [ package ],
            environments,
            flattenScope = true,
            ?inputMap = importMap,
            ?provider = provider
          )
        with
        | Ok response -> return Ok response.map
        | Error err -> return Error err
      }

    static member AddJspm
      (
        packages: string seq,
        environments: GeneratorEnv seq,
        [<Optional>] ?importMap: ImportMap,
        [<Optional>] ?provider: Provider
      ) =
      task {
        match!
          JspmGenerator.Install(
            packages,
            environments,
            flattenScope = true,
            ?inputMap = importMap,
            ?provider = provider
          )
        with
        | Ok response -> return Ok response.map
        | Error err -> return Error err
      }

    static member Regenerate
      (
        packages: string seq,
        environments: GeneratorEnv seq,
        [<Optional>] ?provider: Provider,
        [<Optional>] ?importMap: ImportMap
      ) =
      task {
        match!
          JspmGenerator.Install(
            packages,
            environments,
            flattenScope = true,
            ?provider = provider,
            ?inputMap = importMap
          )
        with
        | Ok response -> return Ok response
        | Error err -> return Error err
      }

[<assembly: Extension;
  Obsolete("This module is deprecated, use Perla.PkgManager instead")>]
do ()
