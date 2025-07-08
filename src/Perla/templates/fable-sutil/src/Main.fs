module Main


open Sutil
open Fable.Core.JsInterop

importSideEffects "./styles.css"

// Start the app
App.view () |> Program.mountElement "fable-app"
