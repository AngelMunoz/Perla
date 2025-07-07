namespace Perla


type PlatformOps =
  abstract member IsWindows: unit -> bool
  abstract member PlatformString: unit -> string
  abstract member ArchString: unit -> string

module PlatformOps =
  val Create: unit -> PlatformOps
