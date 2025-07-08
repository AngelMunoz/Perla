import "./index.css";
//@ts-ignore
import { createApp, h } from "vue";

import { App } from "./app.js";

const app = createApp({
  render() {
    return <App />;
  },
});
app.mount("#app");
