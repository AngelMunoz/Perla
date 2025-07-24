[target]: https://esbuild.github.io/api#target
[development]: #/v0/docs/features/development?id=environment-variable-support

## Production Builds

Perla sets a few defaults when it comes to final builds/bundles which can be overriden in the `build` property in the `perla.json` configuration file.

### Target (target)

Depending on what **Modern** browsers you want to support, you have a few options

- **Default: es2020**

Other options are:

- es + specification year. Examples: `es2018` or `es2020`
- chrome
- safari
- firefox
- edge
- esnext

> For more information check esbuild [target].

### Out Directory (outDir)

This option dictates where do you want your files to be generated.

- **Default: ./dist**

You can set this option to any relative path of your choice.

### Bundle (bundle)

Boolean option that allows you to inline your imports.

- **Default: true**

### Format (format)

Which bundle format to produce in the final build.

- **Default: esm**

Options:

- iife
- esm

To get most out of it we recommend you to not change this setting.

### Minify (minify)

Minify the sources to reduce bundle size.

- **Default: true**

### Emit Env File (emitEnvFile)

- **Default: true**

If you have added environment variables for development (see [development]]) you could also generate the same file at build in case you want to deploy right after you generated your production build. This setting is opt out rather than opt in, if no `PERLA_` env vars are present then the file won't be emitted.
