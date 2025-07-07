namespace Perla.Tests

open Xunit
open System.Collections.Generic
open System.CommandLine
open System.Threading
open System.Threading.Tasks

open FSharp.UMX

open LiteDB

open Perla
open Perla.Warmup
open Perla.Warmup.Middleware.MiddlewareImpl
open FsToolkit.ErrorHandling


module private MiddlewareUtils =

  let getEsbuildEnv
    (flags:
      {|
        absentInConfig: bool
        savedInDB: bool
        isBinPresent: bool
        throwInSetup: bool
      |})
    : Warmup.Esbuild =
    { new Perla.Warmup.Esbuild with
        member _.EsbuildVersion: FSharp.UMX.string<Units.Semver> =
          UMX.tag "0.0.0"

        member _.IsEsbuildPluginAbsent: bool = flags.absentInConfig

        member _.IsEsbuildPresent
          (arg1: FSharp.UMX.string<Units.Semver>)
          : bool =
          flags.isBinPresent

        member _.RunSetupEsbuild
          (arg1: FSharp.UMX.string<Units.Semver>, arg2: CancellationToken)
          : Task<unit> =
          if flags.throwInSetup then
            raise(exn "Error in setup")
          else
            Task.FromResult(())

        member _.SaveEsbuildPresent
          (arg1: FSharp.UMX.string<Units.Semver>)
          : ObjectId =
          if flags.savedInDB then ObjectId.NewObjectId() else null
    }

  let getSetup
    (flags:
      {|
        isSetup: bool
        exitCode: int
        savedInDb: bool
      |})
    : Setup =
    { new Setup with
        member _.IsAlreadySetUp() : bool = flags.isSetup

        member _.RunSetup(arg1: bool, arg2: CancellationToken) : Task<int> =
          Task.FromResult(flags.exitCode)

        member _.SaveSetup() : ObjectId =
          if flags.savedInDb then ObjectId.NewObjectId() else null
    }

  let getTemplatesEnv
    (flags:
      {|
        exitCode: int
        templatesExist: bool
        savedInDB: bool
        throwInSetup: bool
      |})
    =
    { new Templates with
        member _.RunSetupTemplates
          (arg1: Handlers.TemplateRepositoryOptions, arg2: CancellationToken)
          : Task<int> =
          if flags.throwInSetup then
            raise(exn "Error in setup")
          else
            Task.FromResult(flags.exitCode)

        member _.SaveTemplatesArePresent() : ObjectId =
          if flags.savedInDB then ObjectId.NewObjectId() else null

        member _.TemplatesArePresent() : bool = flags.templatesExist
    }

  let getFableEnv
    (flags:
      {|
        isFableInConfig: bool
        isFablePresent: bool
        failRestore: bool
      |})
    =
    { new Fable with
        member _.IsFableInConfig: bool = flags.isFableInConfig

        member _.IsFablePresent(arg1: CancellationToken) : Task<bool> =
          flags.isFablePresent |> Task.FromResult

        member _.RestoreFable
          (arg1: CancellationToken)
          : Task<Result<unit, string>> =
          if flags.failRestore then
            Task.FromResult(Error "Error in restore")
          else
            Task.FromResult(Ok())
    }

