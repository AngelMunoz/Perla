// @ts-ignore
import { html } from "lit";

export function MyMenu({ menuItems = [] }) {
  return html`
    <nav
      style="display: flex; padding: 0.5rem; background-color: #333; color: white; font-family: Arial, sans-serif;"
    >
      ${menuItems.map(
        (item) =>
          html`<a
            href="${item.href}"
            style="color: white; text-decoration: none; margin: 0 1rem;"
            >${item.label}</a
          >`
      )}
    </nav>
  `;
}
