[esbuild]: https://esbuild.github.io/
[skypack]: https://www.skypack.dev/
[jspm]: https://jspm.org/docs/cdn
[unpkg]: https://unpkg.com/
[install]: #content/install
[run]: #v1/docs/features/development
[build]: #v1/docs/features/cli
[jsx]: #v1/docs/build/jsx-tsx
[tsx]: #v1/docs/build/jsx-tsx
[import maps]: #content/import-maps
[real world fable]: https://github.com/AngelMunoz/real-world-fable
[scaffolding]: #v1/docs/features/scaffolding

# Perla Dev Server

The simplest cross-platform dev server you will find, focused on **web-first** and **standards-first** web development.

## Web-First, Standards-First Philosophy

- **Import maps are first-class citizens**: Perla uses import maps as the foundation for module resolution, ensuring your projects are portable and standards-compliant.
- **esbuild is optional**: You can use esbuild for enhanced development experience (like JSX/TSX, minification, or bundling), but it is **not required** and is disabled by default. Your app will work out of the box with just browser standards.
- **Foreign modules (CSS/JSON)**: Perla encourages the use of the [import assertions](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Statements/import/with) spec for loading non-JS modules, following the latest web platform standards.
- **Advanced features are a plus, not a requirement**: Features like minification, local package support, and bundling are available for those who want them, but never required to get started or to build a standards-compliant app.

Our belief is that you shouldn't be obligated to learn node.js nor have a complex toolchain to develop a **SPA** (Single Page Application). In a few words you should be able to:

- [Install] a tool.
- [Run] the tool.
- Focus on your SPA.
- [Build] your production ready SPA.

If you are learning or are a seasoned developer, the priority should be on your application, not the tooling around it.

Perla leverages the excellent **.NET** and **Go** ecosystems to give you a simple and effective development server.

Once you [install] it simply run:

```sh
perla new my-project -t basic && cd my-project

perla serve
```

> Looking for other frameworks?
>
> Check the complete list of default templates
>
> - `perla templates ls`
>
> - Basic (vanilla HTML/JS)
> - Basic Local (vanilla HTML/JS, local dependencies)
> - F# (Fable)
> - F# (Feliz, React SPA)
> - JSX (Preact, local dependencies)

Check [scaffolding] to see how you can author your own templates as well!

Also For F# users, we have a [Real World Fable] implementation in fable-react from the days of fable 2.0 revived with Perla tooling.
