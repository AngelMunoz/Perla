[import map]: /#/content/import-maps
[json schema]: https://github.com/AngelMunoz/Perla/blob/main/perla.schema.json
[json schemas]: https://json-schema.org/

## perla.json and perla.json.importmap

The `perla.json` file es the main configuration file with this file you an control most of the Perla CLI.

The `perla.json.importmap` is the actual [import map] used by your application in both development and production
You can write comments on this file to keep tabs on why are things the way they are.

We offer a [JSON schema] for the `perla.json` file so you can get intellisense in editors like VSCode and any other that supports [JSON Schemas]

All of the options in the `perla.json` file are optional, we have set defaults already for these properties.

> If you set any property in the `perla.json` file you will override the defaults, while in v1 we have made sure to update parts of the schema to tell you if yu're missing something vital, we don't do any kind of _merge_ strategy on objects or arrays, so if you do it you need to provide the whole object. If this is a concern for you please raise an issue to be aware of it.

A full `perla.json` file looks like this:

```json
{
  // we tag each release on github if you're running a particular version of perla you can
  // use the git tag version of the schema
  "$schema": "https://raw.githubusercontent.com/AngelMunoz/Perla/dev/perla.schema.json",
  // Version of the schema used for this configuration file
  "schema-version": "2025-07",
  // The main index file to be processed by perla
  "index": "./index.html",
  // The CDN provider to use for dependencies (jspm, unpkg, jsdelivr)
  "provider": "jspm",
  // Use local packages from node_modules if true
  "useLocalPkgs": false,
  // List of plugins to use (perla-esbuild-plugin is required for JS/CSS/TSX/JSX/TS)
  "plugins": [],
  // Map server paths to local directories
  "mountDirectories": {
    "/src": "./src", // resources under ./src will be available at /src
    "/assets": "./assets", // static assets
    "/": "./sw" // service worker files
  },
  // Enable providing environmental variables at dev time
  "enableEnv": true,
  // URL to serve the env vars for perla
  "envPath": "/env.js",
  // Project dependencies (package name: version)
  "dependencies": {
    "@preact/signals": "1.1.2",
    "lit": "2.0.0"
  },
  // Fable compiler configuration (for F# projects)
  "fable": {
    "project": "./src/App.fsproj", // F# project to compile
    "extension": ".fs.js", // Output extension for compiled files
    "sourceMaps": true, // Enable Fable source maps
    "outDir": "./dist" // Output directory for compiled files
  },
  // Dev server configuration
  "devServer": {
    "host": "localhost", // Host to bind the dev server
    "port": 7331, // Port to listen on
    "useSSL": false, // Enable SSL for local development
    "liveReload": true, // Enable live reload on file changes
    "proxy": {
      "/api/{**catch-all}": "http://localhost:5000", // Proxy API requests
      "/ws": "http://localhost:8080/sockets" // Proxy WebSocket requests
    }
  },
  // esbuild configuration
  "esbuild": {
    "esBuildPath": "/path/to/esbuild", // Absolute path to esbuild executable
    "version": "0.12.28", // esbuild version
    "ecmaVersion": "es2020", // Target ECMAScript version
    "minify": true, // Enable minification
    "injects": ["./LICENSE.js"], // Files to inject at build time
    "externals": ["lit"], // Libraries to exclude from the bundle
    "fileLoaders": {
      ".png": "file",
      ".woff": "file",
      ".woff2": "file",
      ".svg": "file"
    },
    "jsxAutomatic": true, // Enable automatic JSX transform
    "jsxImportSource": "preact" // Source for JSX imports
  },
  // Production build configuration
  "build": {
    "outDir": "./dist", // Output directory for build
    "includes": ["**/**/*.html", "./assets/**/*.png"], // Files to include in build output
    "excludes": ["**/*.exclude-me.*"], // Files to exclude from build output
    "emitEnvFile": true // Emit environment file in build output
  },
  // Testing configuration (powered by Playwright)
  "testing": {
    "browsers": ["chromium", "webkit"], // Browsers to run test suites with
    "includes": ["**/featureA/*.spec.js", "**/*.test.js"], // Test file patterns to include
    "excludes": ["**/feature-b/*.spec.js", "**/*.e2e.js"], // Test file patterns to exclude
    "watch": false, // Run tests in watch mode
    "headless": true, // Run browsers in headless mode
    "browserMode": "parallel" // Run test suites in parallel across browsers
  },

  // Deprecated properties (move these to the bottom of your config if you still use them)
  // These are kept for backward compatibility and may be removed in future versions
  "runConfiguration": "production", // DEPRECATED: Use for legacy mode selection
  "devDependencies": [
    // DEPRECATED: Use dependencies for all packages
    { "name": "rxjs-spy", "version": "8.0.2" },
    { "name": "@esm-bundle/chai", "version": "4.3.4" }
  ],
  "packages": {
    // DEPRECATED: Use dependencies instead
    "lit": "https://cdn.skypack.dev/lit"
  }
}
```

Please keep in mind that most of the time you need at most 4-5 properties and a couple of nodes. The less you have to configure in your app, the better. If you think we could simplify things even more, please let us know!
