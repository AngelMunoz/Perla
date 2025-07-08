import "./index.css";
//@ts-ignore
import { createSignal, onCleanup } from "solid-js";
//@ts-ignore
import { render, For } from "solid-js/web";
//@ts-ignore
import html from "solid-js/html";
//@ts-ignore
import todos from "./todos.json?module";

/**
 *
 * @param {{todo: Todo}} todo
 */
function Todo({ todo }) {
  return html`<li>${todo.title} - ${todo.done ? "done" : "pending"}</li>`;
}

/**
 *
 * @param {{ items: Todo[] }} items
 */
function TodoList({ items }) {
  const fallback = html`<li>Loading...</li>`;
  const content = (/** @type {Todo} */ todo) => html`<${Todo} todo=${todo} />`;
  return html`
    <ul>
      <${For} each=${items} fallback=${fallback}>${content}<//>
    </ul>
  `;
}

const App = () => {
  const [count, setCount] = createSignal(0);
  const timer = setInterval(() => setCount(count() + 1), 1000);

  onCleanup(() => clearInterval(timer));

  return html`
    <h1>Hello <b>solid.js!</b> - ${count}</h1>
    <${TodoList} items=${todos} />
  `;
};
render(() => html`<${App} />`, document.querySelector("#app"));
