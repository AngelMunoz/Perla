module Tests.QUnit

open Fable.Core
open Fable.Core.JS

type ExpectedMatcher =
  U4<(unit -> bool), exn, System.Text.RegularExpressions.Regex, System.Type>

type Assert =
  abstract async: ?count: int -> (unit -> unit)

  abstract closeTo:
    actual: float * expected: float * delta: float * ?message: string -> unit

  abstract deepEqual: actual: 'T * expected: 'T * ?message: string -> unit
  abstract equal: actual: 'T * expected: 'T * ?message: string -> unit
  abstract expect: amount: int -> unit
  abstract ``false``: result: 'T * ?message: string -> unit
  abstract notDeepEqual: actual: 'T * expected: 'T * ?message: string -> unit
  abstract notEqual: actual: 'T * expected: 'T * ?message: string -> unit
  abstract notOk: result: 'T * ?message: string -> unit
  abstract notPropContains: actual: 'T * expected: 'T * ?message: string -> unit
  abstract notPropEqual: actual: 'T * expected: 'T * ?message: string -> unit
  abstract notStrictEqual: actual: 'T * expected: 'T * ?message: string -> unit
  abstract ok: result: 'T * ?message: string -> unit
  abstract propContains: actual: 'T * expected: 'T * ?message: string -> unit
  abstract propEqual: actual: 'T * expected: 'T * ?message: string -> unit

  abstract raises:
    block: (unit -> unit) * ?expected: ExpectedMatcher * ?message: string ->
      unit

  abstract strictEqual: actual: 'T * expected: 'T * ?message: string -> unit

  abstract throws:
    block: (unit -> unit) * ?expected: ExpectedMatcher * ?message: string ->
      unit

  abstract ``true``: result: 'T * ?message: string -> unit
  abstract step: value: string -> unit
  abstract verifySteps: steps: string[] * ?message: string -> unit
  abstract timeout: duration: int -> unit

  abstract rejects:
    promise: Promise<'T> * ?expectedMatcher: ExpectedMatcher * ?message: string ->
      Promise<unit>

type TestCallback = Assert -> unit
type AsyncTestCallback = Assert -> Promise<unit>

type Hooks =
  abstract before: (Assert -> unit) -> unit
  abstract beforeEach: (Assert -> unit) -> unit
  abstract afterEach: (Assert -> unit) -> unit
  abstract after: (Assert -> unit) -> unit

type ModuleOptions =
  abstract before: (Assert -> unit) option
  abstract beforeEach: (Assert -> unit) option
  abstract afterEach: (Assert -> unit) option
  abstract after: (Assert -> unit) option

type HookCallback = Hooks -> unit

[<Erase>]
type QUnit =
  // Main test methods
  [<Emit("QUnit.test($0, $1)")>]
  static member inline test(name: string, callback: TestCallback) : unit =
    jsNative

  [<Emit("QUnit.test($0, $1)")>]
  static member inline test(name: string, callback: AsyncTestCallback) : unit =
    jsNative

  // Test variants
  [<Emit("QUnit.test.only($0, $1)")>]
  static member inline testOnly(name: string, callback: TestCallback) : unit =
    jsNative

  [<Emit("QUnit.test.only($0, $1)")>]
  static member inline testOnly
    (name: string, callback: AsyncTestCallback)
    : unit =
    jsNative

  [<Emit("QUnit.test.skip($0, $1)")>]
  static member inline testSkip(name: string, ?callback: TestCallback) : unit =
    jsNative

  [<Emit("QUnit.test.skip($0, $1)")>]
  static member inline testSkip
    (name: string, ?callback: AsyncTestCallback)
    : unit =
    jsNative

  [<Emit("QUnit.test.todo($0, $1)")>]
  static member inline testTodo(name: string, ?callback: TestCallback) : unit =
    jsNative

  [<Emit("QUnit.test.todo($0, $1)")>]
  static member inline testTodo
    (name: string, ?callback: AsyncTestCallback)
    : unit =
    jsNative

  [<Emit("QUnit.test.if($0, $1, $2)")>]
  static member inline testIf
    (name: string, condition: bool, callback: TestCallback)
    : unit =
    jsNative

  [<Emit("QUnit.test.if($0, $1, $2)")>]
  static member inline testIf
    (name: string, condition: bool, callback: AsyncTestCallback)
    : unit =
    jsNative

  // Main module methods
  [<Emit("QUnit.module($0)")>]
  static member inline module'(name: string) : unit = jsNative

  [<Emit("QUnit.module($0, $1)")>]
  static member inline module'(name: string, scope: HookCallback) : unit =
    jsNative

  [<Emit("QUnit.module($0, $1)")>]
  static member inline module'(name: string, options: ModuleOptions) : unit =
    jsNative

  [<Emit("QUnit.module($0, $1, $2)")>]
  static member inline module'
    (name: string, options: ModuleOptions, scope: HookCallback)
    : unit =
    jsNative

  // Module variants
  [<Emit("QUnit.module.only($0, $1)")>]
  static member inline moduleOnly(name: string, scope: HookCallback) : unit =
    jsNative

  [<Emit("QUnit.module.skip($0, $1)")>]
  static member inline moduleSkip(name: string, scope: HookCallback) : unit =
    jsNative

  [<Emit("QUnit.module.todo($0, $1)")>]
  static member inline moduleTodo(name: string, scope: HookCallback) : unit =
    jsNative

  [<Emit("QUnit.module.if($0, $1, $2)")>]
  static member inline moduleIf
    (name: string, condition: bool, scope: HookCallback)
    : unit =
    jsNative


