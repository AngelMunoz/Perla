module Perla.Tests.ImportMaps

open Xunit
open Perla
open Perla.Units
open FSharp.UMX
open System.IO
open Perla.Types
open Perla.PkgManager
open FSharp.Data.Adaptive

let testRootDir =
  // Use a platform-correct absolute path for the test root
  let baseDir =
    if
      System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
        System.Runtime.InteropServices.OSPlatform.Windows
      )
    then
      "C:\\perla-test-root"
    else
      "/tmp/perla-test-root"

  Path.GetFullPath(baseDir)

let testFile fileName = Path.Combine(testRootDir, fileName)

[<Fact>]
let ``replaceImports: import { Button } from '/components/my-button.js'``() =
  let input = "import { Button } from '/components/my-button.js'"
  let prefix = "/components/"
  let replacement = "./components/"

  let expected = $"import {{ Button }} from './components/my-button.js'"

  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: import { Button } from at_src_services_my_service_js``() =
  let input = "import { Button } from \"@src/services/my-service.js\""
  let prefix = "@src/"
  let replacement = "./src/"

  let expected = "import { Button } from \"./src/services/my-service.js\""

  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: import x from '/components/other.js'``() =
  let input = "import x from '/components/other.js'"
  let prefix = "/components/"
  let replacement = "./components/"
  let expected = "import x from './components/other.js'"
  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``getExternals: should extract all externals from ImportMap imports and scopes``
  ()
  =
  let importMap = {
    Perla.PkgManager.ImportMap.imports =
      [
        "@preact/signals",
        "https://ga.jspm.io/npm:@preact/signals@2.2.1/dist/signals.module.js"
        "@preact/signals-core",
        "https://ga.jspm.io/npm:@preact/signals-core@1.11.0/dist/signals-core.module.js"
      ]
      |> Map.ofList
    Perla.PkgManager.ImportMap.scopes =
      [
        "https://ga.jspm.io/",
        [
          "preact",
          "https://ga.jspm.io/npm:preact@10.26.9/dist/preact.module.js"
          "preact/hooks",
          "https://ga.jspm.io/npm:preact@10.26.9/hooks/dist/hooks.module.js"
        ]
        |> Map.ofList
      ]
      |> Map.ofList
    Perla.PkgManager.ImportMap.integrity = Map.empty
  }

  let result = ImportMaps.getExternals importMap
  Assert.Equal(4, result.Length)
  Assert.Contains("@preact/signals", result)
  Assert.Contains("@preact/signals-core", result)
  Assert.Contains("preact", result)
  Assert.Contains("preact/hooks", result)

[<Fact>]
let ``replaceImports: import('/components/dyn.js')``() =
  let input = "import('/components/dyn.js')"
  let prefix = "/components/"
  let replacement = "./components/"
  let expected = "import('./components/dyn.js')"
  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: import('/components/dyn.js', { with: { type: 'json' } })``
  ()
  =
  let input = "import('/components/dyn.js', { with: { type: 'json' } })"
  let prefix = "/components/"
  let replacement = "./components/"

  let expected = "import('./components/dyn.js', { with: { type: 'json' } })"

  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: import(`/components/${var}`) (should not replace)``() =
  let input = "import(`/components/${var}`)"
  let prefix = "/components/"
  let replacement = "./components/"
  let expected = "import(`/components/${var}`)"
  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: import('/not-matching.js') (should not replace)``() =
  let input = "import('/not-matching.js')"
  let prefix = "/components/"
  let replacement = "./components/"
  let expected = "import('/not-matching.js')"
  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports prefers longer prefix match``() =
  let input = "import { X } from '@src/longer/path/file.js'"

  let paths =
    [
      (UMX.tag "@src/", UMX.tag "./src/")
      (UMX.tag "@src/longer/path/", UMX.tag "./src/longer/path/")
    ]
    |> Map.ofList

  let expected = "import { X } from './src/longer/path/file.js'"

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Theory>]
[<InlineData("import('/components/' + var)", "/components/", "./components/")>]
[<InlineData("import(`/components/${var}`)", "/components/", "./components/")>]
let ``replaceImports does not replace dynamic imports with expressions``
  (input: string, prefix: string, replacement: string)
  =
  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(input, actual)

