import { setMessage } from "../src/main.js";

QUnit.module("Main Module Tests");

QUnit.test("setMessage sets correct HTML content", (assert) => {
  const fixture = document.querySelector("#qunit-fixture");

  setMessage(fixture);
  assert.equal(
    fixture.innerHTML,
    `
  <h1>Perla Offline Template</h1>
  <p>This is a basic offline template for Perla.</p>
  <p>It is designed to work without any server-side processing.</p>
  <p>Feel free to modify it as per your requirements.</p>
  `
  );
});
