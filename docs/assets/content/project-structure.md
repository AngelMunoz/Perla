[perla samples]: https://github.com/AngelMunoz/perla-templates

# Project Structure

Once you have Perla [up and running](#content/install), you can pick a template from the [Perla Samples] and simply type `perla serve` for your dev server to start working.

Perla has a set of defaults which allow you to keep a very basic structure, in reality you just need an `index.html` and an empty `perla.json` file but the most ideal structure would be something like this

```sh
perla.json
index.html
src/
  index.js
```

With that, Perla will know what should be served and where

- `perla.json` isn't actually required to be there for very simple functionality, for most cases all of the settings are optional as we provide defaults for every section of the file but for clarity we'll use the following example

```json
{
  "$schema": "https://raw.githubusercontent.com/AngelMunoz/Perla/dev/perla.schema.json",
  "schema-version": "2025-07",
  "fable": {
    "project": "./src/App.fsproj",
    "sourceMaps": true
  },
  "dependencies": {
    "date-fns": "2.29.3",
    "react": "19.1.0",
    "react-dom": "19.1.0",
    "react-dom/client": "19.1.0"
  }
}
```

> **_NOTE_**: The Json Schema version should always point to the closest released version you're currenly using to ensure you get the correct intellisense by your editor. In the case of previews you can point to the latest pre-release tag like:
>
> - `AngelMunoz/Perla/v1.0.0-beta-033/perla.schema.json`
>
> or to the dev branch:
>
> - `AngelMunoz/Perla/dev/perla.schema.json`

- `index.html` must have at least these fields

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="./src/favicon.ico" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Perla App</title>
    <!-- This shim is required for import maps to work propperly-->
    <script
      async
      src="https://ga.jspm.io/npm:es-module-shims@2.6.1/dist/es-module-shims.js"
      crossorigin="anonymous"
    ></script>
  </head>
  <body>
    <div id="root"></div>
    <!-- data-entry-point is very important when building for production
         this attribute tells perla that it must process the file when building
         you can include multiple `data-entry-pont` scripts -->
    <script data-entry-point type="module" src="./src/index.js"></script>
  </body>
</html>
```

- `index.js` a non-empty javascript module e.g.

```js
function greeting() {
  console.log("Hello, there!");
}
```
