module Perla.Constants

[<Literal>]
let Esbuild_Version = "0.19.5"

[<Literal>]
let Esbuild_Target = "es2020"

[<Literal>]
let Default_Templates_Repository = "AngelMunoz/perla-templates"

[<Literal>]
let ArtifactsDirectoryname = "perla"

[<Literal>]
let Default_Templates_Repository_Branch = "main"

[<Literal>]
let PerlaConfigName = "perla.json"

[<Literal>]
let IndexFile = "index.html"

[<Literal>]
let FableProject = "./src/App.fsproj"

[<Literal>]
let EnvPath = "/env.js"

[<Literal>]
let EnvBareImport = "@@perla/env"

[<Literal>]
let ProxyConfigName = "proxy-config.json"

[<Literal>]
let ScaffoldConfiguration = "TemplateConfiguration"

[<Literal>]
let TemplatesDatabase = "templates.db"

[<Literal>]
let TemplatesDirectory = "templates"

[<Literal>]
let OfflineTemplatesDirectory = "offline-templates"

[<Literal>]
let ImportMapName = "perla.json.importmap"

[<Literal>]
let TemplatingScriptName = "templating.fsx"

[<Literal>]
let JsonSchemaUrl =
  "https://raw.githubusercontent.com/AngelMunoz/Perla/main/perla.schema.json"

[<Literal>]
let SchemaVersion = "2025-07"

[<Literal>]
let PerlaEsbuildPluginName = "perla-esbuild-plugin"