module Expect =
  let inline equal
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.strictEqual(actual, expected, message)

  let inline notEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notStrictEqual(actual, expected, message)

  let inline deepEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.deepEqual(actual, expected, message)

  let inline notDeepEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notDeepEqual(actual, expected, message)

  let inline looseEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.equal(actual, expected, message)

  let inline notLooseEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notEqual(actual, expected, message)

  let inline isTrue (value: bool) (message: string) (assert': Assert) =
    assert'.``true``(value, message)

  let inline isFalse (value: bool) (message: string) (assert': Assert) =
    assert'.``false``(value, message)

  let inline isOk (value: 'T) (message: string) (assert': Assert) =
    assert'.ok(value, message)

  let inline isNotOk (value: 'T) (message: string) (assert': Assert) =
    assert'.notOk(value, message)

  let inline propEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.propEqual(actual, expected, message)

  let inline notPropEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notPropEqual(actual, expected, message)

  let inline propContains
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.propContains(actual, expected, message)

  let inline notPropContains
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notPropContains(actual, expected, message)

  let inline closeTo
    (actual: float)
    (expected: float)
    (delta: float)
    (message: string)
    (assert': Assert)
    =
    assert'.closeTo(actual, expected, delta, message)

  let inline throws (f: unit -> unit) (message: string) (assert': Assert) =
    assert'.throws(f, message = message)

  let inline throwsWithMatcher
    (f: unit -> unit)
    (matcher: ExpectedMatcher)
    (message: string)
    (assert': Assert)
    =
    assert'.throws(f, matcher, message)

  let inline rejects
    (promise: Promise<'T>)
    (message: string)
    (assert': Assert)
    =
    assert'.rejects(promise, message = message)

  let inline rejectsWithMatcher
    (promise: Promise<'T>)
    (matcher: ExpectedMatcher)
    (message: string)
    (assert': Assert)
    =
    assert'.rejects(promise, matcher, message)

  let inline step (value: string) (assert': Assert) = assert'.step(value)

  let inline verifySteps (steps: string[]) (message: string) (assert': Assert) =
    assert'.verifySteps(steps, message)

  let inline expectCount (count: int) (assert': Assert) = assert'.expect(count)

  let inline timeout (duration: int) (assert': Assert) =
    assert'.timeout(duration)

  let inline asyncTimeout (count: int) (assert': Assert) =
    assert'.async(count = count)

  let inline async(assert': Assert) = assert'.async()

type CallbackKind =
  | Test of TestCallback
  | AsyncTest of AsyncTestCallback

  member inline this.Value<'T>() : 'T =
    match this with
    | Test f -> unbox<'T> f
    | AsyncTest f -> unbox<'T> f

type Test = { name: string; test: CallbackKind }

type TestList = Test list

type TestConfig = {
  before: (Assert -> unit) option
  beforeEach: (Assert -> unit) option
  afterEach: (Assert -> unit) option
  after: (Assert -> unit) option
}

module TestConfig =
  let inline Empty() = {
    before = None
    beforeEach = None
    afterEach = None
    after = None
  }

module Tests =
  let inline test (name: string) (f: Assert -> unit) : Test = {
    name = name
    test = Test f
  }

  let inline testPromise (name: string) (f: Assert -> Promise<unit>) : Test = {
    name = name
    test = AsyncTest(fun assert' -> f assert')
  }

  let inline testAsync (name: string) (f: Assert -> Async<unit>) : Test = {
    name = name
    test = AsyncTest(fun assert' -> f assert' |> Async.StartAsPromise)
  }

  let inline testOnly (name: string) (f: Assert -> unit) : unit =
    QUnit.testOnly(name, f)

  let inline testSkip (name: string) (f: Assert -> unit) : unit =
    QUnit.testSkip(name, f)

  let inline testTodo (name: string) (f: Assert -> unit) : unit =
    QUnit.testTodo(name, f)

  let inline testOnlyAsync (name: string) (f: Assert -> Async<unit>) : unit =
    QUnit.testOnly(name, f >> Async.StartAsPromise)

  let inline testOnlyPromise
    (name: string)
    (f: Assert -> Promise<unit>)
    : unit =
    QUnit.testOnly(name, f)

  let inline testSkipPromise
    (name: string)
    (f: Assert -> Promise<unit>)
    : unit =
    QUnit.testSkip(name, f)

  let inline testSkipAsync (name: string) (f: Assert -> Async<unit>) : unit =
    QUnit.testSkip(name, f >> Async.StartAsPromise)

  let inline testTodoPromise
    (name: string)
    (f: Assert -> Promise<unit>)
    : unit =
    QUnit.testTodo(name, f)

  let inline testTodoAsync (name: string) (f: Assert -> Async<unit>) : unit =
    QUnit.testTodo(name, f >> Async.StartAsPromise)

  let inline testIf
    (name: string)
    (condition: bool)
    (f: Assert -> unit)
    : unit =
    QUnit.testIf(name, condition, f)

  let inline testIfPromise
    (name: string)
    (condition: bool)
    (f: Assert -> Promise<unit>)
    : unit =
    QUnit.testIf(name, condition, f)

  let inline testIfAsync
    (name: string)
    (condition: bool)
    (f: Assert -> Async<unit>)
    : unit =
    QUnit.testIf(name, condition, f >> Async.StartAsPromise)

  let inline testList (name: string) (tests: TestList) : unit =
    QUnit.module'(
      name,
      fun hooks ->
        tests
        |> List.iter(fun (t: Test) ->
          match t.test with
          | Test f -> QUnit.test(t.name, f)
          | AsyncTest f -> QUnit.test(t.name, f))
    )

  let inline testListWithConfig
    (name: string)
    (config: TestConfig)
    (tests: TestList)
    : unit =
    QUnit.module'(
      name,
      fun hooks ->
        config.before |> Option.iter hooks.before
        config.beforeEach |> Option.iter hooks.beforeEach
        config.afterEach |> Option.iter hooks.afterEach
        config.after |> Option.iter hooks.after

        tests
        |> List.iter(fun t ->
          match t.test with
          | Test f -> QUnit.test(t.name, f)
          | AsyncTest f -> QUnit.test(t.name, f))
    )

  let inline testSequenced(tests: TestList) : unit =
    tests
    |> List.iter(fun t ->
      match t.test with
      | Test f -> QUnit.test(t.name, f)
      | AsyncTest f -> QUnit.test(t.name, f))

  let inline run(tests: TestList) : unit =
    tests
    |> List.iter(fun t ->
      match t.test with
      | Test f -> QUnit.test(t.name, f)
      | AsyncTest f -> QUnit.test(t.name, f))

  // Hook helpers
  let inline before(f: Assert -> unit) : TestConfig = {
    before = Some f
    beforeEach = None
    afterEach = None
    after = None
  }

  let inline beforeEach(f: Assert -> unit) : TestConfig = {
    before = None
    beforeEach = Some f
    afterEach = None
    after = None
  }

  let inline afterEach(f: Assert -> unit) : TestConfig = {
    before = None
    beforeEach = None
    afterEach = Some f
    after = None
  }

  let inline after(f: Assert -> unit) : TestConfig = {
    before = None
    beforeEach = None
    afterEach = None
    after = Some f
  }

  // Config combinators
  let inline withBefore (f: Assert -> unit) (config: TestConfig) : TestConfig = {
    config with
        before = Some f
  }

  let inline withBeforeEach
    (f: Assert -> unit)
    (config: TestConfig)
    : TestConfig =
    { config with beforeEach = Some f }

  let inline withAfterEach
    (f: Assert -> unit)
    (config: TestConfig)
    : TestConfig =
    { config with afterEach = Some f }

  let inline withAfter (f: Assert -> unit) (config: TestConfig) : TestConfig = {
    config with
        after = Some f
  }
