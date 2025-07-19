> **_NOTE_**: This documentation is still being updated to reflect changes for V1, the contents may be outdated while this notice is still present

## CLI Reference

### Serve - `perla serve`

Starts a development server for modern JavaScript development.

- `-p`, `--port <number>`: Port to listen on (default: 7331)
- `--host <host>`: Host to bind the dev server (default: localhost)
- `--ssl`: Enable SSL for local development (default: false)

### Build - `perla build`

Builds the SPA application for distribution.

- `-p`, `--preview`: Enable preview mode (build and start a static server)

### New - `perla new`

Creates a new Perla-based project from a template.

- `name`: Name of the new project (required)
- `--id <template-id>`: Fully qualified template name (e.g. perla.templates.vanilla.js)
- `-t`, `--template <shortname>`: Short name of the template (e.g. ff)
- `--skip`, `-s`, `-y`: Skip interactive prompts and use defaults

### Templates - `perla templates`

Handles template repository operations (list, add, update, remove).

- `TemplateRepositoryName`: The User/repository name combination
- `-a`, `--add`: Add the template repository
- `-u`, `--update`: Update the template repository
- `-r`, `--remove`: Remove the template repository
- `--list-format <table|text>`: Format to display templates (default: table)

### Add - `perla add`

Adds a package to the project dependencies.

- `packages`: One or more package names to add (required)

### Remove - `perla remove`

Removes a package from the project dependencies.

- `packages`: One or more package names to remove (required)

### Install - `perla install`

Installs the project dependencies from the perla.json file.

- `-o`, `--offline`: Install packages without network access
- `-s`, `--source <provider>`: The source to download packages from (jspm.io, unpkg, jsdelivr)

### List - `perla list`

Lists the current dependencies in a table or npm-style JSON string.

- `--as-package-json`, `-j`: Show the packages in npm's package.json format

### Test - `perla test`

Runs client-side tests in a headless browser (hidden command).

- `-b`, `--browsers <browser>`: Browsers to run tests with (chromium, firefox, webkit, edge, chrome)
- `-t`, `--tests <pattern>`: Glob of tests to run
- `-s`, `--skip <pattern>`: Glob of tests to skip
- `-hl`, `--headless`: Run browsers in headless mode
- `-w`, `--watch`: Watch for file changes and re-run tests
- `-bs`, `--browser-sequential`: Run each browser's test suite in sequence

### Describe - `perla describe`

Describes the perla.json file or its properties as requested.

- `properties`: One or more property names or JSON path-like strings
- `-c`, `--current`: Print the current configuration from perla.json

---

### Deprecated or Unavailable Commands/Options

- `perla init`, `perla search`, `perla show`, `perla List Template`, `perla add-template`, `perla update-template`, `perla remove-template`, `perla version`

  These commands/options are not present in the current CLI implementation or have been replaced. Please refer to the above commands for supported operations.
