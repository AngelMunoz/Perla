namespace Perla.Build

open Microsoft.Extensions.Logging

open AngleSharp.Html.Dom

open FSharp.UMX


open Perla.FileSystem
open Perla.Types
open Perla.Units
open Perla.PkgManager

[<RequireQualifiedAccess>]
module Build =

  val EntryPoints:
    IHtmlDocument ->
      string<ServerUrl> seq * string<ServerUrl> seq * string<ServerUrl> seq

  val Externals: PerlaConfig -> string seq

  val Index:
    IHtmlDocument * string<ServerUrl> seq * string<ServerUrl> seq * ImportMap ->
      string
