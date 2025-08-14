[real world fable]: https://github.com/AngelMunoz/real-world-fable

> **_NOTE_**: This documentation is still being updated to reflect changes for V1, the contents may be outdated while this notice is still present

# Fable |> Perla = ❤️

This is probably one of the main drivers for Perla. F# Users who are doing frontend development today have had some friction with the Node.Js ecosystem in the past and we believe there's a desire of reducing the amount of complexity in the toolchain when it comes to Fable projects.

I believe F# users can start using Perla given that Fable outputs modern javascript and that's the easiest input to process by Perla

As an example for that take a look at the [Real World Fable] and as the repository says

> This codebase was created to demonstrate a fully fledged fullstack application built with Fable, Elmish and F# including CRUD operations, authentication, routing, pagination, and more.

While it's a bit outdated this repo was created on the times of Fable 2 and Perla can make it run today with Fable 3 and produce a production ready build from it. That being said... let's take a look at a normal project structure for a Fable project

# Project Structure

Fable projects are farly straight forward the project structure doesn't differ too much from non-fable projects, the differences reside on the presence of perla as a dotnet tool as well as fable as a dotnet tool plus the F# sources.

```txt
.config/
    dotnet-tools.json
index.html
perla.json
src/
    App.fsproj
    Main.fs
```

You can start of with the empty fable template (`perla new my-project -t fempty`) or the Feliz (`perla new my-project -t feliz`) Templates or start from scratch.

```text
mkdir project
cd project
dotnet new tool-manifest
dotnet tool install perla
dotnet tool install fable
dotnet tool install fantomas
dotnet new classlib -o src -n App
touch index.html
```

your `index.html` should have something like this

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta http-equiv="X-UA-Compatible" content="IE=edge" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Perla App</title>
    <!-- import maps shim/polyfill
        untill it is natively supported in the browser -->
    <script
      async
      src="https://ga.jspm.io/npm:es-module-shims@2.6.1/dist/es-module-shims.js"
      crossorigin="anonymous"
    ></script>
  </head>
  <body>
    <div id="fable-app"></div>
    <script data-entry-point src="./src/Main.fs.js" type="module"></script>
  </body>
</html>
```

then you can start with `dotnet perla serve` and it will automatically start the Fable Compilation in watch mode as well as the saturn dev server

### Migrating from Webpack Based projects

If for some reason you have a spare project based on webpack or you believe in Perla and want to proceed further here's a checklist you need to fill first

- [x] You're not using SASS/LESS or any other CSS pre-processor
- [x] You're not using CSS Modules (the popular solution, not the browser spec)
- [x] You don't depend on plugins that required node.js to run

That's it.

Some things to take into account as well:

- There is no way to run tests at the moment.

  Current node based tooling has test runners like mocha that run unit tests on compiled F# -> JS code, Perla doesn't have an alternative for that yet.

#### Migration Steps

If you want to go ahead, then ensure of the following:

1. Move `index.html` to the root of your project or to the CWD you're going to run the _Perla CLI_
2. Replace css imports from npm packages with a CDN based alternative, example for `Bulma CSS`:

- From: `importSideEffects "bulma/css/bulma.css"`
- To:
  ```html
  <!-- inside your index.html -->
  <link
    rel="stylesheet"
    href="https://cdn.skypack.dev/-/bulma@v0.9.3-qiMIbVYAqsanl5Ue4g6H/dist=es2020,mode=raw/css/bulma.min.css"
  />
  ```
  > If you have your own CSS Files you don't need to do anything they can stay like `importSideEffects "./index.css"` these imports are supported, only external css files are required to be changed.

3. Import your entry point compiled F# -> JS file in the index.html

Your HTML file should look along these lines

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta http-equiv="X-UA-Compatible" content="IE=edge" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Perla App</title>
    <!-- import maps shim/polyfill
        untill it is natively supported in the browser -->
    <script
      async
      src="https://ga.jspm.io/npm:es-module-shims@2.6.1/dist/es-module-shims.js"
      crossorigin="anonymous"
    ></script>
    <link
      rel="stylesheet"
      href="https://cdn.skypack.dev/-/bulma@v0.9.3-qiMIbVYAqsanl5Ue4g6H/dist=es2020,mode=raw/css/bulma.min.css"
    />
  </head>
  <body>
    <div id="fable-app"></div>
    <!-- data-entry-point is very important when building for production
         this attribute tells perla that it must process the file when building
         you can include multiple `data-entry-pont` scripts -->
    <script data-entry-point src="./src/Main.fs.js" type="module"></script>
  </body>
</html>
```

5. Install your dependencies. Example:

   - `perla add react react-dom react-dom/client`

   To identify which dependencies are the ones you actually need, they are usually in the `dependencies` object inside your `package.json`

   > **_NOTE_**: If your project has deep imports (like react-dom/client above) you will have to add those with the perla CLI too, otherwise you will get an error like
   >
   > ```text
   > Uncaught TypeError: Failed to resolve module specifier "react-dom/client". Relative references must start with either "/", "./", or "../".
   > ```
   >
   > which means that the import map is missing the `react-dom/client` entry.

6. Do a test run

   - `dotnet perla serve`

7. Check in the browser's console for errors like

   ```text
   Uncaught TypeError: Failed to resolve module specifier "components/button".
   Relative references must start with either "/", "./", or "../".
   ```

   That usually means you are missing a package from your npm dependencies or that you are trying to do something like `importSideEffects "components/button"` when the correct import must include the `.js` extension `importSideEffects "./components/button.js"`

   If at this point you don't see any error and your application is working then you can finally remove any `package.json`, and `package-lock.json`

## Congratulations, Welcome to Perla :)
