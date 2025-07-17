module Main

open Fable.Core
open Browser

let element = document.createElement("section")

element.innerHTML <-
  """
    <h1>Welcome to the Fable Feliz App!</h1>
    <p>This is a basic template using Feliz for Fable applications.</p>
    <p>Enjoy building your app!</p>
"""

document.addEventListener(
  "DOMContentLoaded",
  fun _ ->
    let root = document.getElementById "feliz-app"

    if not(isNull root) then
      root.appendChild element |> ignore
    else
      console.error("App root element not found.")
)
