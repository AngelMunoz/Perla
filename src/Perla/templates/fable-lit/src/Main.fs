module Main

open Components
Fable.Core.JsInterop.importSideEffects "./styles.css"

// register your custom elements here
Counter.register ()
Navbar.register ()
App.start ()
