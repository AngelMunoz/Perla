[target]: https://esbuild.github.io/api#target
[development]: #v1/docs/features/development?id=environment-variable-support

## Production Builds

Perla sets a few defaults when it comes to final builds/bundles which can be overridden in the `build` and `esbuild` properties in the `perla.json` configuration file.

Relevant Properties in `perla.json`:

- **Provider**:

  This is the provider that will be used to fetch the dependencies, by default it's set to `jspm` as it is the most stable CDN available that works with our tooling but you can change it to `unpkg`, or `jsdelivr` if you prefer.

- **Build**:

  Build runs after esbuild, ensuring all contents of the esbuild output and the build options are properly copied to the final destination directory. These files are grabbed from the virtual file system introduced in v1.

- **Esbuild**:

  Perla provides sensible defaults for esbuild that should work for most applications. However, you can override these defaults by specifying properties in the `esbuild` section of your `perla.json` for convenience. Esbuild will take the TypeScript/JavaScript sources and transpile them according to the values provided, such as the target output version (e.g., ES2020, which is supported by evergreen browsers at the time of writing).

### Esbuild's Target

> You can find this setting in `esbuild.ecmaVersion` within the `perla.json` configuration file.

Depending on what **Modern** browsers you want to support, you have a few options

- **Default: es2020**

Other options are:

- es + specification year. Examples: `es2019` or `es2020`
- chrome
- safari
- firefox
- edge
- esnext

> For more information check esbuild [target].

### Perla Env Files

Perla offers a way to capture some of the current OS environment variables and generate a file that can be used by the application at runtime. This is useful if you want to set some variables like API endpoints or the current environment (e.g. development or production).

This file is generated at runtime when you run `perla build` or `perla serve`, the file is not present in the disk when you run `perla serve` as it is only served. in the specied path `envPath` (defaults to `/env.js`). You can customize these settings to match your deployment environment's urls or paths.

Please keep in mind that these environment variables are only available in the browser, if you want to modify the build with environment variables, you would need to use a different approach as this is not supported.
