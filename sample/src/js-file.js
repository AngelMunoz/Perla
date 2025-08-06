// @ts-ignore
import { render, html } from "lit";
import { SamplePage } from "./nested1/sample-page.js";

function Sample() {
  return html`<h1 style="color: var(--primary-color)">Hello world!</h1>`;
}

export function renderLit() {
  const litApp = document.querySelector("#lit-app");
  const litApp2 = document.querySelector("#lit-app-2");
  render(Sample(), litApp);
  render(SamplePage(), litApp2);
}
