# Plugins

Perla supports plugins to extend build and serve functionality. Plugins must be authored manually as F# script files and placed in the `.perla/plugins` directory of your project.

## Using Plugins

Add plugins to your `perla.json` configuration by specifying the plugin name:

```json
{
  "plugins": ["markdown-plugin"]
}
```

Perla will automatically look for plugins in the `.perla/plugins` directory of your project. Each plugin is registered with a name specified in the plugin definition.

## Plugin API

The Perla plugin API provides several types and functions to help you create plugins:

### FileTransform

The core data structure that plugins operate on:

```fsharp
type FileTransform = {
  /// The text of the file, this will change between plugin transformations
  content: string
  /// The extension this file is currently holding
  /// this will change between plugin transformations
  /// It also serves for plugin authors to determine
  /// if their plugin should act on this particular file
  extension: string
  /// Full path to the source file
  fileLocation: string
}
```

### FilePredicate

A function that determines whether a plugin should process a file:

```fsharp
/// A function predicate that allows the plugin author
/// to signal if the file should be processed by the plugin or not
type FilePredicate = string -> bool
```

### Transform Functions

Perla supports multiple types of transform functions:

```fsharp
/// A Synchronous function that takes the content of the file and its extension
/// and returns the processed content and the new extension after processing the file
type Transform = FileTransform -> FileTransform

/// A Task<'T> based asynchronous function
type TransformTask = FileTransform -> Task<FileTransform>

/// An Async<'T> based asynchronous function
type TransformAsync = FileTransform -> Async<FileTransform>

/// A ValueTask<'T> based function (used internally by Perla)
type TransformAction = FileTransform -> ValueTask<FileTransform>
```

If a transform function cannot modify the content for any reason, it should return the original `FileTransform` and log the error to the console rather than crashing.

### Plugin Builder

Plugins are defined using a computation expression builder:

```fsharp
plugin "plugin-name" {
  should_process_file (fun extension -> /* predicate logic */)
  with_transform (fun file -> /* transform logic */)
}
```

The builder supports these operations:

- `should_process_file`: Defines a predicate to determine if a file should be processed
- `with_transform`: Defines the transformation to apply to matching files

Note that if you provide multiple instances of the same operation, only the first one will be used by Perla.

## Authoring Plugins

Plugins are written in F# as script files (`.fsx`). Each plugin exports logic using the Perla plugin API. The plugin receives hooks to process files during build or serve.

Example plugin (`.perla/plugins/markdown.fsx`):

```fsharp
#r "nuget: Markdig, 0.41.3"
#r "nuget: Perla.Plugins, 1.0.0-rc-002"

open Perla.Plugins
open Markdig

let pipeline =
  lazy
    (MarkdownPipelineBuilder()
      .UseAdvancedExtensions()
      .UsePreciseSourceLocation()
      .Build())

let shouldProcess: FilePredicate =
  fun extension -> [ ".md"; ".markdown" ] |> List.contains extension

let transform: Transform =
  fun args -> {
    args with
        content = Markdown.ToHtml(args.content, pipeline.Value)
        extension = ".html"
  }

plugin "markdown-plugin" {
  should_process_file shouldProcess
  with_transform transform
}
```

## Plugin Lifecycle

1. Perla loads plugins from the `.perla/plugins` directory at startup
2. Each plugin is registered with its specified name
3. During build or serve, for each file:
   - Perla checks each plugin's `should_process_file` predicate
   - If the predicate returns true, the file is passed to the plugin's transform function
   - The transform function processes the file and returns the modified content and extension
   - The transformed file is then passed to the next matching plugin or written to disk
