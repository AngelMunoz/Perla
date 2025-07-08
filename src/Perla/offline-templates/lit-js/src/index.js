import "./index.css";
//@ts-ignore
import { LitElement, html } from "lit";
//@ts-ignore
import todos from "./todos.json?module";

/**
 * Renders a simple todo on the screen
 * @param {Todo} todo
 */
const todoTpl = (todo) =>
  html`<li>${todo.title} - ${todo.done ? "Done" : "Pending"}</li>`;

class MyApp extends LitElement {
  constructor() {
    super();
    this.mouseCtrl = new MouseController(this);
  }

  render() {
    return html`
      <article>
        <h1>Hello, World!</h1>
        <p>Your mouse is at: (${this.mouseCtrl.x}, ${this.mouseCtrl.y})</p>
        <ul>
          ${todos.map(todoTpl)}
        </ul>
      </article>
    `;
  }
}

export class MouseController {
  /**
   * @param {LitElement} host
   */
  constructor(host) {
    this.host = host;
    host.addController(this);
    this.x = 0;
    this.y = 0;
  }

  _updatePosition =
    /**
     *
     * @param {MouseEvent} event
     */
    (event) => {
      this.x = event.x;
      this.y = event.y;
      this.host.requestUpdate();
    };

  hostConnected() {
    window.addEventListener("mousemove", this._updatePosition);
  }

  hostDisconnected() {
    window.removeEventListener("mousemove", this._updatePosition);
  }
}

//@ts-ignore
customElements.define("my-app", MyApp);
