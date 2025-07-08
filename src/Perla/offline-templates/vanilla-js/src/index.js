import "./index.css";

//@ts-ignore
import todos from "./todos.json?module";

/**
 *
 * @param {string} name
 * @param {Map<string, string>} attributes
 * @param {Node[]} children
 */
function el(name, attributes = new Map(), children = []) {
  const el = document.createElement(name);
  for (const [attribute, value] of attributes) {
    el.setAttribute(attribute, value);
  }

  for (const child of children) {
    el.appendChild(child);
  }
  return el;
}

/**
 * @param {string} text
 */
function text(text) {
  return document.createTextNode(text);
}

/**
 *
 * @param {Todo} item
 */
function todoItem(item) {
  return el("li", new Map([["class", "todo-list__item"]]), [
    text(`${item.title} - ${item.done ? "done" : "pending"}`),
  ]);
}

/**
 *
 * @param {Todo[]} items
 */
function todoList(items) {
  return el("ul", new Map([["class", "todo-list"]]), items.map(todoItem));
}

const styles = new CSSStyleSheet();

styles.replaceSync(`
:host {
  --color: rebeccapurple;
  --border: 2px solid var(--color);
}

.main {
    font-weight: thin;
}

.todo-list {
  border: var(--border);
  display: flex;
  justify-content: space-evenly;
}

.todo-list__item {
  --color: tomato;
  border: 1px groove var(--color);
  margin: 1em;
  padding: 0.5em;
}
`);

class MyApp extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: "open" });
    this.shadowRoot.adoptedStyleSheets = [styles];
    this.shadowRoot.appendChild(
      el(
        "main",
        new Map([
          ["data-name", "main-element"],
          ["class", "main"],
        ]),
        [
          el("h1", undefined, [text("Welcome to the vanilla JS sample :)")]),
          todoList(todos),
        ]
      )
    );
  }
}

customElements.define("my-app", MyApp);
