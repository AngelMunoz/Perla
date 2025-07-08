module Pages.Home

open Lit

let Home () =
  html
    $"""
    <h3>Counter at 0</h3>
    <flit-counter></flit-counter>
    <h3>Counter at 100</h3>
    <flit-counter initial="100"></flit-counter>
  """
