[typescript]: #/v0/docs/features/transpilation
[perla samples]: https://github.com/AngelMunoz/perla-templates
[react]: https://reactjs.org/

# JSX/TSX

JSX is an XML like dialect of javascript created by [React] as with [typescript] we use esbuild to transpile these files on the fly there's nothing in particular needed to support JSX/TSX besides two things:

- jsx-factory
- jsx-fragment

These functions are used to set te correct names of the compiled functions which will be called at the runtime.

At the moment we offer no out-of the box support for react/preact or similar cases we instead offer a way for you to inject a particular script where you an define these.

> If there is enough demand for it we can bake in this behavior, so feel free to let us know.

## React

In your JSX/TSX React projects you should create a `react-shim.js` file next to your `perla.jsonc` with the following content:

```javascript
import * as React from "react";
export { React };
```

then inside your `perla.jsonc` file you need to add the `injects` array property to the `build` object:

```json
{
  "$schema": "https://raw.githubusercontent.com/AngelMunoz/Perla/main/perla.schema.json",
  "index": "./index.html",
  "devServer": {
    // dev server options
  },
  "build": {
    // other build options
    // This property
    "injects": [
      // you can chose where to put your shim as well
      // but we recommend it to be next to your perla.jsonc file
      "./react-shim.js"
    ]
    // other build options
  },
  "packages": {
    // dependency list
  }
}
```

## Preact and others

In the case of JSX/TSX Preact or other similar libraries' projects, you should create a `jsx-shim.js` file next to your `perla.jsonc` with the following content:

```javascript
import { h, Fragment } from "preact";
export { h, Fragment };
```

Of course if you're using something other than `preact` do the correct imports

then inside your `perla.jsonc` file you need to add the `injects` array property to the `build` object:

```json
{
  "$schema": "https://raw.githubusercontent.com/AngelMunoz/Perla/main/perla.schema.json",
  "index": "./index.html",
  "devServer": {
    // dev server options
  },
  "build": {
    // other build options
    // This property
    "injects": [
      // you can chose where to put your shim as well
      // but we recommend it to be next to your perla.jsonc file
      "./jsx-shim.js"
    ]
    // other build options
  },
  "packages": {
    // dependency list
  }
}
```
