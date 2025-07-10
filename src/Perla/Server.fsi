namespace Perla.Server

open System
open System.Runtime.InteropServices
open Microsoft.AspNetCore.Builder
open Perla.Types
open Perla.Plugins
open Perla.VirtualFs
open System.Reactive.Subjects
open Perla.FileSystem
open FSharp.Data.Adaptive

[<Class>]
type Server =
  static member GetServerApp:
    config: PerlaConfig aval *
    vfs: VirtualFileSystem *
    fileChangedEvents: IObservable<FileChangedEvent> *
    compileErrorEvents: IObservable<string option> *
    fsManager: PerlaFsManager ->
      WebApplication

  static member GetTestingApp:
    config: PerlaConfig aval *
    vfs: VirtualFileSystem *
    dependencies: Perla.PkgManager.ImportMap aval *
    testEvents: ISubject<TestEvent> *
    fileChangedEvents: IObservable<FileChangedEvent> *
    compileErrorEvents: IObservable<string option> *
    fsManager: PerlaFsManager *
    [<Optional>] ?fileGlobs: string seq *
    [<Optional>] ?mochaOptions: Map<string, obj> ->
      WebApplication

  static member GetStaticServer: config: PerlaConfig aval -> WebApplication
