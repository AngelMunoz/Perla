module Perla.Fable.Mocha

open Fable.Core
open Fable.Core.JS
open System

type TestCallback = unit -> unit
type AsyncTestCallback = unit -> Promise<unit>
type HookCallback = unit -> unit
type AsyncHookCallback = unit -> Promise<unit>

module BDD =
  // BDD Interface
  [<Erase; AutoOpen>]
  type BDD =
    // Main test functions
    [<Emit("describe($0, $1)")>]
    static member inline describe(title: string, fn: unit -> unit) : unit =
      jsNative

    [<Emit("context($0, $1)")>]
    static member inline context(title: string, fn: unit -> unit) : unit =
      jsNative

    [<Emit("it($0, $1)")>]
    static member inline it(title: string, fn: TestCallback) : unit = jsNative

    [<Emit("it($0, $1)")>]
    static member inline it(title: string, fn: AsyncTestCallback) : unit =
      jsNative

    static member inline it(title: string, fn: unit -> Async<unit>) : unit =
      BDD.it(title, fn >> Async.StartAsPromise)

    [<Emit("specify($0, $1)")>]
    static member inline specify(title: string, fn: TestCallback) : unit =
      jsNative

    [<Emit("specify($0, $1)")>]
    static member inline specify(title: string, fn: AsyncTestCallback) : unit =
      jsNative

    static member inline specify
      (title: string, fn: unit -> Async<unit>)
      : unit =
      BDD.specify(title, fn >> Async.StartAsPromise)

    // Hooks
    [<Emit("before($0)")>]
    static member inline before(fn: HookCallback) : unit = jsNative

    [<Emit("before($0)")>]
    static member inline before(fn: AsyncHookCallback) : unit = jsNative

    static member inline before(fn: unit -> Async<unit>) : unit =
      BDD.before(fn >> Async.StartAsPromise)

    [<Emit("after($0)")>]
    static member inline after(fn: HookCallback) : unit = jsNative

    [<Emit("after($0)")>]
    static member inline after(fn: AsyncHookCallback) : unit = jsNative

    static member inline after(fn: unit -> Async<unit>) : unit =
      BDD.after(fn >> Async.StartAsPromise)

    [<Emit("beforeEach($0)")>]
    static member inline beforeEach(fn: HookCallback) : unit = jsNative

    [<Emit("beforeEach($0)")>]
    static member inline beforeEach(fn: AsyncHookCallback) : unit = jsNative

    static member inline beforeEach(fn: unit -> Async<unit>) : unit =
      BDD.beforeEach(fn >> Async.StartAsPromise)

    [<Emit("afterEach($0)")>]
    static member inline afterEach(fn: HookCallback) : unit = jsNative

    [<Emit("afterEach($0)")>]
    static member inline afterEach(fn: AsyncHookCallback) : unit = jsNative

    static member inline afterEach(fn: unit -> Async<unit>) : unit =
      BDD.afterEach(fn >> Async.StartAsPromise)

    // Test modifiers
    [<Emit("describe.only($0, $1)")>]
    static member inline describeOnly(title: string, fn: unit -> unit) : unit =
      jsNative

    [<Emit("describe.skip($0, $1)")>]
    static member inline describeSkip(title: string, fn: unit -> unit) : unit =
      jsNative

    [<Emit("it.only($0, $1)")>]
    static member inline itOnly(title: string, fn: TestCallback) : unit =
      jsNative

    [<Emit("it.only($0, $1)")>]
    static member inline itOnly(title: string, fn: AsyncTestCallback) : unit =
      jsNative

    static member inline itOnly(title: string, fn: unit -> Async<unit>) : unit =
      BDD.itOnly(title, fn >> Async.StartAsPromise)

    [<Emit("it.skip($0, $1)")>]
    static member inline itSkip(title: string, fn: TestCallback) : unit =
      jsNative

    [<Emit("it.skip($0, $1)")>]
    static member inline itSkip(title: string, fn: AsyncTestCallback) : unit =
      jsNative

    static member inline itSkip(title: string, fn: unit -> Async<unit>) : unit =
      BDD.itSkip(title, fn >> Async.StartAsPromise)