[<Fact>]
let ``replaceImports: replaces multiple different imports in the same file``() =
  let input =
    "import { Button } from \"/components/my-button.js\"\nimport { Button } from \"@src/services/my-service.js\""

  let paths =
    [
      (UMX.tag "/components/", UMX.tag "./components/")
      (UMX.tag "@src/", UMX.tag "./src/")
    ]
    |> Map.ofList

  let expected =
    "import { Button } from \"./components/my-button.js\"\nimport { Button } from \"./src/services/my-service.js\""

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: replaces multiple different imports in a single line``() =
  let input =
    "import { Button } from \"/components/my-button.js\";import { Button } from \"@src/services/my-service.js\""

  let paths =
    [
      (UMX.tag "/components/", UMX.tag "./components/")
      (UMX.tag "@src/", UMX.tag "./src/")
    ]
    |> Map.ofList

  let expected =
    "import { Button } from \"./components/my-button.js\";import { Button } from \"./src/services/my-service.js\""

  let actual =
    ImportMaps.replaceImports
      paths
      (testFile "dummy.js")
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: deeply nested import path to top-level resolution url``
  ()
  =
  let input =
    "import { PillButton, PrimaryButton } from '/components/buttons.js'"

  let prefix = "/components/"
  let replacement = "./components/"

  let expected =
    "import { PillButton, PrimaryButton } from '../../../../components/buttons.js'"

  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]
  // importing file is deeply nested
  let importingFile = "src/feature1/deep/nested/code.js"
  // sourcesRoot is project root
  let actual =
    ImportMaps.replaceImports
      paths
      (testFile importingFile)
      (UMX.tag<SystemPath> testRootDir)
      input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: deeply nested import path to top-level resolution url (single import)``
  ()
  =
  let input = "import x from '/components/foo.js'"
  let prefix = "/components/"
  let replacement = "./components/"
  let expected = "import x from '../../../../components/foo.js'"
  let paths = Map.ofList [ (UMX.tag prefix, UMX.tag replacement) ]
  let importingFile = "src/feature1/deep/nested/code.js"
  let sourcesRoot = UMX.tag<SystemPath> testRootDir

  let actual = ImportMaps.replaceImports paths importingFile sourcesRoot input

  Assert.Equal(expected, actual)

[<Fact>]
let ``replaceImports: deeply nested file with multiple import map entries``() =
  let paths =
    [
      (UMX.tag "/components/", UMX.tag "./src/internal/components/")
      (UMX.tag "@services", UMX.tag "./src/services")
    ]
    |> Map.ofList

  // Test importing from /components/ in a deeply nested file
  let input1 = "import Button from '/components/Button.js'"
  let importingFile1 = "src/feature1/views/my-view.js"
  let expected1 = "import Button from '../../internal/components/Button.js'"

  let actual1 =
    ImportMaps.replaceImports
      paths
      (testFile importingFile1)
      (UMX.tag<SystemPath> testRootDir)
      input1

  Assert.Equal(expected1, actual1)

  // Test importing from @services in a deeply nested file
  let input2 = "import api from '@services/api.js'"
  let importingFile2 = "src/feature2/deep/other.js"
  let expected2 = "import api from '../../services/api.js'"

  let actual2 =
    ImportMaps.replaceImports
      paths
      (testFile importingFile2)
      (UMX.tag<SystemPath> testRootDir)
      input2

  Assert.Equal(expected2, actual2)

  // Test both imports in the same file
  let input3 =
    "import Btn from '/components/Btn.js'\nimport svc from '@services/util.js'"

  let importingFile3 = "src/feature1/views/my-view.js"

  let expected3 =
    "import Btn from '../../internal/components/Btn.js'\nimport svc from '../../services/util.js'"

  let actual3 =
    ImportMaps.replaceImports
      paths
      (testFile importingFile3)
      (UMX.tag<SystemPath> testRootDir)
      input3

  Assert.Equal(expected3, actual3)

// --- Mount-aware cleanupLocalPaths and resolveForBuildA tests ---

[<Fact>]
let ``cleanupLocalPaths removes server path covered by mount``() =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let importMap = {
    ImportMap.Empty with
        imports =
          Map.ofList [ UMX.tag "@components/", "/src/shared/components/" ]
  }

  let result = ImportMaps.cleanupLocalPaths mounts importMap
  Assert.False(result.imports.ContainsKey(UMX.tag "@components/"))

