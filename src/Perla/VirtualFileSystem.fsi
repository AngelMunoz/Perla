namespace Perla.VirtualFs

open System

open Microsoft.Extensions.Logging

open FSharp.UMX
open FSharp.Control

open Perla.Units
open Perla.Extensibility
open Perla.Plugins

type MountedDirectories = Map<string<ServerUrl>, string<UserPath>>
type ApplyPluginsFn = FileTransform -> Async<FileTransform>

[<Struct>]
type ChangeKind =
  | Created
  | Deleted
  | Renamed
  | Changed

type FileChangedEvent = {
  serverPath: string<ServerUrl>
  userPath: string<UserPath>
  oldPath: string<SystemPath> option
  oldName: string<SystemPath> option
  changeType: ChangeKind
  path: string<SystemPath>
  name: string<SystemPath>
}

type FileContent = {
  filename: string
  mimetype: string
  content: string
  source: string<SystemPath>
}

type BinaryFileInfo = {
  filename: string
  mimetype: string
  source: string<SystemPath>
}

type FileKind =
  | TextFile of FileContent
  | BinaryFile of BinaryFileInfo

module FileKind =
  val source: FileKind -> string<SystemPath>
  val filename: FileKind -> string
  val mimetype: FileKind -> string

type VirtualFileEntry = {
  kind: FileKind
  lastModified: DateTime
}

type VirtualFileSystem =
  inherit IDisposable
  abstract member Resolve: string<ServerUrl> -> FileKind option
  abstract member Load: MountedDirectories -> Async<unit>

  abstract member ToDisk:
    ?location: string<SystemPath> -> Async<string<SystemPath>>

  abstract member FileChanges: IObservable<FileChangedEvent>

type VirtualFileSystemArgs = {
  Extensibility: ExtensibilityService
  Logger: ILogger
}

[<RequireQualifiedAccess>]
module VirtualFs =

  val Create: VirtualFileSystemArgs -> VirtualFileSystem
