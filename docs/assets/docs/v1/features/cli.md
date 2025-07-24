[dev server]: #v1/docs/features/development
[build tool]: #v1/docs/build/javascript
[package manager]: #v1/docs/features/package-manager
[pacakge search]: #v1/docs/features/package-manager
[fable]: #v1/docs/features/fable
[esbuild]: https://esbuild.github.io/
[perla.json]: #v1/docs/reference/perla
[scaffolding]: #v1/docs/features/scaffolding

> **_NOTE_**: This documentation is still being updated to reflect changes for V1, the contents may be outdated while this notice is still present

# Perla CLI

The Perla CLI is (hopefully) very straight forward it provides the following functionalities

- [Dev Server]
- [Build Tool]
- [Package Manager]
- [Pacakge Search]
- [Scaffolding]

## Dev Server

> Please visit [Dev Server] for a more complete introduction.

The dev server is as simple as typing `perla serve`, Perla will try to look for the `perla.json` configuration file and depending on the content it will start derving the content in `localhost:7331` by default, if you're not using [Fable] this is instantaneous and you can start modyfing files right away. When you use [Fable] while the dev server is running but your Fable build is still running, once it finishes the website will load as usual and recurrent changes will auto-reload as soon as the files are compiled.

In your [perla.json] look for the `devServer` object to configure it.

## Build Tool

The build command is also as simple as `perla build` this will grab your `index.html` and `perla.json` and produce a minified, tree-shaken, production ready courtesy of [esbuild] you can also configure

In your [perla.json] look for the `build` object to configure it.

## Package Manager

> Please check [Package Manager] for a more complete reference.

You have a project now, what about dependencies? what if you want to use something like `lodash` or `moment`? Then simply type `perla add moment lodash`.

> **NOTE**: It is always worth mentioning, please ensure you are using the correct packages to prevent bundle bloat or security holes in your applications.

## Scaffolding

> Please visit [Scaffolding] for a more complete introduction.

It's quite annoying to have something set up manually each time you need to start a new project, Perla provides scaffolding features that are extensible via Scriban templates (the syntax is like handlebars) and F# script files.

- `perla templates ls` - Will show you the templates you have downloaded
- `perla templates GitHubUsername/Repository --add` - will download that repository for future references
- `perla templates GitHubUsername/Repository --update` - Will re-download the github repository
- `perla templates GitHubUsername/Repository --remove` - will remove said repository
