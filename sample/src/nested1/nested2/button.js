// @ts-ignore
import { html } from "lit";

export function MyButton({
  label = "Click Me",
  onClick = () => {},
  className = "",
  disabled = false,
}) {
  return html`
    <button @click="${onClick}" class="${className}" ?disabled="${disabled}">
      ${label}
    </button>
  `;
}
