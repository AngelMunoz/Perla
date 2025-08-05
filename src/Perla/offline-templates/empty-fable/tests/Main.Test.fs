module Tests.Main

open Main

open Fable.Core
open Browser

open Perla.Fable.QUnit

let initialize =
    Tests.test "Counter initializes correctly"
    <| fun assert' ->
        let initial = 10
        let counter = Counter initial
        let value = counter.GetValue()
        Expect.equal value initial "Counter should initialize to the given value" assert'

let increment =
    Tests.test "Counter increments correctly"
    <| fun assert' ->
        let counter = Counter 5
        let initialValue = counter.GetValue()
        let newValue = counter.Increment()
        Expect.equal newValue (initialValue + 1) "Counter should increment by 1" assert'

let decrement =
    Tests.test "Counter decrements correctly"
    <| fun assert' ->
        let counter = Counter 5
        let initialValue = counter.GetValue()
        let newValue = counter.Decrement()
        Expect.equal newValue (initialValue - 1) "Counter should decrement by 1" assert'

Tests.testList "Main App Tests" [ initialize; increment; decrement ]
