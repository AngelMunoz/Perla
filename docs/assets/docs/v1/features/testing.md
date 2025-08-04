# Testing

Perla provides built-in testing capabilities powered by Playwright, supporting multiple browsers and test frameworks. Tests run in real browsers with full ES module support and import maps.

> Perla will automatically try to load test files within the `<Root>/tests` directory.

## Quick Start

```bash
# Run tests once
perla test

# Run tests in watch mode
perla test --watch

# Run tests in specific browsers
perla test --browsers chromium firefox

# Run tests with visible browser windows
perla test --headless false # or perla test --no-headless
```

## Command Line Options

> **_Note_**: These options will override settings in `perla.json`.

```bash
perla test [options]

Options:
  --browsers <browsers>     Browsers to run tests in (chromium, firefox, webkit, chrome, edge)
  --files <patterns>        Test file patterns to include
  --skip <patterns>         Test file patterns to exclude
  --watch                   Run tests in watch mode
  --no-headless            Show browser windows during testing
  --sequential             Run browsers sequentially instead of parallel
```

## Configuration options

```jsonc
{
  "testing": {
    // by default only chromium is listed
    "browsers": ["chromium", "firefox", "webkit", "chrome", "edge"],
    "includes": [
      "**/*.test.js",
      "**/*.spec.js",
      "**/*.Test.fs.js",
      "**/*.Spec.fs.js"
    ],
    "excludes": [],
    "watch": false,
    "headless": true,
    "browserMode": "parallel",
    "testFramework": "qunit",
    // If Fable options are provided, Fable will run before tests
    "fable": {
      "project": "./tests/Tests.fsproj"
    }
  }
}
```

## Test Frameworks

The Test runner provided by Perla supports two particular test frameworks:

