[typescript]: #v1/docs/features/transpilation
[perla samples]: https://github.com/AngelMunoz/perla-templates
[react]: https://reactjs.org/

# JSX/TSX

JSX is an XML like dialect of javascript created by [React]. As with [typescript], we use esbuild to transpile these files on the fly. Perla provides two main ways to configure JSX/TSX support:

1. Manual JSX imports via `injects` (described below)
2. Automatic JSX runtime imports via the `jsxAutomatic` and `jsxImportSource` options

## JSX Configuration Options

Perla provides two primary ways to configure JSX support:

### 1. Automatic JSX Runtime (Recommended)

In the `esbuild` section of your `perla.json`, you can enable automatic JSX runtime imports:

```json
{
  "esbuild": {
    "jsxAutomatic": true,
    "jsxImportSource": "preact" // or "react", etc.
  }
}
```

With these options:

- `jsxAutomatic`: When set to `true`, esbuild will automatically inject the necessary JSX runtime imports
- `jsxImportSource`: Specifies the package from which to import JSX runtime functions

This approach eliminates the need for manual JSX runtime imports in each file.

### 2. Manual Imports via Injects

Alternatively, you can manually specify JSX runtime imports using the `injects` array in the `esbuild` configuration:

## React

### Using Automatic JSX Runtime (Recommended)

For React projects, you can use the automatic JSX runtime configuration:

```json
{
  "$schema": "https://raw.githubusercontent.com/AngelMunoz/Perla/main/perla.schema.json",
  "esbuild": {
    "jsxAutomatic": true
    // No jsxImportSource needed for React as it's the default
  }
}
```

## TypeScript Configuration with tsconfig.json/jsconfig.json

Perla also supports TypeScript's JSX configuration through a `tsconfig.json` or `jsconfig.json` file at the root of your project. This provides additional TypeScript-specific JSX options that complement Perla's esbuild configuration.

A typical `tsconfig.json` for a React project might look like this:

```json
{
  "compilerOptions": {
    "target": "ESNext",
    "module": "ESNext",
    "moduleResolution": "node",
    "jsx": "react-jsx",
    "jsxImportSource": "react",
    "strict": true
  },
  "include": ["src/**/*"]
}
```

For Preact:

```json
{
  "compilerOptions": {
    "target": "ESNext",
    "module": "ESNext",
    "moduleResolution": "node",
    "jsx": "react-jsx",
    "jsxImportSource": "preact",
    "strict": true
  },
  "include": ["src/**/*"]
}
```

When both the `tsconfig.json` and Perla's esbuild configurations specify JSX options, Perla will use the configuration from both sources, with Perla's settings taking precedence when there are conflicts.

### Using Manual Imports

Alternatively, create a `react-shim.js` file next to your `perla.json` with the following content:

```javascript
import * as React from "react";
export { React };
```

Then in your `perla.json` file, add the file to the `injects` array in the `esbuild` object:

```json
{
  "$schema": "https://raw.githubusercontent.com/AngelMunoz/Perla/main/perla.schema.json",
  "index": "./index.html",
  "esbuild": {
    "injects": ["./react-shim.js"]
  }
}
```
