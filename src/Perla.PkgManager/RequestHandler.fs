module Perla.PkgManager.RequestHandler


open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

open FsHttp
open Thoth.Json.Net

open Perla.PkgManager

type JspmService =
  abstract member Install:
    options: IDictionary<string, obj> * ?cancellationToken: CancellationToken ->
      Task<GeneratorResponse>

  abstract member Update:
    options: IDictionary<string, obj> * ?cancellationToken: CancellationToken ->
      Task<GeneratorResponse>

  abstract member Uninstall:
    options: IDictionary<string, obj> * ?cancellationToken: CancellationToken ->
      Task<GeneratorResponse>

  abstract member Download:
    packages: string *
    options: (string * string) seq *
    ?cancellationToken: CancellationToken ->
      Task<DownloadResponse>


module JspmService =

  let create() =
    { new JspmService with
        member _.Download(packages, options, ?cancellationToken) = task {
          let token = defaultArg cancellationToken CancellationToken.None
          let url = $"{Constants.JSPM_API_URL}download/"

          let! req =
            http {
              GET url

              query [ "packages", packages; yield! options ]

              config_cancellationToken token
            }
            |> Request.sendTAsync

          let! response = Response.toTextTAsync token req

          return Decode.unsafeFromString DownloadResponse.Decoder response
        }

        member _.Install(options, ?cancellationToken) = task {
          let token = defaultArg cancellationToken CancellationToken.None
          let url = $"{Constants.JSPM_API_URL}generate"

          let! req =
            http {
              POST url
              body
              jsonSerialize options
            }
            |> Config.cancellationToken token
            |> Request.sendTAsync

          let! responseStream = Response.toTextTAsync token req

          return
            Decode.unsafeFromString GeneratorResponse.Decoder responseStream
        }

        member _.Uninstall(options, ?cancellationToken) = task {
          let token = defaultArg cancellationToken CancellationToken.None
          let url = $"{Constants.JSPM_API_URL}generate"

          let! req =
            http {
              POST url
              body
              jsonSerialize options
            }
            |> Config.cancellationToken token
            |> Request.sendTAsync

          let! responseStream = Response.toTextTAsync token req

          return
            Decode.unsafeFromString GeneratorResponse.Decoder responseStream
        }

        member _.Update(options, ?cancellationToken) = task {
          let token = defaultArg cancellationToken CancellationToken.None
          let url = $"{Constants.JSPM_API_URL}generate"

          let! req =
            http {
              POST url
              body
              jsonSerialize options
            }
            |> Config.cancellationToken token
            |> Request.sendTAsync

          let! responseStream = Response.toTextTAsync token req

          return
            Decode.unsafeFromString GeneratorResponse.Decoder responseStream
        }
    }
