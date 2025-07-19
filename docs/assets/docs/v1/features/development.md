[asp.net]: https://dotnet.microsoft.com/apps/aspnet

> **_NOTE_**: This documentation is still being updated to reflect changes for V1, the contents may be outdated while this notice is still present

# Development Server

Perla is built using [asp.net] making it a performant, solid and battle tested development server.

by default Perla uses these options if you don't specify them in the `perla.json` > `devServer` object

- autoStart - true

  This means that the saturn server should start as soon as the `perla serve` command is entered.

- port - 7331
- host - localhost
- mountDirectories

  The mount directories object provides a way for Perla to know which directories will be used to provide content and what will be copied into the final dev build

  ```json
  // mount the ./src directory on the /src url path
  {
    "mountDirectories": {
      "/src": "./src",
      "/node_modules": "./node_modules"
    }
  }
  ```

  for example: you could provide an "_assets_" directory to mount all of the images or other kinds of files in your project and serve them under "_/assets_" url.

  ```json
  { "/src": "./src", "/assets": "./assets" }
  ```

## Environment Variable Support

Perla supports environment variables at dev time prefixed with `PERLA_`.

As an example let's think about the following environment variables

```bash
PERLA_clientToken=abcdefg1234557
PERLA_API_KEY=12334566abcdefg
```

Perla will use `enableEnv` and `envPath` nodes from the `devServer` options and provide a javascript file with those environment variables, something like

```js
export const clientToken = "abcdefg1234557";
export const API_KEY = "12334566abcdefg";
```

so you can do the following in your code

```js
import { clientToken, API_KEY } from "/env.js";
```

or in Fable Projects

```fsharp
open Fable.Core
open Fable.Core.JsInterop

let API_KEY = import "API_KEY" "/env.js"
// or the named import option
let clientToken = importMember "/env.js"
```

at build time we don't emit anything for security reasons

> **NOTE**: If you're using TS/JSX/TSX then you might want to enable `"preserveValueImports": true` in your tsconfig.json if for some reason the import is not working at dev time