- [QUnit](https://qunitjs.com/) (default)
- [Mocha](https://mochajs.org/)

QUnit is the default framework due it's lightweight nature and ease of use, browser native support and 0 dependencies however, we know Mocha is ubiquitous in the JS ecosystem so we provide support for it as well.

By default, Perla will load QUnit or Mocha from a CDN and inject it into the global scope of the browser window. If you want to override the resolution of these dependencies, you can do so by adding them to your project's as dependencies or to the `perla.json#paths` property. Please import both the JS and CSS files if you want to run your tests in a non-headless mode otherwise the JS file is enough.

> **_Note_**: If perla finds the "qunit" or "mocha" key in the resolved import map (`perla.json#paths` + `perla.json#dependencies` merged object),it will skip loading from CDN and use the provided path instead.

example:
Overriding Mocha

```json
{
  "paths": {
    "mocha": "https://unpkg.com/mocha@<version>/mocha.js",
    "mocha/mocha.css": "https://unpkg.com/mocha@<version>/mocha.css"
  }
}
```

overriding QUnit

```json
{
  "paths": {
    "qunit": "https://unpkg.com/qunit@<version>/qunit/qunit.js",
    "qunit/qunit.css": "https://unpkg.com/qunit@<version>/qunit/qunit.css"
  }
}
```

> **_NOTE_**: The version that you point to must load the library in the global scope, we reccommend using unpkg.com if you need a specific version.

### QUnit (Default)

```javascript
// Basic QUnit test
QUnit.module("Math Operations");

QUnit.test("addition works", function (assert) {
  assert.equal(2 + 2, 4, "2 + 2 should equal 4");
});

QUnit.test("async test", async function (assert) {
  const result = await fetch("/api/data");
  assert.ok(result.ok, "API call should succeed");
});
```

> For more information, see the [QUnit documentation](https://qunitjs.com/).

Qunit has some options that we pass directly to the test framework, you can configure them in `perla.json`:

```json
{
  "testing": {
    "testFramework": "qunit",
    "frameworkOptions": {
      "hidepassed": true, // Hide passed tests in the output
      "noglobals": true, // Prevent global leaks
      "notrycatch": true // Disable try/catch for tests
      ... // Other QUnit options
    }
  }
}
```

### Mocha

```javascript
// Basic Mocha test
describe("Math Operations", function () {
  it("should add numbers correctly", function () {
    assert.equal(2 + 2, 4);
  });

  it("should handle async operations", async function () {
    const result = await fetch("/api/data");
    assert.ok(result.ok);
  });
});
```

> For more information, see the [Mocha documentation](https://mochajs.org/).

Mocha can be configured in `perla.json`:

```json
{
  "testing": {
    "testFramework": "mocha",
    "frameworkOptions": {
      "ui": "bdd", // Use BDD interface
      "timeout": 5000 // Set test timeout
    }
  }
}
```

The interface can be set to `bdd`, `tdd` or `qunit` to satisfy the API you may want to use but `bdd` is set as the default.

For mocha we don't provide any assertions library by default, you can use any assertion library you like, such as [Chai](https://www.chaijs.com/). To use Chai, install it as a dependency and import it in your tests:

```bash
perla add chai
```

Then in your test files:

```javascript
import { expect } from "chai";
// Basic Mocha test with Chai
describe("Math Operations", function () {
  it("should add numbers correctly", function () {
    expect(2 + 2).to.equal(4);
  });
  // ...
});
```

## F# Testing with Fable

Perla provides F# testing support through Fable compilation with an Expecto-like API for both QUnit and Mocha frameworks. The API offers familiar F# testing patterns with `test`, `testList`, `testAsync`, and built-in `Expect` assertions.

> Tests can be written with sync/promise/F# async functions.

### QUnit with F#

For QUnit, use the `Perla.Fable.QUnit` module:

```fsharp
// Math.Test.fs
module MathTests

open Perla.Fable.QUnit

let tests = [
  Tests.test "Addition works" (fun assert' ->
    let actual = 2 + 2
    Expect.equal actual 4 "Math should work" assert')

  Tests.test "String operations" (fun assert' ->
    let result = "Hello" + " World"
    Expect.equal result "Hello World" "Strings should concatenate" assert')

  Tests.testAsync "Async operations" (fun assert' -> async {
    let! result = Promise.resolve 42 |> Async.AwaitPromise
    Expect.equal result 42 "Promise should resolve" assert'
  })
]

// Run all tests in this file
Tests.run tests
```

You can also use `Tests.testList` to group tests:

```fsharp
let tests = [
  Tests.test "Simple calculation" (fun assert' ->
    let actual = multiply 6 7
    Expect.equal actual 42 "6 * 7 should equal 42" assert')

  Tests.test "Error handling" (fun assert' ->
    let throws() = failwith "Expected error"
    Expect.throws throws "Should throw an exception" assert')
]

Tests.testList "Calculator Tests" tests
```

### Mocha with F#

For Mocha, choose from BDD, TDD, or QUnit interfaces:

#### BDD Interface

```fsharp
// Calculator.Test.fs
module CalculatorTests

open Perla.Fable.Mocha

let tests = [
  Tests.test "should add numbers" (fun _ ->
    Expect.equal 8 (5 + 3))

  Tests.test "should multiply numbers" (fun _ ->
    Expect.equal 12 (4 * 3))

  Tests.testAsync "should handle async operations" (fun _ -> async {
    let! result = Promise.resolve 42 |> Async.AwaitPromise
    Expect.equal 42 result
  })
]

Tests.run tests
```

You can also use `Tests.testList` to group tests:

```fsharp
let calculatorTests = [
  Tests.test "addition" (fun _ ->
    Expect.equal 4 (2 + 2))

  Tests.test "division" (fun _ ->
    Expect.equal 5.0 (10.0 / 2.0))
]

Tests.testList "Calculator" calculatorTests
```

#### Using BDD Style Directly

```fsharp
open Perla.Fable.Mocha
open Mocha.BDD

describe "Calculator" (fun _ ->
  it "should add numbers" (fun _ ->
    Expect.equal 8 (5 + 3))

  it "should multiply numbers" (fun _ ->
    Expect.equal 12 (4 * 3))
)
```

#### TDD Interface

```fsharp
open Perla.Fable.Mocha
open Mocha.TDD

suite "String utilities" (fun _ ->
  test "should trim whitespace" (fun _ ->
    let result = "  hello  ".Trim()
    Expect.equal "hello" result)

  test "should convert to uppercase" (fun _ ->
    Expect.equal "HELLO" ("hello".ToUpper()))
)
```

#### QUnit Interface (for Mocha)

```fsharp
open Perla.Fable.Mocha
open Mocha.QUnit

suite "API Tests"

test "should fetch data" (fun _ ->
  Expect.isTrue true)
```

### Configuration

Configure Fable compilation in `perla.json`:

```json
{
  "testing": {
    "testFramework": "qunit", // or "mocha"
    "fable": {
      "project": "./tests/Tests.fsproj",
      "outDir": "./tests",
      "sourceMaps": true
    }
  }
}
```

### Some of the available expect functions

**QUnit Expect functions** (require an `Assert` parameter):

Qunit provides its own assertions API that we expose through the `Expect` and the `assert'` parameter:

- `Expect.equal actual expected message assert'` - Strict equality
- `Expect.notEqual actual expected message assert'` - Strict inequality
- `Expect.deepEqual actual expected message assert'` - Deep object comparison
- `Expect.isTrue / Expect.isFalse value message assert'` - Boolean assertions
- `Expect.isOk / Expect.isNotOk value message assert'` - Truthy/falsy checks
- `Expect.throws f message assert'` - Exception testing
- `Expect.closeTo actual expected delta message assert'` - Floating point comparison

**Mocha Expect functions** (no Assert parameter needed):

These are very basic assertions provided by us in the `Expect` module, you can leverge these or simply use an existing assertion library. For Mocha anything that throws an error will work as an assertion, so you can use `throw` to fail a test if you don't want to use the `Expect` module or it is missing a function you need:

- `Expect.equal expected actual` - Equality check
- `Expect.notEqual expected actual` - Inequality check
- `Expect.isTrue / Expect.isFalse actual` - Boolean assertions
- `Expect.throws f` - Exception testing
- `Expect.closeTo expected actual delta` - Floating point comparison
- `Expect.contains substring actual` - String contains check
- `Expect.isEmpty / Expect.isNotEmpty actual` - String emptiness checks

## Watch Mode

Watch mode provides a live dashboard showing:

- **Session & Activity**: Current browser, test duration, active suite/test
- **Test Results**: Pass/fail counts, success rate, progress
- **Recent Tests**: Last 6 tests with status and timing
- **Recent Errors**: Latest test failures with timestamps

```bash
perla test --watch
```

The dashboard updates in real-time as tests run and file changes are detected.

## Import Maps in Tests

Tests have full access to your project's import map and paths. You can import modules using bare specifiers as defined in your `perla.json`:

```javascript
// Import from your dependencies
import { html, render } from "lit";
import { expect } from "chai";

// Import your application code
import { Calculator } from "../src/calculator.js";

QUnit.test("Calculator works with lit", function (assert) {
  const calc = new Calculator();
  const template = html`<div>${calc.add(2, 2)}</div>`;
  assert.ok(template, "Template should render");
});
```

This can be useful for mocking (if that's your thing) dependencies by providing your own resolutions.

## Troubleshooting

### Tests Not Found

- Check your `includes` patterns in `perla.json`
- Ensure test files are in the correct location
- Verify file extensions match the patterns

### Browser Issues

- Run `perla test --no-headless` to see browser errors
- Check browser console for JavaScript errors
- Ensure Playwright browsers are installed

### Performance

- Use `--sequential` for debugging flaky parallel tests
- Reduce browser count for faster feedback
- Use specific file patterns to run subset of tests
