module Perla.PkgManager.RequestHandler


open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

open FsHttp
open JDeck

open Perla.PkgManager

exception DecodingException of DecodeError

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
  open System.Text.Json

  let create(jsonOptions: JsonSerializerOptions) =

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

          let! response = Response.toStreamTAsync token req

          match! Decoding.auto(response, jsonOptions) with
          | Ok response -> return response
          | Error error -> return raise(DecodingException error)
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

          let! responseStream = Response.toStreamTAsync token req

          match! Decoding.auto(responseStream, jsonOptions) with
          | Ok response -> return response
          | Error error -> return raise(DecodingException error)
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

          let! responseStream = Response.toStreamTAsync token req

          match! Decoding.auto(responseStream, jsonOptions) with
          | Ok response -> return response
          | Error error -> return raise(DecodingException error)
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

          let! responseStream = Response.toStreamTAsync token req

          match! Decoding.auto(responseStream, jsonOptions) with
          | Ok response -> return response
          | Error error -> return raise(DecodingException error)
        }
    }
