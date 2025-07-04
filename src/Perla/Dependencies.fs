namespace Perla

open System
open System.Runtime.InteropServices
open Spectre.Console
open FSharp.UMX

open Perla.Logger
open Perla.PackageManager
open Perla.PackageManager.Types
open Perla.PackageManager.Skypack

open FsToolkit.ErrorHandling
open Perla.Types
open Perla.Units


module Dependencies =

  let printSearchTable(searchData: PackageSearchResult seq) =
    let table =
      Table()
        .AddColumn(TableColumn("Name"))
        .AddColumn(TableColumn("Description"))
        .AddColumn(TableColumn("Maintainers"))
        .AddColumn(TableColumn("Last Updated"))

    printfn "%A" searchData

    for row in searchData do
      let maintainers =
        row.maintainers
        |> Seq.truncate 3
        |> Seq.map(fun maintainer ->
          $"[yellow]{maintainer.name}[/] - [yellow]{maintainer.email}[/]")

      let maintainers = String.Join("\n", maintainers)

      table.AddRow(
        Markup($"[bold green]{row.name}[/]"),
        Markup(row.description),
        Markup($"{maintainers}..."),
        Markup(row.updatedAt.ToShortDateString())
      )
      |> ignore

    AnsiConsole.Write table

  let printShowTable(package: PackageInfo) =
    let table =
      Table()
        .AddColumn(TableColumn("Description"))
        .AddColumn(TableColumn("Is Deprecated"))
        .AddColumn(TableColumn("Dependency Count"))
        .AddColumn(TableColumn("License"))
        .AddColumn(TableColumn("Versions"))
        .AddColumn(TableColumn("Maintainers"))
        .AddColumn(TableColumn("Last Update"))

    let maintainers =
      package.maintainers
      |> Seq.truncate 5
      |> Seq.map(fun maintainer ->
        $"[yellow]{maintainer.name}[/] - [yellow]{maintainer.email}[/]")

    let maintainers = String.Join("\n", maintainers)

    let versions =
      package.distTags
      |> Map.toSeq
      |> Seq.truncate 5
      |> Seq.map(fun (name, version) ->
        $"[bold yellow]{name}[/] - [dim green]{version}[/]")

    let versions = String.Join("\n", versions)

    let deprecated =
      if package.isDeprecated then
        "[bold red]Yes[/]"
      else
        "[green]No[/]"

    let sep =
      Rule($"[bold green]{package.name}[/]").Centered().RuleStyle("bold green")

    AnsiConsole.Write sep

    table.AddRow(
      Markup(package.description),
      Markup(deprecated),
      Markup($"[bold green]%i{package.dependenciesCount}[/]"),
      Markup($"[bold yellow]{package.license}[/]"),
      Markup(versions),
      Markup(maintainers),
      Markup(package.updatedAt.ToShortDateString())
    )
    |> ignore

    AnsiConsole.Write table

  let Search(name: string, page: int) = task {
    let! results =
      Logger.spinner(
        "Searching for package information",
        Skypack.SearchPackage(name, page)
      )

    printfn "%A" results
    results.results |> printSearchTable

    Logger.log(
      $"[bold green]Found[/]: {results.meta.totalCount}",
      escape = false
    )

    Logger.log(
      $"[bold green]Page[/] {results.meta.page} of {results.meta.totalPages}",
      escape = false
    )
  }

  let Show(name: string) = task {
    let! package =
      Logger.spinner(
        "Searching for package information",
        Skypack.PackageInfo name
      )

    printShowTable package
  }

  let consolidateResolutions
    (packages: (string * string) list, newMap: ImportMap)
    : Map<string, string> =

    let unparsablePackages =
      packages
      |> List.choose(fun (name, url) ->
        match ExtractDependencyInfoFromUrl url with
        | ValueSome _ -> None
        | ValueNone -> Some(name, url))

    if unparsablePackages |> List.length > 0 then
      Logger.log
        "Unable to get standard package information from following packages:"

      Logger.log "These are likely to be manual resolutions"

      Logger.log
        "if there's an actual dependency from the supported providers here please report it as a bug"

      for name, url in unparsablePackages do
        Logger.log($"[yellow]{name}[/yellow] - {url}", escape = false)

    let allPackages =
      let unresolved = unparsablePackages

      unresolved
      |> List.fold
        (fun current (uName, uUrl) -> current |> Map.add uName uUrl)
        newMap.imports

    allPackages