module CliMiddleware =

  [<Fact>]
  let ``PerlaCliMiddleware.ShouldRunFor can run for command in list``() =
    let candidate = "new"
    let commands = [ "new"; "build" ]
    let result = ShouldRunFor candidate commands
    Result.isOk result |> Assert.True

  [<Fact>]
  let ``PerlaCliMiddleware.ShouldRunFor fails with "Continue"``() =
    let candidate = "serve"
    let commands = [ "new"; "build" ]
    let result = ShouldRunFor candidate commands

    Result.isError result |> Assert.True

    result
    |> Result.teeError(fun err ->
      Assert.Equal(Middleware.PerlaMdResult.Continue, err))

  [<Fact>]
  let ``PerlaCliMiddleware.HasDirective can find directive``() =
    let directive = Constants.CliDirectives.Preview

    let hasDirective: KeyValuePair<string, string seq> seq = [
      KeyValuePair(Constants.CliDirectives.Preview, [])
    ]

    let doesntHaveDirective: KeyValuePair<string, string seq> seq = [
      KeyValuePair(Constants.CliDirectives.CiRun, [])
    ]

    HasDirective directive hasDirective |> Assert.True
    HasDirective directive doesntHaveDirective |> Assert.False

  [<Fact>]
  let ``MiddlewareImpl.previewCheck stops if command is in preview and directive is not present``
    ()
    =
    task {
      let command =
        Command("in-preview", "This command is in preview", IsHidden = true)

      let directives: KeyValuePair<string, string seq> seq = []
      let! result = previewCheck(command, directives)


      result
      |> Result.tee(fun _ -> Assert.Fail "Result should not be Ok")
      |> Result.teeError(fun err -> Assert.Equal((Middleware.Exit 1), err))
      |> ignore

      return ()
    }

  [<Fact>]
  let ``MiddlewareImpl.previewCheck continues if command is in preview and directive is present``
    ()
    =
    task {
      let command =
        Command("in-preview", "This command is in preview", IsHidden = true)

      let directives: KeyValuePair<string, string seq> seq = [
        KeyValuePair(Constants.CliDirectives.Preview, [])
      ]

      let! result = previewCheck(command, directives)

      result
      |> Result.teeError(fun err ->
        Assert.Fail $"Result should not be Error: %A{err}")
      |> ignore

      return ()
    }

  [<Fact>]
  let ``MiddlewareImpl.previewCheck continues if command is not in preview``() = task {
    let command =
      Command("in-preview", "This command is in preview", IsHidden = false)

    let directives: KeyValuePair<string, string seq> seq = []

    let! result = previewCheck(command, directives)

    match result with
    | Ok() -> ()
    | Error(Middleware.Exit _) -> Assert.Fail "The result should not be an exit"
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.esbuildPluginCheck stops if plugin is not present and directive is not present``
    ()
    =
    task {
      let esbuild =
        MiddlewareUtils.getEsbuildEnv {|
          absentInConfig = true
          savedInDB = true
          isBinPresent = true
          throwInSetup = false
        |}

      let command = Command("serve", "This command requires esbuild")
      let directives: KeyValuePair<string, string seq> seq = []

      let! result = esbuildPluginCheck esbuild (command, directives)

      match result with
      | Ok() -> Assert.Fail "Result should not be Ok"
      | Error(Middleware.Exit _) -> ()
      | Error Middleware.Continue ->
        Assert.Fail "Result should not be a continuation"

      return ()
    }

  [<Fact>]
  let ``MiddlewareImpl.esbuildPluginCheck continues if plugin is not present and directive is present``
    ()
    =
    task {
      let esbuild =
        MiddlewareUtils.getEsbuildEnv {|
          absentInConfig = true
          savedInDB = true
          isBinPresent = true
          throwInSetup = false
        |}

      let command = Command("serve", "This command requires esbuild")

      let directives: KeyValuePair<string, string seq> seq = [
        KeyValuePair(Constants.CliDirectives.NoEsbuildPlugin, [])
      ]

      let! result = esbuildPluginCheck esbuild (command, directives)

      match result with
      | Ok() -> ()
      | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
      | Error Middleware.Continue ->
        Assert.Fail "Result should not be a continuation"

      return ()
    }

  [<Fact>]
  let ``MiddlewareImpl.setupCheck continues if setup check is present``() = task {
    let command = Command("build", "This command requires setup")
    let token = CancellationToken.None
    let directives: KeyValuePair<string, string seq> seq = []

    let setup =
      MiddlewareUtils.getSetup {|
        isSetup = true
        exitCode = 0
        savedInDb = true
      |}

    let! result = setupCheck setup (command, directives, token)

    match result with
    | Ok() -> Assert.Fail "Result should not be Ok"
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue -> ()

    return ()
  }


  [<Fact>]
  let ``MiddlewareImpl.setupCheck stops if setup check is not present``() = task {
    let command = Command("build", "This command requires setup")
    let token = CancellationToken.None
    let directives: KeyValuePair<string, string seq> seq = []

    let setup =
      MiddlewareUtils.getSetup {|
        isSetup = false
        exitCode = 0
        savedInDb = true
      |}

    let! result = setupCheck setup (command, directives, token)

    match result with
    | Ok() -> ()
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.setupCheck exits with handler's exit code``() = task {
    let command = Command("build", "This command requires setup")
    let token = CancellationToken.None
    let directives: KeyValuePair<string, string seq> seq = []
    let expectedCode = 500

    let setup =
      MiddlewareUtils.getSetup {|
        isSetup = false
        exitCode = expectedCode
        savedInDb = true
      |}

    let! result = setupCheck setup (command, directives, token)

    match result with
    | Ok() -> Assert.Fail "Result should not be Ok"
    | Error(Middleware.Exit code) -> Assert.Equal(expectedCode, code)
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.esbuildBinCheck continues if esbuild bin is present``() = task {
    let esbuild =
      MiddlewareUtils.getEsbuildEnv {|
        absentInConfig = false
        savedInDB = true
        isBinPresent = true
        throwInSetup = false
      |}

    let command = Command("build", "This command requires esbuild")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None

    let! result = esbuildBinCheck esbuild (command, directives, token)

    match result with
    | Ok() -> ()
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }


  [<Fact>]
  let ``MiddlewareImpl.esbuildBinCheck stops if esbuild bin is not present``() = task {
    let esbuild =
      MiddlewareUtils.getEsbuildEnv {|
        isBinPresent = false
        throwInSetup = false
        absentInConfig = false
        savedInDB = true
      |}

    let command = Command("build", "This command requires esbuild")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None

    let! result = esbuildBinCheck esbuild (command, directives, token)

    match result with
    | Ok() -> ()
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.esbuildBinCheck exits if setup fails``() = task {
    let esbuild =
      MiddlewareUtils.getEsbuildEnv {|
        isBinPresent = false
        throwInSetup = true
        absentInConfig = false
        savedInDB = true
      |}

    let command = Command("build", "This command requires esbuild")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None

    let! result = esbuildBinCheck esbuild (command, directives, token)

    match result with
    | Ok() -> Assert.Fail "Result should not be Ok"
    | Error(Middleware.Exit code) -> Assert.Equal(1, code)
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.esbuildBinCheck continues if check save fails``() = task {
    let esbuild =
      MiddlewareUtils.getEsbuildEnv {|
        isBinPresent = false
        throwInSetup = false
        absentInConfig = false
        savedInDB = false
      |}

    let command = Command("build", "This command requires esbuild")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None

    let! result = esbuildBinCheck esbuild (command, directives, token)

    match result with
    | Ok() -> Assert.Fail "Result should not be Ok"
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue -> ()

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.templatesCheck continues if templates are present``() = task {
    let templates =
      MiddlewareUtils.getTemplatesEnv {|
        exitCode = 0
        templatesExist = true
        savedInDB = true
        throwInSetup = false
      |}

    let command = Command("new", "This command requires templates")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None
    let! result = templatesCheck templates (command, directives, token)

    match result with
    | Ok() -> Assert.Fail "Result should not be Ok"
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue -> ()
  }

  [<Fact>]
  let ``MiddlewareImpl.templatesCheck stops if templates are not present``() = task {
    let templates =
      MiddlewareUtils.getTemplatesEnv {|
        exitCode = 0
        templatesExist = false
        savedInDB = true
        throwInSetup = false
      |}

    let command = Command("new", "This command requires templates")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None
    let! result = templatesCheck templates (command, directives, token)

    match result with
    | Ok() -> ()
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.templatesCheck exits with handler's exit code``() = task {
    let expectedExit = 100

    let templates =
      MiddlewareUtils.getTemplatesEnv {|
        exitCode = expectedExit
        templatesExist = false
        savedInDB = true
        throwInSetup = false
      |}

    let command = Command("new", "This command requires templates")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None
    let! result = templatesCheck templates (command, directives, token)

    match result with
    | Ok() -> Assert.Fail "Result should not be Ok"
    | Error(Middleware.Exit exitCode) -> Assert.Equal(expectedExit, exitCode)
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.fableCheck stops if fable is not present``() = task {
    let fable =
      MiddlewareUtils.getFableEnv {|
        isFableInConfig = true
        isFablePresent = false
        failRestore = false
      |}

    let command = Command("build", "This command requires fable")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None
    let! result = fableCheck fable (command, directives, token)

    match result with
    | Ok() -> ()
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue ->
      Assert.Fail "Result should not be a continuation"

    return ()
  }

  [<Fact>]
  let ``MiddlewareImpl.fableCheck continues if fable is present``() = task {
    let fable =
      MiddlewareUtils.getFableEnv {|
        isFableInConfig = true
        isFablePresent = true
        failRestore = false
      |}

    let command = Command("build", "This command requires fable")
    let directives: KeyValuePair<string, string seq> seq = []
    let token = CancellationToken.None
    let! result = fableCheck fable (command, directives, token)

    match result with
    | Ok() -> Assert.Fail "Result should not be Ok"
    | Error(Middleware.Exit _) -> Assert.Fail "Result should not be an exit"
    | Error Middleware.Continue -> ()

    return ()
  }
