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
  val EnsureBody: IHtmlDocument -> AngleSharp.Dom.IElement
  val EnsureHead: IHtmlDocument -> AngleSharp.Dom.IElement

  val EntryPoints:
    IHtmlDocument ->
      string<ServerUrl> seq * string<ServerUrl> seq * string<ServerUrl> seq

  val Externals: PerlaConfig -> string seq

  val Index:
    IHtmlDocument *
    ImportMap *
    jsExtras: string<ServerUrl> seq *
    cssExtras: string<ServerUrl> seq ->
      string
