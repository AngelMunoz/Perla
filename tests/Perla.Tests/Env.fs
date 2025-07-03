namespace Perla.Tests

open System
open Xunit
open Xunit.Sdk
open Perla
open Perla.Units
open FSharp.UMX



type PlatformOps() =
    [<Fact>]
    member _.``GetEnvContent provides EnvVars with "PERLA_" prefix``() =
        let platformOps = Env.Create({
            GetEnvVars = fun () -> [("PERLA_currentEnv", "tests"); ("PERLA_IAmSet", "yes")]
            ReadFile = fun _ -> [||]
        })
        let actual = platformOps.GetEnvContent()

        match actual with
        | Some actual ->
            let expected = "export const IAmSet = \"yes\";"
            Assert.True(actual.Contains(expected))

            let expected = "export const currentEnv = \"tests\";"
            Assert.True(actual.Contains(expected))
        | None -> raise(XunitException("Content is Empty when It should have data"))

    [<Fact>]
    member _.``GetEnvContent doesn't provide EnvVars without "PERLA_" prefix``() =
        let platformOps = Env.Create({
            GetEnvVars = fun () -> [("PERLA-NotAvailable", "not-available"); ("OtherNotAvailable", "other-not-available")]
            ReadFile = fun _ -> [||]
        })
        let actual = platformOps.GetEnvContent()

        match actual with
        | Some actual ->
            let expected = "export const NotAvailable = \"not-available\";"
            Assert.False(actual.Contains(expected))

            let expected = "export const OtherNotAvailable = \"not-available\";"
            Assert.False(actual.Contains(expected))
        | None -> Assert.True(true) //This is expected

    [<Fact>]
    member _.``LoadEnvFiles loads variables correctly``() =
        let platformOps = Env.Create({
            GetEnvVars = fun () -> []
            ReadFile = fun (path: string<SystemPath>) ->
                match UMX.untag path with
                | "a.env" -> [| "PERLA_VAR1=A"; "VAR2=B" |]
                | _ -> [||]
        })

        platformOps.LoadEnvFiles([UMX.tag "a.env"])

        let var1 = Environment.GetEnvironmentVariable("PERLA_VAR1")
        let var2 = Environment.GetEnvironmentVariable("PERLA_VAR2")

        Assert.Equal("A", var1)
        Assert.Equal("B", var2)