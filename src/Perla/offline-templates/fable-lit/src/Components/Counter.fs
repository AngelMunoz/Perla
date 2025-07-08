[<RequireQualifiedAccess>]
module Components.Counter

open Lit

[<LitElement("flit-counter")>]
let private Counter () =
  let (_,props) =
    LitElement.init
      (fun config ->
        config.props <- {| initial = Prop.Of(0, attribute = "initial") |})

  let count, setCount = Hook.useState props.initial.Value

  html
    $"""
    <p>{count}</p>
    <button @click={fun _ -> setCount (count + 1)}>Increment</button>
    <button @click={fun _ -> setCount (count - 1)}>Decrement</button>
    <button @click={fun _ -> setCount (props.initial.Value)}>Reset</button>
  """

let register () = ()
