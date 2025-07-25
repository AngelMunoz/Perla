[css module scripts]: https://web.dev/css-module-scripts/
[lit]: https://lit.dev

### CSS Modules

CSS Module Scripts are very useful for Web component libraries like [lit] or any other library that produces custom elements with shadow roots, here's an example of the usage:

```javascript
import sheet from "./styles.css" with { type: "css" };
document.adoptedStyleSheets = [sheet];
shadowRoot.adoptedStyleSheets = [sheet];
```

# JSON

We support using JSON files as JS objects in the following ways:

the way to use import with is just like the CSS counterpart:

```javascript
import json from "./json-file.json" with { type: "json" };
// you can now "dot" into any property from the json file;
console.log(json.myProp);
```

## Importing via esbuild supported imports (not recommended)

While not especially recommended, it is also possible to import both CSS and JSON files using esbuild-style imports by appending the `?js` query parameter. This signals that you want the file to be imported as a JavaScript module, similar to how esbuild handles these imports:

```javascript
import "my-cssfile.css?js";
import json from "./my-file.json?js";
```

> **Note:** While this may work without any plugins enabled while serving your app, this will likely break your final build if you don't enable the esbuild plugin as this relies on esbuild features.