module TDD =
  // TDD Interface
  [<Erase; AutoOpen>]
  type TDD =
    [<Emit("suite($0, $1)")>]
    static member inline suite(title: string, fn: unit -> unit) : unit =
      jsNative

    [<Emit("test($0, $1)")>]
    static member inline test(title: string, fn: TestCallback) : unit = jsNative

    [<Emit("test($0, $1)")>]
    static member inline test(title: string, fn: AsyncTestCallback) : unit =
      jsNative

    static member inline test(title: string, fn: unit -> Async<unit>) : unit =
      TDD.test(title, fn >> Async.StartAsPromise)

    [<Emit("suiteSetup($0)")>]
    static member inline suiteSetup(fn: HookCallback) : unit = jsNative

    [<Emit("suiteSetup($0)")>]
    static member inline suiteSetup(fn: AsyncHookCallback) : unit = jsNative

    static member inline suiteSetup(fn: unit -> Async<unit>) : unit =
      TDD.suiteSetup(fn >> Async.StartAsPromise)

    [<Emit("suiteTeardown($0)")>]
    static member inline suiteTeardown(fn: HookCallback) : unit = jsNative

    [<Emit("suiteTeardown($0)")>]
    static member inline suiteTeardown(fn: AsyncHookCallback) : unit = jsNative

    static member inline suiteTeardown(fn: unit -> Async<unit>) : unit =
      TDD.suiteTeardown(fn >> Async.StartAsPromise)

    [<Emit("setup($0)")>]
    static member inline setup(fn: HookCallback) : unit = jsNative

    [<Emit("setup($0)")>]
    static member inline setup(fn: AsyncHookCallback) : unit = jsNative

    static member inline setup(fn: unit -> Async<unit>) : unit =
      TDD.setup(fn >> Async.StartAsPromise)

    [<Emit("teardown($0)")>]
    static member inline teardown(fn: HookCallback) : unit = jsNative

    [<Emit("teardown($0)")>]
    static member inline teardown(fn: AsyncHookCallback) : unit = jsNative

    static member inline teardown(fn: unit -> Async<unit>) : unit =
      TDD.teardown(fn >> Async.StartAsPromise)

    [<Emit("suite.only($0, $1)")>]
    static member inline suiteOnly(title: string, fn: unit -> unit) : unit =
      jsNative

    [<Emit("suite.skip($0, $1)")>]
    static member inline suiteSkip(title: string, fn: unit -> unit) : unit =
      jsNative

    [<Emit("test.only($0, $1)")>]
    static member inline testOnly(title: string, fn: TestCallback) : unit =
      jsNative

    [<Emit("test.only($0, $1)")>]
    static member inline testOnly(title: string, fn: AsyncTestCallback) : unit =
      jsNative

    static member inline testOnly
      (title: string, fn: unit -> Async<unit>)
      : unit =
      TDD.testOnly(title, fn >> Async.StartAsPromise)

    [<Emit("test.skip($0, $1)")>]
    static member inline testSkip(title: string, fn: TestCallback) : unit =
      jsNative

    [<Emit("test.skip($0, $1)")>]
    static member inline testSkip(title: string, fn: AsyncTestCallback) : unit =
      jsNative

    static member inline testSkip
      (title: string, fn: unit -> Async<unit>)
      : unit =
      TDD.testSkip(title, fn >> Async.StartAsPromise)

