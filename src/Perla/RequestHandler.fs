namespace Perla.RequestHandler

open System.IO
open System.IO.Compression
open System.Formats.Tar
open Microsoft.Extensions.Logging
open Perla.Units
open Perla
open FsHttp
open IcedTasks
open FSharp.UMX

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
  let private downloadEsbuild
    (logger: ILogger)
    (env: PlatformOps)
    (dirs: PerlaDirectories)
    (version: string<Semver>)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()

      let esbuildVersion = UMX.untag version

      let binString = $"{env.PlatformString()}-{env.ArchString()}"

      let url =
        $"https://registry.npmjs.org/@esbuild/{binString}/-/{binString}-{esbuildVersion}.tgz"

      let extractDir =
        Path.Combine(UMX.untag dirs.PerlaArtifactsRoot, UMX.untag version)

      let dir = DirectoryInfo(extractDir)
      dir.Create()


      try
        let! req =
          get url |> Config.cancellationToken token |> Request.sendTAsync

        use! stream = req |> Response.toStreamTAsync token
        use source = new GZipStream(stream, CompressionMode.Decompress)

        do!
          TarFile.ExtractToDirectoryAsync(
            source,
            UMX.untag extractDir,
            true,
            token
          )
      with ex ->
        logger.LogWarning("Failed to extract esbuild from {url}", url, ex)
        return ()

    }

  let private downloadTemplate
    (user: string)
    (repository: string<Repository>)
    (branch: string<Branch>)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()

      let! req =
        get
          $"https://github.com/{user}/{repository}/archive/refs/heads/{branch}.zip"
        |> Config.cancellationToken token
        |> Request.sendTAsync

      return! req |> Response.toStreamTAsync token
    }

  let Create(args: RequestHandlerArgs) : RequestHandler =
    { new RequestHandler with
        member _.DownloadEsbuild version =
          downloadEsbuild
            args.Logger
            args.PlatformOps
            args.PerlaDirectories
            version

        member _.DownloadTemplate(user, repository, branch) =
          downloadTemplate user repository branch

        member _.PingTestServer url = cancellableTask {
          let! token = CancellableTask.getCancellationToken()

          let! res =
            get $"{url}/~perla~/ping"
            |> Config.cancellationToken token
            |> Request.sendTAsync

          return res.statusCode = System.Net.HttpStatusCode.OK
        }
    }
