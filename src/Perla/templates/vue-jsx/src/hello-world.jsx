//@ts-ignore
import { h } from "vue";

import "./hello-world.css";

/**
 *
 * @param {{ msg?: string }} props
 * @returns
 */
export function HelloWorld(props) {
  return (
    <div class="hello-world">
      <h1>{props.msg}</h1>
      <p>
        For a guide and recipes on how to configure / customize this project,
        <br />
        check out the
        <a href="https://perla-docs.web.app" target="_blank" rel="noopener">
          Perla documentation
        </a>
        .
      </p>
      <h3>Essential Links</h3>
      <ul>
        <li>
          <a href="https://vuejs.org" target="_blank" rel="noopener">
            Core Docs
          </a>
        </li>
        <li>
          <a href="https://forum.vuejs.org" target="_blank" rel="noopener">
            Forum
          </a>
        </li>
        <li>
          <a href="https://chat.vuejs.org" target="_blank" rel="noopener">
            Community Chat
          </a>
        </li>
        <li>
          <a href="https://twitter.com/vuejs" target="_blank" rel="noopener">
            Twitter
          </a>
        </li>
        <li>
          <a href="https://news.vuejs.org" target="_blank" rel="noopener">
            News
          </a>
        </li>
      </ul>
      <h3>Ecosystem</h3>
      <ul>
        <li>
          <a href="https://router.vuejs.org" target="_blank" rel="noopener">
            vue-router
          </a>
        </li>
        <li>
          <a href="https://vuex.vuejs.org" target="_blank" rel="noopener">
            vuex
          </a>
        </li>
        <li>
          <a
            href="https://github.com/vuejs/vue-devtools#vue-devtools"
            target="_blank"
            rel="noopener"
          >
            vue-devtools
          </a>
        </li>
        <li>
          <a
            href="https://github.com/vuejs/awesome-vue"
            target="_blank"
            rel="noopener"
          >
            awesome-vue
          </a>
        </li>
      </ul>
    </div>
  );
}
HelloWorld.props = ["msg"];
