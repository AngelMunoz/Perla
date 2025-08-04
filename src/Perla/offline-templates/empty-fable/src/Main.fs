module Main

open Fable.Core
open Browser.Dom
open Browser.Types

type Counter(initialValue: int) =
  let mutable count = initialValue

  member this.Increment() =
    count <- count + 1
    count

  member this.Decrement() =
    count <- count - 1
    count

  member this.GetValue() = count


let createCounter initialValue =
  let counter = Counter initialValue
  let element = document.createElement("div")

  element.innerHTML <- sprintf "<p>Counter: %d</p>" (counter.GetValue())

  document.addEventListener(
    "click",
    fun _ ->
      counter.Increment() |> ignore
      element.innerHTML <- sprintf "<p>Counter: %d</p>" (counter.GetValue())
  )

  element

document.addEventListener(
  "DOMContentLoaded",
  fun _ ->
    let root = document.getElementById "feliz-app"
    let element = document.createElement("section")

    element.innerHTML <-
      """<h1>Welcome to the Fable Feliz App!</h1>
  <p>This is a basic template using Feliz for Fable applications.</p>
  <p>Enjoy building your app!</p>"""

    if not(isNull root) then
      root.appendChild element |> ignore
      element.appendChild(createCounter 0) |> ignore
    else
      console.error("App root element not found.")
)