type Dependencies =

  static member Add(package: string, map: ImportMap) = taskResult {
    let! resultMap =
      PackageManager.AddJspm(
        package,
        [ GeneratorEnv.Browser; GeneratorEnv.Module ],
        map,
        provider = Provider.Jspm
      )

    let packages = map.imports |> Map.toList

    let allPackages = Dependencies.consolidateResolutions(packages, resultMap)

    return { resultMap with imports = allPackages }
  }

  static member Restore(package: string) =
    PackageManager.AddJspm(
      package,
      [ GeneratorEnv.Browser; GeneratorEnv.Module ],
      provider = Provider.Jspm
    )

  static member Restore(packages: string seq) =
    PackageManager.AddJspm(
      packages,
      [ GeneratorEnv.Browser; GeneratorEnv.Module ],
      provider = Provider.Jspm
    )

  static member GetMapAndDependencies(packages: string seq) =
    PackageManager.Regenerate(
      packages,
      [ GeneratorEnv.Browser; GeneratorEnv.Module ],
      provider = Provider.Jspm
    )
    |> TaskResult.map(fun result -> result.staticDeps, result.map)

  static member GetMapAndDependencies(map: ImportMap) =
    let packages = map.imports |> Map.toList

    let parsablePackages =
      packages
      |> List.choose(
        snd >> ExtractDependencyInfoFromUrl >> Option.ofValueOption
      )
      |> List.map(fun (_, name, version) -> $"{name}@{version}")

    PackageManager.Regenerate(
      parsablePackages,
      [ GeneratorEnv.Browser; GeneratorEnv.Module ],
      importMap = map,
      provider = Provider.Jspm
    )
    |> TaskResult.map(fun result -> result.staticDeps, result.map)

  static member Remove(package: string, map: ImportMap) = taskResult {
    let packages =
      map.imports
      |> Map.filter(fun existing _ -> existing <> package)
      |> Map.toList

    let parsablePackages =
      packages
      |> List.choose(
        snd >> ExtractDependencyInfoFromUrl >> Option.ofValueOption
      )
      |> List.map(fun (_, name, version) -> $"{name}@{version}")

    let! resultMap =
      PackageManager.AddJspm(
        parsablePackages,
        [ GeneratorEnv.Browser; GeneratorEnv.Module ],
        map,
        Provider.Jspm
      )

    let allPackages = Dependencies.consolidateResolutions(packages, resultMap)

    return { resultMap with imports = allPackages }
  }

  static member SwitchProvider(map: ImportMap) = taskResult {
    let packages = map.imports |> Map.toList

    let parsablePackages =
      packages
      |> List.choose(
        snd >> ExtractDependencyInfoFromUrl >> Option.ofValueOption
      )
      |> List.map(fun (_, name, version) -> $"{name}@{version}")

    let! resultMap =
      PackageManager.AddJspm(
        parsablePackages,
        [ GeneratorEnv.Browser; GeneratorEnv.Module ],
        provider = Provider.Jspm
      )

    let allPackages = Dependencies.consolidateResolutions(packages, resultMap)

    return { resultMap with imports = allPackages }
  }

  static member LocateDependenciesFromMapAndConfig
    (importMap: ImportMap, config: PerlaConfig)
    =
    let devDependencies = Set.empty

    let dependencies = config.dependencies |> Seq.map(fun f -> f.package) |> set

    let allTogether = set dependencies |> Set.union(set devDependencies)

    let fromImportMap =
      importMap.imports
      |> Map.toList
      |> List.choose(fun (name, url) ->
        match ExtractDependencyInfoFromUrl url with
        | ValueSome value ->
          if allTogether |> Set.contains name then
            Some value
          else
            None
        | ValueNone -> None)
      |> List.map(fun (_, name, version) -> {
        package = name
        version = UMX.tag<Semver> version
      })
      |> Set.ofList

    fromImportMap, Set.empty<PkgDependency>
