[esbuild]: https://esbuild.github.io/
[esbuild has some caveats]: https://esbuild.github.io/content-types/#typescript-caveats

# Transpilation

Perla supports TypeScript, JSX, TSX, CSS, and modern JavaScript, primarily through [esbuild].

## How Transpilation Works in Perla

### Local esbuild Binary

Perla downloads a local copy of esbuild for your OS and architecture. This binary is stored in a central location and reused for all Perla projects, eliminating the need for multiple copies. Since Perla operates outside a Node.js environment, it supports what esbuild supports, with version compatibility determining the exact feature set.

### On-the-fly Transpilation

During development, Perla performs on-the-fly transpilation:

1. When a browser requests a file (e.g., a `.ts`, `.tsx`, or `.jsx` file), Perla's virtual file system locates it
2. The file is passed through the appropriate loader based on its extension:
   - `.ts` → TypeScript loader
   - `.tsx` → TSX loader
   - `.jsx` → JSX loader
   - `.css` → CSS loader
   - `.js` → No loader needed (passed through)
3. Esbuild transpiles the file to JavaScript
4. The transpiled content is served back to the browser

This process happens instantaneously thanks to the speed of both **Go** (esbuild) and **.NET** (Perla). You won't experience noticeable compilation delays or bundling phases during development.

### File Transformation Process

The transformation process involves several components:

1. **VirtualFileSystem**: Manages file access and applies transformations through plugins
2. **EsbuildService**: Provides the interface for processing JS and CSS files
3. **Handlers**: Coordinates the build process, including running esbuild
4. **SuaveService**: Handles HTTP requests and serves transformed files

### Special Transformations

Perla also supports special transformations when files are requested with the `?js` query parameter:

- **CSS files**: Transformed into JavaScript that creates a style element and injects the CSS content
- **JSON files**: Transformed into JavaScript that exports the JSON content as the default export

### Build Process

During the build process, Perla:

1. Collects all source files
2. Processes them with esbuild
3. Outputs optimized JavaScript and CSS files
4. Handles minification based on configuration

## Supported File Types

For TypeScript, JavaScript, TSX, JSX, and CSS support, you don't need any special configuration - they work out of the box.

## TypeScript Support

> [Esbuild has some caveats] when it comes to TypeScript support.

Perla uses esbuild's TypeScript transpilation, which performs type erasure rather than type checking. This means:

- Type errors won't prevent compilation
- Some TypeScript-specific features may not be fully supported
- For full type checking, it's recommended to run the TypeScript compiler (`tsc`) separately

## Configuration

Perla respects your project's `tsconfig.json` file for TypeScript configuration. The esbuild configuration can be customized in your `perla.json` file, allowing you to control:

- ECMAScript version target
- JSX handling (automatic or classic)
- JSX import source
- Minification settings
- File loaders
