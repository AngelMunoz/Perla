import { createMessage, add } from "../src/main.js";

QUnit.module("Blocal Module Tests");

QUnit.test("createMessage formats message correctly", (assert) => {
  const result = createMessage("Hello", "World");
  assert.equal(result, "Hello: World", "Should format message correctly");

  const result2 = createMessage("Perla", "Offline Template");
  assert.equal(
    result2,
    "Perla: Offline Template",
    "Should handle different inputs"
  );
});

QUnit.test("add function works correctly", (assert) => {
  assert.equal(add(2, 3), 5, "Should add two positive numbers");
  assert.equal(add(-1, 1), 0, "Should handle negative numbers");
  assert.equal(add(0, 0), 0, "Should handle zero");
});
