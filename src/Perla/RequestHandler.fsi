namespace Perla.RequestHandler

open System.IO
open Microsoft.Extensions.Logging
open IcedTasks
open FSharp.UMX
open Perla
open Perla.Units

[<Interface>]
type RequestHandler =
  abstract DownloadEsbuild: version: string<Semver> -> CancellableTask<unit>

  abstract DownloadTemplate:
    user: string * repository: string<Repository> * branch: string<Branch> ->
      CancellableTask<Stream>

  abstract PingTestServer: url: string -> CancellableTask<bool>

type RequestHandlerArgs = {
  Logger: ILogger
  PlatformOps: PlatformOps
  PerlaDirectories: PerlaDirectories
}

module RequestHandler =
  val Create: RequestHandlerArgs -> RequestHandler
