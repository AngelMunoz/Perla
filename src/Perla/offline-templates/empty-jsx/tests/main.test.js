import { multiply, createGreeting } from "../src/main.jsx";

QUnit.module("Empty JSX Module Tests");

QUnit.test("multiply function works correctly", (assert) => {
  assert.equal(multiply(3, 4), 12, "Should multiply two positive numbers");
  assert.equal(multiply(-2, 5), -10, "Should handle negative numbers");
  assert.equal(multiply(0, 100), 0, "Should handle zero");
});

QUnit.test("createGreeting creates correct greeting", (assert) => {
  assert.equal(
    createGreeting("World"),
    "Hello, World!",
    "Should create greeting with name"
  );
  assert.equal(
    createGreeting("Perla"),
    "Hello, Perla!",
    "Should work with different names"
  );
  assert.equal(createGreeting(""), "Hello, !", "Should handle empty string");
});
