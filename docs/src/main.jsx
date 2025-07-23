import "./index.css?js";
import { render } from "preact";
import { App } from "./App.js";

import { setBasePath } from "@shoelace-style/shoelace/dist/utilities/base-path.js";

setBasePath(
  "https://cdn.jsdelivr.net/npm/@shoelace-style/shoelace@2.20.1/cdn/"
);

async function main() {
  await Promise.allSettled([import("@shoelace-style/shoelace")]).then((args) =>
    console.debug("imported dependencies")
  );
  const el = document.querySelector("#root");
  if (!el) {
    throw new Error("Root element not found");
  }
  render(<App />, el);
}

main();
