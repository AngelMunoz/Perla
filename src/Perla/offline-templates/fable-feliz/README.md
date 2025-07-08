# Perla + Feliz Template

Visit the Perla Docs at https://perla-docs.web.app

## Requirements

- [dotnet SDK](http://get.dot.net/)

## Editor

To write and edit your code, you can use either VS Code + [Ionide](http://ionide.io/), Emacs with [fsharp-mode](https://github.com/fsharp/emacs-fsharp-mode), [Rider](https://www.jetbrains.com/rider/) or Visual Studio.

## Development

Then to start development mode:

```bash
dotnet tool restore
dotnet perla serve
```

This will start the development server after compiling the project, once it is finished, navigate to http://localhost:7331 to view the application .

To build the application and make ready for production:

```
dotnet perla build
```

This command builds the application and puts the generated files into `dist`.
