module Tests.Components

open App.Utils

open Fable.Core
open Browser

open Perla.Fable.QUnit

let addTests =
  Tests.test "add function works correctly"
  <| fun assert' ->
    let result = add 5 3
    Expect.equal result 8 "Should add two positive numbers" assert'

let formatNameTests =
  Tests.test "formatName formats correctly"
  <| fun assert' ->
    let result = formatName "John" "Doe"

    Expect.equal
      result
      "Doe, John"
      "Should format name as LastName, FirstName"
      assert'

let isEvenTests =
  Tests.test "isEven detects even numbers"
  <| fun assert' ->
    let even = isEven 4
    let odd = isEven 5
    Expect.isTrue even "4 should be even" assert'
    Expect.isFalse odd "5 should not be even" assert'

Tests.testList "Components Utils Tests" [
  addTests
  formatNameTests
  isEvenTests
]
