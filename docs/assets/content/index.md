[esbuild]: https://esbuild.github.io/
[skypack]: https://www.skypack.dev/
[jspm]: https://jspm.org/docs/cdn
[unpkg]: https://unpkg.com/
[install]: /#/content/install
[run]: /#/v1/docs/features/development
[build]: /#/v1/docs/features/cli
[jsx]: /#/v1/docs/build/jsx-tsx
[tsx]: /#/v1/docs/build/jsx-tsx
[import maps]: /#/content/import-maps
[perla templates]: https://github.com/AngelMunoz/perla-templates
[real world fable]: https://github.com/AngelMunoz/real-world-fable
[scaffolding]: /#/v1/docs/features/scaffolding

# Perla Dev Server

The simplest cross-platform dev server you will find.

Our Belief is that you shouldn't be obligated to learn node.js nor have a complex toolchain to develop a **SPA** (Single Page Application). In a few words you should be able to:

- [Install] a tool.
- [Run] the tool.
- Focus on your your SPA.
- [Build] your production ready SPA.

If you are learning or are a seasoned developer, the priority should be on your application, not the tooling around it.

Perla leverages the excelent **.NET** and **Go** ecosystems to give you a simple and effective development server.

Once you [install] it simply run:

```sh
perla init

# generate a new project
perla new my-preact-project perla-templates/preact-jsx && cd my-preact-project

perla serve
```

> Looking for other frameworks?
>
> Check the complete list of default templates
>
> - `perla templates:list`

Check [scaffolding] to see how you can author your own templates as well!

### How do Perla projects look like?

Check the [perla templates] where you can projects written in:

- Javascript (Lit)
- Typescript (Lit)
- JSX (React, and Preact)
- TSX (React, and Preact)
- F# (Feliz, Sutil)

Also For F# users, we have a [Real World Fable] implementation in fable-react from the days of fable 2.0 revived with Perla tooling.
