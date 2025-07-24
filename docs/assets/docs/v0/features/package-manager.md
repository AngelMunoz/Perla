[import map]: #/content/import-maps
[skypack]: https://www.skypack.dev/
[jspm]: https://jspm.org/docs/cdn
[unpkg]: https://unpkg.com/
[snowpack remote sources]: https://www.snowpack.dev/reference/configuration#packageoptionssourceremote

# Package Manager

Perla tries to simplify the tooling for your applications reducing the amount of tools you need to build/develop the applications themselves in the case of Perla we go a step further (inspired by [snowpack remote sources]) and pull your dependencies directly from the cloud using the following Content Delivery Networks

- [Skypack] - default source
- [JSPM] - optional source
- [Unpkg] - optional source

Sometimes a package might now work in a particular source, you can simply choose which one works best for you, thankfully these CDNs are really good and reliable

## Add Package

Next to your `perla.jsonc` type and enter

- `perla add <Package Name>`

this will fetch the package from the default source, if you need a particular source for a particular package, just pass the `-s` or `--source` flag

- `perla add <Package Name> -s jspm`

Perla will request to the corresponding API to provide the correct [import map] url and if the API supports it, we will also grab the corresponding _scopes_ for those sources.

If you need a specific version of a package you can specify it `perla add <Package Name>@<Version>` e.g. `perla add lodash@3` or `perla add lodash@4.16.0` by default Perla will pick the latest available release.

### Aliased Packages

You can also include multiple versions of the same package side by side using an `alias`

- `perla add lodash@3 --alias lodash3`
- `perla add lodash`

and you will be able to do the following imports

```javascript
import { dependency } from "lodash";
import { dependencyV3 } from "lodash3";
```

the packages will still use their internal packages as usual but you will be able to use the particular version you need side by side, which can be helpful when migrating from legacy applications.

## Remove Package

To remove a package it is very similar

- `perla remove <Package Name>`

If you required a particular version you will need to also pass the `@<Version>`.

## Search and Show Package's Information

Perla offers an unoficial package search from the [skypack] API, Perla searches the package on their API and we will try to print relevant information about it

- `perla search -n <Package Name>`

  Optionally you can search between pages passing the page number

  - `perla search -n <Package Name> -p <Page Number>`

- `perla show -p <Package Name>`

Although useful, these two functionalities might get removed in the future if there is no demand for them.
