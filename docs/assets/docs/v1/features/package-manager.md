[import map]: #content/import-maps
[skypack]: https://www.skypack.dev/
[jspm]: https://jspm.org/docs/cdn
[unpkg]: https://unpkg.com/
[snowpack remote sources]: https://www.snowpack.dev/reference/configuration#packageoptionssourceremote

# Package Manager

Perla simplifies dependency management for your applications by reducing the amount of tools needed to build and develop them. By default, Perla uses import maps to pull your dependencies directly from Content Delivery Networks (CDNs) (inspired by [snowpack remote sources]), but also supports a local package mode that creates a familiar node_modules structure.

## Dependency Sources

Perla supports the following CDN providers:

- [JSPM] (jspm.io) - default provider
- [Unpkg] - alternative provider
- JsDelivr - alternative provider

You can configure which provider to use in your `perla.json` file by setting the `provider` property, or by using the `--source` flag when adding packages.

> **_Note_**: While these extra providers are supported by the JSPM API, they tend to be unreliable sometimes we recommend to stay with jspm as we're sure it just works™️.

## Dependency Modes

Perla supports two primary modes for handling dependencies:

### CDN Mode (Default)

By default (`useLocalPkgs: false`), Perla will generate an import map that points directly to the CDN URLs for your dependencies. This mode:

- Minimizes disk usage as dependencies aren't stored locally
- Provides faster setup as no download is required
- Works well for development when you have reliable internet access

### Local Package Mode

When you set `useLocalPkgs: true` in your `perla.json`, Perla will:

- Download all package files from the CDN
- Create a local `node_modules` directory structure compatible with standard JavaScript tooling
- Generate an import map that points to these local files
- Allow offline development once dependencies are downloaded

This structure mirrors what tools like npm, yarn, or pnpm would create, making it compatible with other tools that expect this directory structure.

## Add Package

To add a package to your project, run:

```bash
perla add <Package Name>
```

This will:

1. Fetch the package information from the default provider
2. Update your `perla.json` with the new dependency
3. Generate or update the import map to include the package
4. If `useLocalPkgs` is `true`, download the package files to your local `node_modules` directory

### Specifying a Provider

You can specify which provider to use for a particular package:

```bash
perla add <Package Name> -s jspm    # Use JSPM
perla add <Package Name> -s unpkg    # Use Unpkg
perla add <Package Name> -s jsdelivr # Use JsDelivr
```

### Specifying a Version

You can specify a particular version of a package:

```bash
perla add <Package Name>@<Version>
```

Examples:

```bash
perla add lodash@3        # Major version 3
perla add lodash@4.16.0   # Specific version 4.16.0
```

By default, Perla will use the latest available release if no version is specified.

> **_Note_**: While JS Package managers tend to use relative versions like `^1.2.3` or `~1.2.3`, Perla does not support these formats directly. We prefer to pin the exact version to ensure consistency across builds. If you need to update a package, you can run `perla add <Package Name>` again with the desired version.

### How it Works

When you add a package, Perla:

1. Contacts the JSPM API to get the correct [import map] URL
2. Retrieves corresponding _scopes_ if the API supports them
3. Updates your project's dependency list in `perla.json`
4. If `useLocalPkgs` is enabled, downloads the package files to create a local `node_modules` structure

## Remove Package

To remove a package from your project, run:

```bash
perla remove <Package Name> <Package Name 2>
```

When removing a package, Perla will:

1. Remove the package from your `perla.json` dependencies
2. Update the import map to remove references to the package
3. If using `useLocalPkgs: true`, the local package files will be consolidated (unused packages won't be included)

Note that when using `useLocalPkgs: true`, Perla will regenerate the import map with the remaining packages to ensure consistency between your `perla.json` and the local `node_modules` directory.

## Restore Packages

To restore or reinstall all dependencies listed in your `perla.json` file, run:

```bash
perla restore
```

This command will:

1. Read the dependencies from your `perla.json` file
2. Generate an import map for all dependencies
3. If `useLocalPkgs` is `true`, download all package files and create a local `node_modules` structure

### Restore Options

You can modify the restore behavior with these flags however, to make them permanent you have to update the perla.json configuration file:

```bash
# Change the provider for this restore
perla restore --source jspm     # Use JSPM (default)
perla restore --source unpkg    # Use Unpkg
perla restore --source jsdelivr # Use JsDelivr

# Control offline mode
perla restore --offline         # Enable local packages mode
```

These options can also be configured in your `perla.json` file:

```json
{
  "provider": "jspm", // or "unpkg", "jsdelivr"
  "useLocalPkgs": true // or false for CDN-only mode
}
```