module QUnit =
  // QUnit Interface (flat style)
  [<Erase; AutoOpen>]
  type QUnitStyle =
    [<Emit("suite($0)")>]
    static member inline suite(title: string) : unit = jsNative

    [<Emit("test($0, $1)")>]
    static member inline test(title: string, fn: TestCallback) : unit = jsNative

    [<Emit("test($0, $1)")>]
    static member inline test(title: string, fn: AsyncTestCallback) : unit =
      jsNative

    static member inline test(title: string, fn: unit -> Async<unit>) : unit =
      QUnitStyle.test(title, fn >> Async.StartAsPromise)

    [<Emit("before($0)")>]
    static member inline before(fn: HookCallback) : unit = jsNative

    [<Emit("before($0)")>]
    static member inline before(fn: AsyncHookCallback) : unit = jsNative

    static member inline before(fn: unit -> Async<unit>) : unit =
      QUnitStyle.before(fn >> Async.StartAsPromise)

    [<Emit("after($0)")>]
    static member inline after(fn: HookCallback) : unit = jsNative

    [<Emit("after($0)")>]
    static member inline after(fn: AsyncHookCallback) : unit = jsNative

    static member inline after(fn: unit -> Async<unit>) : unit =
      QUnitStyle.after(fn >> Async.StartAsPromise)

    [<Emit("beforeEach($0)")>]
    static member inline beforeEach(fn: HookCallback) : unit = jsNative

    [<Emit("beforeEach($0)")>]
    static member inline beforeEach(fn: AsyncHookCallback) : unit = jsNative

    static member inline beforeEach(fn: unit -> Async<unit>) : unit =
      QUnitStyle.beforeEach(fn >> Async.StartAsPromise)

    [<Emit("afterEach($0)")>]
    static member inline afterEach(fn: HookCallback) : unit = jsNative

    [<Emit("afterEach($0)")>]
    static member inline afterEach(fn: AsyncHookCallback) : unit = jsNative

    static member inline afterEach(fn: unit -> Async<unit>) : unit =
      QUnitStyle.afterEach(fn >> Async.StartAsPromise)

