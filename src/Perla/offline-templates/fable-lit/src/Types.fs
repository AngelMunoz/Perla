[<AutoOpen>]
module Extensions

open Browser.Types
open Fable.Core

[<Emit("new Event($0, $1)")>]
let createEvent name options : Event = jsNative

[<Emit("new CustomEvent($0, $1)")>]
let createCustomEvent name options : CustomEvent<_> = jsNative


module Types =

  [<RequireQualifiedAccess>]
  type Page =
    | Home
    | Notes
