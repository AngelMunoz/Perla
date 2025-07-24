[import map]: #/content/import-maps
[json schema]: https://github.com/AngelMunoz/Perla/blob/main/perla.schema.json
[json schemas]: https://json-schema.org/

## perla.jsonc and perla.jsonc.importmap

The `perla.jsonc` file es the main configuration file with this file you an control most of the Perla CLI.

The `perla.jsonc.importmap` is the actual [import map] used by your application in both development and production
You can write comments on this file to keep tabs on why are things the way they are.

We offer a [JSON schema] for the `perla.jsonc` file so you can get autocompletition in editors like VSCode and any other that supports [JSON Schemas]

Most of the options in the `perla.jsonc` file are optional, we have set defaults already for these properties.

> If you set any property in the `perla.jsonc` file you will override the defaults, we don't do any kind of _merge_ strategy on objects or arrays, so if you do it you need to provide the whole object. If this is a concern for you please raise an issue to be aware of it.

A full `perla.jsonc` file looks like this:

```json
{
  // we tag each release on github if you're running a particular version of perla you can
  // use the git tag version of the schema
  "$schema": "https://raw.githubusercontent.com/AngelMunoz/Perla/main/perla.schema.json",
  "index": "./index.html",
  // development server options
  "devServer": {
    // auto start the dev server when the perla serve command is run
    "autoStart": true,
    "host": "127.0.0.1",
    // enable reload on change for sources
    "liveReload": true,
    "port": 7331,
    "useSSL": false,
    // mount local directories on specific URL paths
    "mountDirectories": {
      // resources under ./src will be available in /src URL
      // e.g. ./src/index.js -> /src/index.js
      "./src": "/src",
      // e.g. ./assets/docs/index.md -> /assers/docs/indexmd
      "./assets": "/assets",
      // e.g. anything under ./root-files will be available at "/" in the dev server
      "./root-files": ""
    },
    // modify watch behavior
    "watchConfig": {
      "directories": ["./src"],
      // extensions to monitor under the above directores
      "extensions": ["*.js", "*.css", "*.ts", "*.tsx", "*.jsx", "*.json"]
    }
  },
  // Enable Fable compilation, You Must Have the fable dotnet tool installed
  // for this to work propperly
  "fable": {
    // start together with the dev server
    "autoStart": true,
    // output extension of the fable files
    "extension": ".fs.js",
    // where to output these files
    "outDir": "./dist",
    // F# project to compile
    "project": "./src/App.fsproj"
  },
  "build": {
    "bundle": true,
    // use a custom esbuild binary
    "esBuildPath": "./path/to/esbuild",
    // use a custom esbuild version
    "esbuildVersion": "0.12.28",
    "format": "esm",
    // JSX specific options
    "jsxFactory": "h",
    "jsxFragment": "Fragment",
    "minify": true,
    "outDir": "./dist",
    "target": "es2015",
    // copy static files as is
    "copyPaths": {
      // list of files and extensions to exclude
      // when copying sources to the bundled output
      "excludes": [
        "index.html",
        ".fsproj",
        ".fable",
        "fable_modules",
        "bin",
        "obj",
        ".fs",
        ".js",
        ".css",
        ".ts",
        ".jsx",
        ".tsx"
      ],
      // ensure a particular resource is copied even if it's
      // under a non-copy'able location
      "includes": [
        "./src/sample.png",
        // when building you can re-target where your sources
        // can be copied at, this together with mount directories
        // can give you a flexible way to mount/copy non-standard files
        // or files that need to have a specific address/location
        "./root-files/manifest.webmanifest -> ./manifest.webmanifest"
      ]
    },
    // ensure esbuild ignores a particular dependency
    "externals": ["my-undeclared-dependency", "@undeclared/dep"],
    // use a specific esbuild loader for a particular extension
    "fileLoaders": {
      ".png": "file",
      ".woff": "file",
      ".woff2": "file",
      ".svg": "file"
    },
    // inject text, code or any other text based source to
    // compiled output
    "injects": ["./preact-shim.js"]
  },
  // list of the packages you install and their sources
  "packages": {
    "lit": "https://cdn.skypack.dev/lit"
  }
}
```