[<Fact>]
let ``cleanupLocalPaths removes normalized relative path covered by mount``() =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let importMap = {
    ImportMap.Empty with
        imports =
          Map.ofList [ UMX.tag "@components/", "./src/shared/components/" ]
  }

  let result = ImportMaps.cleanupLocalPaths mounts importMap
  Assert.False(result.imports.ContainsKey(UMX.tag "@components/"))

[<Fact>]
let ``cleanupLocalPaths keeps external URL``() =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let importMap = {
    ImportMap.Empty with
        imports =
          Map.ofList [ UMX.tag "confetti", "https://esm.sh/confetti@1.0.0" ]
  }

  let result = ImportMaps.cleanupLocalPaths mounts importMap
  Assert.True(result.imports.ContainsKey(UMX.tag "confetti"))

[<Fact>]
let ``cleanupLocalPaths keeps path not covered by any mount``() =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let importMap = {
    ImportMap.Empty with
        imports = Map.ofList [ UMX.tag "@other/", "/other/path/" ]
  }

  let result = ImportMaps.cleanupLocalPaths mounts importMap
  Assert.True(result.imports.ContainsKey(UMX.tag "@other/"))

[<Fact>]
let ``resolveForBuildA produces correct externals and import map``() =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let config = {
    Defaults.PerlaConfig with
        mountDirectories = mounts
        useLocalPkgs = true
        plugins = [ Constants.PerlaEsbuildPluginName ]
        paths =
          Map.ofList [
            UMX.tag "@components/", UMX.tag "/src/shared/components/"
            UMX.tag "confetti", UMX.tag "https://esm.sh/confetti@1.0.0"
            UMX.tag "@other/", UMX.tag "/other/path/"
          ]
  }

  let importMap = ImportMap.Empty

  // Simulate the build pipeline: merge config.paths into the import map
  let mergedImportMap = ImportMaps.withPaths config.paths importMap

  let configA = AVal.constant config
  let importMapA = AVal.constant mergedImportMap
  let resultA = ImportMaps.resolveForBuildA configA importMapA
  let result = AVal.force resultA
  // Should only keep confetti and @other/ as externals (not @components/)
  let externals = List.ofSeq result.externals
  Assert.True(List.exists ((=)(UMX.tag "confetti")) externals)
  Assert.True(List.exists ((=)(UMX.tag "@other/")) externals)
  Assert.False(List.exists ((=)(UMX.tag "@components/")) externals)
  // Import map should not contain @components/
  Assert.False(result.importMap.imports.ContainsKey(UMX.tag "@components/"))
  Assert.True(result.importMap.imports.ContainsKey(UMX.tag "confetti"))
  Assert.True(result.importMap.imports.ContainsKey(UMX.tag "@other/"))

[<Fact>]
let ``resolveForBuildA with esbuild present and useLocalPkgs = true with node_modules import map``
  ()
  =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let config = {
    Defaults.PerlaConfig with
        mountDirectories = mounts
        useLocalPkgs = true
        plugins = [ Constants.PerlaEsbuildPluginName ]
        paths = Map.empty // not used in this test
  }

  let importMap = {
    Perla.PkgManager.ImportMap.imports =
      [
        "@preact/signals",
        "/node_modules/@preact/signals/dist/signals.module.js"
        "htm", "/node_modules/htm/dist/htm.module.js"
        "htm/preact", "/node_modules/htm/preact/index.module.js"
        "preact", "/node_modules/preact/dist/preact.module.js"
      ]
      |> Map.ofList
    scopes =
      [
        "/node_modules/",
        [
          "@preact/signals-core",
          "/node_modules/.perla/@preact/signals-core@1.11.0/dist/signals-core.module.js"
          "preact/hooks",
          "/node_modules/.perla/preact@10.26.9/hooks/dist/hooks.module.js"
        ]
        |> Map.ofList
      ]
      |> Map.ofList
    integrity = Map.empty
  }

  let configA = AVal.constant config
  let importMapA = AVal.constant importMap
  let resultA = ImportMaps.resolveForBuildA configA importMapA
  let result = AVal.force resultA
  // Externals should match all top-level imports (current implementation)
  let expectedExternals = [ "@preact/signals"; "htm"; "htm/preact"; "preact" ]

  Assert.Equal<string list>(
    expectedExternals |> List.sort,
    result.externals |> List.sort
  )
  // Import map should match exactly
  Assert.Equal<Map<string, string>>(importMap.imports, result.importMap.imports)

  Assert.Equal<Map<string, Map<string, string>>>(
    importMap.scopes,
    result.importMap.scopes
  )

