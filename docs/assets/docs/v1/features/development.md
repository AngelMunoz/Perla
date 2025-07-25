[suave.io]: https://suave.io

> **_NOTE_**: This documentation is still being updated to reflect changes for V1, the contents may be outdated while this notice is still present

# Development Server

When you call `perla serve`, Perla will start a development server for your project.
This server has a few phases which are more-less like the following

- Initialization
  - If available (`perla.json` has a `fable` section), the Fable compiler will be invoked in watch mode and will wait for the first compilation to be finished.
  - Load Plugins, the built-in plugins will be loaded if the heuristics allow it, and user provided plutgins will be lodaded as well.
- Mounting
  - Given the `mountDirectories` configuration, Perla will start loading resources specified in the `mountDirectories` section of the `perla.json` file.
  - For each file loaded from the `mountDirectories`, Perla will move it through the compilation pipeline
  - A list of file watchers will be created based on the `mountDirectories` configuration, so that any changes to the files will trigger a pass through the compilation pipeline.
  - Store the results of the compilation in a cache, so that subsequent requests for the same file can be served faster.
- Serving

Building your app has a similar process but streamlined to only build the files that are needed for the final output.

## Live Reloading

Perla supports live reloading out of the box, meaning that any changes you make to your source files will automatically trigger a rebuild and refresh the browser.

In the case of CSS, the changes will be applied without a full page reload, allowing for a smoother development experience.

## Environment Variable Support

Perla supports environment variables at dev time prefixed with `PERLA_`.

As an example let's think about the following environment variables

```bash
PERLA_clientToken=abcdefg1234557
PERLA_API_KEY=12334566abcdefg
```

By default, Perla doesn't need any particular configuration, these variables will be available at the server route of `/env.js`

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

If for some reason you want to change the path of the environment variables file, you can do so by modifying the `perla.json` configuration to include the `enableEnv` and `envPath`

```json
{
  "enableEnv": true,
  "envPath": "/path/to/custom-env.js"
}
```

and thepending on the `envPath` you will be able to import the environment variables from that path.

```js
import { clientToken, API_KEY } from "/path/to/custom-env.js";
```

### Why not `import.meta.env`?

While the ecosystem has been moving towards `import.meta` these variables are actually controlled by the runtime and not the build tool, so bundlers like Vite or Webpack perform some string replacement at build/serve in order to replace those variables with the actual values. For us this feels more like a hack than a feature, having a file that provides the environment variables feels like a more flexible and explicit option to handle these important (but ultimately public) variables as you have better control in your server who can request those files and how they are served.

## Mounted Directories

Perla has some knowledge about your project structure based on conventions, one of those is "mounted directories".
by default Perla will mount the `src` directory into your server's `/src`, so your source files will mirror the structure of your project, you can however change this behavior by providing a `mountDirectories` section in your `perla.json` configuration file.

```json
{
  "mountDirectories": {
    "/src": "./src"
  }
}
```

for example: you could provide an "_assets_" directory to mount all of the images or other kinds of files in your project and serve them under "_/assets_" url.

```json
{ "/src": "./src", "/assets": "./assets" }
```

This configuration will also affect the way Perla will generate your final build so keep it in mind when you are building your project.
