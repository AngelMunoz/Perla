[<RequireQualifiedAccess>]
module App

open Lit
open Browser.Types
open Types
open Components
open Browser.Dom
open Pages.Home
open Pages.Notes

[<HookComponent>]
let private app () =
  let state, setState = Hook.useState Page.Home

  let onBackRequested _ = printfn "Back requested"

  let goToPage (ev: CustomEvent<Page>) =
    let page = defaultArg ev.detail Page.Home
    setState page

  let getPage page =
    match page with
    | Page.Home -> Home()
    | Page.Notes -> Notes()

  html
    $"""
     <flit-navbar @go-back={onBackRequested} @go-to-page={goToPage}></flit-navbar>
    {getPage state}
  """

let start () =
  Lit.render (document.querySelector "#lit-app") (app ())