[<Fact>]
let ``resolveForBuildA with esbuild present and useLocalPkgs = false with jspm import map``
  ()
  =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let config = {
    Defaults.PerlaConfig with
        mountDirectories = mounts
        useLocalPkgs = false
        plugins = [ Constants.PerlaEsbuildPluginName ]
        paths = Map.empty // not used in this test
  }

  let importMap = {
    Perla.PkgManager.ImportMap.imports =
      [
        "@preact/signals",
        "https://ga.jspm.io/npm:@preact/signals@2.2.1/dist/signals.module.js"
        "@preact/signals-core",
        "https://ga.jspm.io/npm:@preact/signals-core@1.11.0/dist/signals-core.module.js"
      ]
      |> Map.ofList
    scopes =
      [
        "https://ga.jspm.io/",
        [
          "preact",
          "https://ga.jspm.io/npm:preact@10.26.9/dist/preact.module.js"
          "preact/hooks",
          "https://ga.jspm.io/npm:preact@10.26.9/hooks/dist/hooks.module.js"
        ]
        |> Map.ofList
      ]
      |> Map.ofList
    integrity = Map.empty
  }

  let configA = AVal.constant config
  let importMapA = AVal.constant importMap
  let resultA = ImportMaps.resolveForBuildA configA importMapA
  let result = AVal.force resultA
  // Externals should match only top-level imports (current implementation)
  let expectedExternals = [ "@preact/signals"; "@preact/signals-core" ]

  Assert.Equal<string list>(
    expectedExternals |> List.sort,
    result.externals |> List.sort
  )
  // Import map should match exactly
  Assert.Equal<Map<string, string>>(importMap.imports, result.importMap.imports)

  Assert.Equal<Map<string, Map<string, string>>>(
    importMap.scopes,
    result.importMap.scopes
  )

[<Fact>]
let ``resolveForBuildA with esbuild not present returns empty externals and keeps import map as is (except local cleanup)``
  ()
  =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let config = {
    Defaults.PerlaConfig with
        mountDirectories = mounts
        useLocalPkgs = true // doesn't matter
        plugins = [] // esbuild not present
        paths =
          Map.ofList [
            UMX.tag "@components/", UMX.tag "/src/shared/components/"
            UMX.tag "confetti", UMX.tag "https://esm.sh/confetti@1.0.0"
            UMX.tag "@other/", UMX.tag "/other/path/"
          ]
  }

  let importMap = ImportMap.Empty
  let mergedImportMap = ImportMaps.withPaths config.paths importMap
  let configA = AVal.constant config
  let importMapA = AVal.constant mergedImportMap
  let resultA = ImportMaps.resolveForBuildA configA importMapA
  let result = AVal.force resultA
  // Externals should be empty
  Assert.Empty(result.externals)
  // Import map should not contain @components/ (cleaned up), but should contain confetti and @other/
  Assert.False(result.importMap.imports.ContainsKey(UMX.tag "@components/"))
  Assert.True(result.importMap.imports.ContainsKey(UMX.tag "confetti"))
  Assert.True(result.importMap.imports.ContainsKey(UMX.tag "@other/"))

[<Fact>]
let ``resolveForBuildA with esbuild not present and only local paths returns empty externals and cleans up all local imports``
  ()
  =
  let mounts =
    Map.ofList [ UMX.tag<ServerUrl> "/src", UMX.tag<UserPath> "./src" ]

  let config = {
    Defaults.PerlaConfig with
        mountDirectories = mounts
        useLocalPkgs = false // doesn't matter
        plugins = [] // esbuild not present
        paths =
          Map.ofList [
            UMX.tag "@components/", UMX.tag "/src/shared/components/"
            UMX.tag "@foo/", UMX.tag "./src/foo/"
          ]
  }

  let importMap = ImportMap.Empty
  let mergedImportMap = ImportMaps.withPaths config.paths importMap
  let configA = AVal.constant config
  let importMapA = AVal.constant mergedImportMap
  let resultA = ImportMaps.resolveForBuildA configA importMapA
  let result = AVal.force resultA
  // Externals should be empty
  Assert.Empty(result.externals)
  // Import map should not contain @components/ or @foo/ (both cleaned up)
  Assert.False(result.importMap.imports.ContainsKey(UMX.tag "@components/"))
  Assert.False(result.importMap.imports.ContainsKey(UMX.tag "@foo/"))
