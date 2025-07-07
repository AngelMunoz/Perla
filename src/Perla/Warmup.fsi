namespace Perla.Warmup


open System.Collections.Generic
open System.CommandLine
open System.CommandLine.Invocation
open System.Threading
open System.Threading.Tasks

open LiteDB

open FSharp.UMX

open Perla.Units

open Microsoft.Extensions.Logging
open IcedTasks
open FSharp.UMX

open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open Perla
open Perla.Types
open Perla.Database
open Perla.FileSystem

type RecoverableAssets =
  | Esbuild
  | Templates
  | Fable

type MiddlewareResult =
  | Continue
  | Recover of RecoverableAssets Set
  | HardExit

[<RequireQualifiedAccess>]
module Check =

  val EsbuildPlugin:
    config: PerlaConfig aval * logger: ILogger -> Result<unit, MiddlewareResult>

  val Setup:
    db: PerlaDatabase * config: PerlaConfig aval * fable: Fable.FableService ->
      CancellableTaskResult<unit, MiddlewareResult>

  val Templates:
    db: PerlaDatabase * logger: ILogger -> Result<unit, MiddlewareResult>

  val Fable:
    fable: Fable.FableService * logger: ILogger ->
      CancellableTask<Result<unit, MiddlewareResult>>

module Recover =
  val From:
    config: PerlaConfig aval *
    db: PerlaDatabase *
    pfsm: PerlaFsManager *
    logger: ILogger ->
      recoverFrom: RecoverableAssets seq ->
        CancellableTaskResult<unit, MiddlewareResult>
