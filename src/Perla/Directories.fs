namespace Perla

open System
open System.IO
open FsToolkit.ErrorHandling
open FSharp.UMX
open Perla
open Perla.Units

[<Interface>]
type PerlaDirectories =
  abstract AssemblyRoot: string<SystemPath> with get
  abstract PerlaArtifactsRoot: string<SystemPath> with get
  abstract Database: string<SystemPath> with get
  abstract Templates: string<SystemPath> with get
  abstract OfflineTemplates: string<SystemPath> with get
  abstract PerlaConfigPath: string<SystemPath> with get
  abstract OriginalCwd: string<SystemPath> with get
  abstract CurrentWorkingDirectory: string<SystemPath> with get
  abstract SetCwdToProject: ?fromPath: string<SystemPath> -> unit

[<AutoOpen>]
module Operators =
  let inline (/) a b = Path.Combine(a, b)

  let inline (|/) (a: string<SystemPath>) (b: string) =
    Path.Combine(UMX.untag a, UMX.untag b) |> UMX.tag<SystemPath>

[<RequireQualifiedAccess>]
module PerlaDirectories =
  [<TailCall>]
  let rec private findConfig filename (directory: DirectoryInfo | null) =
    match directory with
    | null -> None
    | directory ->
      let found =
        directory.GetFiles(filename, SearchOption.TopDirectoryOnly)
        |> Array.tryHead

      match found with
      | Some found -> Some found
      | None -> findConfig filename directory.Parent

  let Create() : PerlaDirectories =

    let findPerlaConfig = findConfig "perla.json"

    let originalcwd = Directory.GetCurrentDirectory() |> UMX.tag<SystemPath>

    { new PerlaDirectories with
        member _.AssemblyRoot = UMX.tag<SystemPath> AppContext.BaseDirectory

        member _.CurrentWorkingDirectory =
          UMX.tag<SystemPath>(Directory.GetCurrentDirectory())

        member _.PerlaArtifactsRoot =
          Environment.GetFolderPath
            Environment.SpecialFolder.LocalApplicationData
          / Constants.ArtifactsDirectoryname
          |> UMX.tag<SystemPath>

        member this.Database =
          this.PerlaArtifactsRoot |/ Constants.TemplatesDatabase

        member this.Templates =
          this.PerlaArtifactsRoot |/ Constants.TemplatesDirectory

        member this.OfflineTemplates =
          this.AssemblyRoot |/ Constants.OfflineTemplatesDirectory

        member this.PerlaConfigPath =
          let cwd = DirectoryInfo(UMX.untag this.CurrentWorkingDirectory)

          findPerlaConfig cwd
          |> Option.defaultWith(fun () -> FileInfo(cwd.FullName / "perla.json"))
          |> _.FullName
          |> UMX.tag<SystemPath>

        member _.OriginalCwd = originalcwd

        member this.SetCwdToProject(?fromPath) =
          let path =
            option {
              let! path = fromPath
              let! file = findPerlaConfig(DirectoryInfo(UMX.untag path))
              return file.FullName
            }
            |> function
              | Some path -> path
              | None -> UMX.untag this.OriginalCwd

          Directory.SetCurrentDirectory path
    }