module Expect =
  [<Emit("throw new Error($0)")>]
  let throwError(message: string) : unit = jsNative

  let equal (expected: 'T) (actual: 'T) =
    if not(expected = actual) then
      throwError $"Expected {expected} but got {actual}"

  let notEqual (expected: 'T) (actual: 'T) =
    if expected = actual then
      throwError $"Expected {expected} to not equal {actual}"

  let isTrue(actual: bool) =
    if not actual then
      throwError "Expected true but got false"

  let isFalse(actual: bool) =
    if actual then
      throwError "Expected false but got true"

  let isTruthy (actual: 'T) (failureMessage: string) =
    js $"""if(!{actual}) {{ throw new Error({failureMessage}); }}"""

  let isFalsey (actual: 'T) (failureMessage: string) =
    js $"""if({actual}) {{ throw new Error({failureMessage}); }}"""

  let isNull'(actual: 'T) =
    if not(isNull actual) then
      throwError $"Expected null but got {actual}"

  let isNotNull(actual: 'T) =
    if isNull actual then
      throwError "Expected value to not be null"

  let isEmpty(actual: string) =
    if not(System.String.IsNullOrEmpty actual) then
      throwError $"Expected empty string but got '{actual}'"

  let isNotEmpty(actual: string) =
    if System.String.IsNullOrEmpty actual then
      throwError "Expected non-empty string but got empty or null"

  let contains (substring: string) (actual: string) =
    if not(actual.Contains substring) then
      throwError $"Expected '{actual}' to contain '{substring}'"

  let closeTo (expected: float) (actual: float) (delta: float) =
    if abs(actual - expected) > delta then
      throwError $"Expected {actual} to be close to {expected} within {delta}"

  let throws(f: unit -> 'T) =
    try
      f() |> ignore
      throwError "Expected function to throw but it didn't"
    with _ ->
      ()

  let throwsWithMessage (expectedMessage: string) (f: unit -> 'T) =
    try
      f() |> ignore
      throwError "Expected function to throw but it didn't"
    with
    | ex when ex.Message = expectedMessage -> ()
    | ex ->
      throwError
        $"Expected exception with message '{expectedMessage}' but got '{ex.Message}'"

type CallbackKind =
  | Test of TestCallback
  | AsyncTest of AsyncTestCallback
  | AsyncWorkflow of (unit -> Async<unit>)

  member inline this.Value<'T>() : 'T =
    match this with
    | Test f -> unbox<'T> f
    | AsyncTest f -> unbox<'T> f
    | AsyncWorkflow f -> unbox<'T> f

type TestModifier =
  | Normal
  | Only
  | Skip

type Test = {
  name: string
  test: CallbackKind
  modifier: TestModifier
}

type TestConfig = {
  before: CallbackKind option
  beforeEach: CallbackKind option
  afterEach: CallbackKind option
  after: CallbackKind option
}

type TestList = Test list

module TestConfig =
  let Empty() = {
    before = None
    beforeEach = None
    afterEach = None
    after = None
  }

module Tests =
  open BDD

  let inline test (name: string) (f: unit -> unit) : Test = {
    name = name
    test = Test f
    modifier = Normal
  }

  let inline testPromise (name: string) (f: unit -> Promise<unit>) : Test = {
    name = name
    test = AsyncTest f
    modifier = Normal
  }

  let inline testAsync (name: string) (f: unit -> Async<unit>) : Test = {
    name = name
    test = AsyncWorkflow f
    modifier = Normal
  }

  let inline testOnly (name: string) (f: unit -> unit) : Test = {
    name = name
    test = Test f
    modifier = Only
  }

  let inline testOnlyPromise (name: string) (f: unit -> Promise<unit>) : Test = {
    name = name
    test = AsyncTest f
    modifier = Only
  }

  let inline testOnlyAsync (name: string) (f: unit -> Async<unit>) : Test = {
    name = name
    test = AsyncWorkflow f
    modifier = Only
  }

  let inline testSkip (name: string) (f: unit -> unit) : Test = {
    name = name
    test = Test f
    modifier = Skip
  }

  let inline testSkipPromise (name: string) (f: unit -> Promise<unit>) : Test = {
    name = name
    test = AsyncTest f
    modifier = Skip
  }

  let inline testSkipAsync (name: string) (f: unit -> Async<unit>) : Test = {
    name = name
    test = AsyncWorkflow f
    modifier = Skip
  }

  let inline runHook(hook: CallbackKind option) =
    match hook with
    | Some(Test f) -> before f
    | Some(AsyncTest f) -> before f
    | Some(AsyncWorkflow f) -> before f
    | None -> ()

  let inline runTest(test: Test) =
    match test.modifier, test.test with
    | Normal, Test f -> it(test.name, f)
    | Normal, AsyncTest f -> it(test.name, f)
    | Normal, AsyncWorkflow f -> it(test.name, f)
    | Only, Test f -> itOnly(test.name, f)
    | Only, AsyncTest f -> itOnly(test.name, f)
    | Only, AsyncWorkflow f -> itOnly(test.name, f)
    | Skip, Test f -> itSkip(test.name, f)
    | Skip, AsyncTest f -> itSkip(test.name, f)
    | Skip, AsyncWorkflow f -> itSkip(test.name, f)

  let inline testList (name: string) (tests: TestList) : Test = {
    name = name
    test = Test(fun () -> describe(name, fun () -> tests |> List.iter runTest))
    modifier = Normal
  }

  let testListWith
    (name: string)
    (config: TestConfig)
    (tests: TestList)
    : Test =
    {
      name = name
      test =
        Test(fun () ->
          describe(
            name,
            fun () ->
              runHook config.before

              config.beforeEach
              |> Option.iter (function
                | Test f -> beforeEach f
                | AsyncTest f -> beforeEach f
                | AsyncWorkflow f -> beforeEach f)

              config.afterEach
              |> Option.iter (function
                | Test f -> afterEach f
                | AsyncTest f -> afterEach f
                | AsyncWorkflow f -> afterEach f)

              config.after
              |> Option.iter (function
                | Test f -> after f
                | AsyncTest f -> after f
                | AsyncWorkflow f -> after f)

              tests |> List.iter runTest
          ))
      modifier = Normal
    }

  let inline testListOnly (name: string) (tests: TestList) : Test = {
    name = name
    test =
      Test(fun () -> describeOnly(name, fun () -> tests |> List.iter runTest))
    modifier = Only
  }

  let inline testListSkip (name: string) (tests: TestList) : Test = {
    name = name
    test =
      Test(fun () -> describeSkip(name, fun () -> tests |> List.iter runTest))
    modifier = Skip
  }

  let inline run(tests: TestList) = tests |> List.iter runTest
