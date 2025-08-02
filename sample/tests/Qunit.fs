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
  static member test(name: string, callback: TestCallback) : unit = jsNative

  [<Emit("QUnit.test($0, $1)")>]
  static member test(name: string, callback: AsyncTestCallback) : unit =
    jsNative

  // Test variants
  [<Emit("QUnit.test.only($0, $1)")>]
  static member testOnly(name: string, callback: TestCallback) : unit = jsNative

  [<Emit("QUnit.test.skip($0, $1)")>]
  static member testSkip(name: string, ?callback: TestCallback) : unit =
    jsNative

  [<Emit("QUnit.test.todo($0, $1)")>]
  static member testTodo(name: string, ?callback: TestCallback) : unit =
    jsNative

  [<Emit("QUnit.test.if($0, $1, $2)")>]
  static member testIf
    (name: string, condition: bool, callback: TestCallback)
    : unit =
    jsNative

  // Main module methods
  [<Emit("QUnit.module($0)")>]
  static member module'(name: string) : unit = jsNative

  [<Emit("QUnit.module($0, $1)")>]
  static member module'(name: string, scope: HookCallback) : unit = jsNative

  [<Emit("QUnit.module($0, $1)")>]
  static member module'(name: string, options: ModuleOptions) : unit = jsNative

  [<Emit("QUnit.module($0, $1, $2)")>]
  static member module'
    (name: string, options: ModuleOptions, scope: HookCallback)
    : unit =
    jsNative

  // Module variants
  [<Emit("QUnit.module.only($0, $1)")>]
  static member moduleOnly(name: string, scope: HookCallback) : unit = jsNative

  [<Emit("QUnit.module.skip($0, $1)")>]
  static member moduleSkip(name: string, scope: HookCallback) : unit = jsNative

  [<Emit("QUnit.module.todo($0, $1)")>]
  static member moduleTodo(name: string, scope: HookCallback) : unit = jsNative

  [<Emit("QUnit.module.if($0, $1, $2)")>]
  static member moduleIf
    (name: string, condition: bool, scope: HookCallback)
    : unit =
    jsNative

  // Control
  [<Emit("QUnit.start()")>]
  static member start() : unit = jsNative


