[dev server]: #/v0/docs/features/development
[build tool]: #/v0/docs/build/javascript
[package manager]: #/v0/docs/features/package-manager
[pacakge search]: #/v0/docs/features/package-manager
[fable]: #/v0/docs/features/fable
[esbuild]: https://esbuild.github.io/
[perla.jsonc]: #/v0/docs/reference/perla
[scaffolding]: #/v0/docs/features/scaffolding

# Perla CLI

The Perla CLI is (hopefully) very straight forward it provides the following functionalities

- [Dev Server]
- [Build Tool]
- [Package Manager]
- [Pacakge Search]
- Interactive Mode
- [Scaffolding]

## Dev Server

> Please visit [Dev Server] for a more complete introduction.

The dev server is as simple as typing `perla serve`, Perla will try to look for the `perla.jsonc` configuration file and depending on the content it will start derving the content in `localhost:7331` by default, if you're not using [Fable] this is instantaneous and you can start modyfing files right away. When you use [Fable] while the dev server is running but your Fable build is still running, once it finishes the website will load as usual and recurrent changes will auto-reload as soon as the files are compiled.

In your [perla.jsonc] look for the `devServer` object to configure it.

## Build Tool

The build command is also as simple as `perla build` this will grab your `index.html` and `perla.jsonc` and produce a minified, tree-shaken, production ready courtesy of [esbuild] you can also configure

In your [perla.jsonc] look for the `build` object to configure it.

## Package Manager

> Please check [Package Manager] for a more complete reference.

You have a project now, what about dependencies? what if you want to use something like `lodash` or `monent`? Then simply type `perla add moment` or `perla add lodash -s jspm` if you want a different source for your packages.

If you're not sure what the correct name for your dependency is then try searching for it `perla search -n lodash-es`, if you think that might be the correct package then you can see more details about it `perla show -p lodash-es`

> **NOTE**: It is always worth mentioning, please ensure you are using the correct packages to prevent bundle bloat or security holes in your applications

## Interactive Mode

When Perla starts in dev server mode, Perla also accepts commands for [Fable] and the dev server itself some of these options are

- Server Commands
  - `restart`
  - `start`
  - `stop`
- Fable Commands
  - `restart:fable`
  - `start:fable`
  - `stop:fable`
- CLI Commands
  - `clear` or `cls`
  - `exit`

Besides supporting <kbd>Ctrl</kbd> + <kbd>C</kbd> to stop the Perla process, the interactive mode also supports the rest of the Perla CLI commands (except from `build`, `serve`, `init`) so even if you're developing your SPA you can add dependencies on the fly without having to restart your server (after all the dependencies live on CDN's)

## Scaffolding

> Please visit [Scaffolding] for a more complete introduction.

It's quite annoying to have something set up manually each time you need to start a new project, Perla provides scaffolding features that are extensible via Scriban templates (the syntax is like handlebars) and F# script files.

- `perla add GitHubUsername/Repository` - will download that repository for future references
- `perla list-templates` - Will show you the templates you have downloaded
- `perla update GitHubUsername/Repository` - Will re-download the github repository
- `perla remove GitHubUsername/Repository` - will remove said repository
- `perla new -t Repository/template -n my-new-project` - will create a new prla project under the `my-new-project` directory, if you have multiple repositories with the same name for some reason you can disambiguate by writing the github username as well (`-t GitHubUsername/Repository/template`)