module Expect =
  let equal (actual: 'T) (expected: 'T) (message: string) (assert': Assert) =
    assert'.strictEqual(actual, expected, message)

  let notEqual (actual: 'T) (expected: 'T) (message: string) (assert': Assert) =
    assert'.notStrictEqual(actual, expected, message)

  let deepEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.deepEqual(actual, expected, message)

  let notDeepEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notDeepEqual(actual, expected, message)

  let looseEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.equal(actual, expected, message)

  let notLooseEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notEqual(actual, expected, message)

  let isTrue (value: bool) (message: string) (assert': Assert) =
    assert'.``true``(value, message)

  let isFalse (value: bool) (message: string) (assert': Assert) =
    assert'.``false``(value, message)

  let isOk (value: 'T) (message: string) (assert': Assert) =
    assert'.ok(value, message)

  let isNotOk (value: 'T) (message: string) (assert': Assert) =
    assert'.notOk(value, message)

  let propEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.propEqual(actual, expected, message)

  let notPropEqual
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notPropEqual(actual, expected, message)

  let propContains
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.propContains(actual, expected, message)

  let notPropContains
    (actual: 'T)
    (expected: 'T)
    (message: string)
    (assert': Assert)
    =
    assert'.notPropContains(actual, expected, message)

  let closeTo
    (actual: float)
    (expected: float)
    (delta: float)
    (message: string)
    (assert': Assert)
    =
    assert'.closeTo(actual, expected, delta, message)

  let throws (f: unit -> unit) (message: string) (assert': Assert) =
    assert'.throws(f, message = message)

  let throwsWithMatcher
    (f: unit -> unit)
    (matcher: ExpectedMatcher)
    (message: string)
    (assert': Assert)
    =
    assert'.throws(f, matcher, message)

  let rejects (promise: Promise<'T>) (message: string) (assert': Assert) =
    assert'.rejects(promise, message = message)

  let rejectsWithMatcher
    (promise: Promise<'T>)
    (matcher: ExpectedMatcher)
    (message: string)
    (assert': Assert)
    =
    assert'.rejects(promise, matcher, message)

  let step (value: string) (assert': Assert) = assert'.step(value)

  let verifySteps (steps: string[]) (message: string) (assert': Assert) =
    assert'.verifySteps(steps, message)

  let expectCount (count: int) (assert': Assert) = assert'.expect(count)

  let timeout (duration: int) (assert': Assert) = assert'.timeout(duration)

  let asyncTimeout (count: int) (assert': Assert) = assert'.async(count = count)

  let async(assert': Assert) = assert'.async()

type Test = { name: string; test: Assert -> unit }

type TestList = Test list

type TestConfig = {
  before: (Assert -> unit) option
  beforeEach: (Assert -> unit) option
  afterEach: (Assert -> unit) option
  after: (Assert -> unit) option
}

module Tests =
  let test (name: string) (f: Assert -> unit) : Test = { name = name; test = f }

  let testAsync (name: string) (f: Assert -> Promise<unit>) : Test = {
    name = name
    test = fun assert' -> f assert' |> ignore
  }

  let testOnly (name: string) (f: Assert -> unit) : unit =
    QUnit.testOnly(name, f)

  let testSkip (name: string) (f: Assert -> unit) : unit =
    QUnit.testSkip(name, f)

  let testTodo (name: string) (f: Assert -> unit) : unit =
    QUnit.testTodo(name, f)

  let testIf (name: string) (condition: bool) (f: Assert -> unit) : unit =
    QUnit.testIf(name, condition, f)

  let testList (name: string) (tests: TestList) : unit =
    QUnit.module'(name, fun hooks ->
      tests |> List.iter(fun t -> QUnit.test(t.name, t.test))
    )

  let testListWithConfig (name: string) (config: TestConfig) (tests: TestList) : unit =
    QUnit.module'(name, fun hooks ->
      config.before |> Option.iter hooks.before
      config.beforeEach |> Option.iter hooks.beforeEach
      config.afterEach |> Option.iter hooks.afterEach
      config.after |> Option.iter hooks.after
      tests |> List.iter(fun t -> QUnit.test(t.name, t.test))
    )

  let testSequenced (tests: TestList) : unit =
    tests |> List.iter(fun t -> QUnit.test(t.name, t.test))

  let run (tests: TestList) : unit =
    tests |> List.iter(fun t -> QUnit.test(t.name, t.test))

  let runTests () = QUnit.start()

  // Hook helpers
  let before (f: Assert -> unit) : TestConfig =
    { before = Some f; beforeEach = None; afterEach = None; after = None }

  let beforeEach (f: Assert -> unit) : TestConfig =
    { before = None; beforeEach = Some f; afterEach = None; after = None }

  let afterEach (f: Assert -> unit) : TestConfig =
    { before = None; beforeEach = None; afterEach = Some f; after = None }

  let after (f: Assert -> unit) : TestConfig =
    { before = None; beforeEach = None; afterEach = None; after = Some f }

  // Config combinators
  let withBefore (f: Assert -> unit) (config: TestConfig) : TestConfig =
    { config with before = Some f }

  let withBeforeEach (f: Assert -> unit) (config: TestConfig) : TestConfig =
    { config with beforeEach = Some f }

  let withAfterEach (f: Assert -> unit) (config: TestConfig) : TestConfig =
    { config with afterEach = Some f }

  let withAfter (f: Assert -> unit) (config: TestConfig) : TestConfig =
    { config with after = Some f }

  let emptyConfig : TestConfig =
    { before = None; beforeEach = None; afterEach = None; after = None }
