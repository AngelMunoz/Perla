module Tests.App

open System
open System.Collections.Generic

open Fable.Core
open Tests.QUnit

open Translations

open Types

[<AttachMembers>]
type CustomObservable() =

  let observers = HashSet<IObserver<_>>()

  member _.Broadcast(value) =
    for observer in observers do
      try
        observer.OnNext(value)
      with ex ->
        observer.OnError(ex)


  member _.Complete() =
    for observer in observers do
      try
        observer.OnCompleted()
      with ex ->
        observer.OnError(ex)

    observers.Clear()

  interface IObservable<TranslationCollection option * Language> with
    member _.Subscribe(observer: IObserver<_>) =
      observers.Add observer |> ignore

      { new IDisposable with
          member _.Dispose() =
            if observers.Contains(observer) then
              observers.Remove(observer) |> ignore
      }

let tests = [
  Tests.test
    "matchTranslationLanguage with None should not bring anything"
    (fun assert' ->
      let actual = matchTranslationLanguage(None, Language.FromString("es-mx"))
      Expect.equal actual None "should return None" assert')

  Tests.test
    "getTranslationValue to not find anything in a None map"
    (fun assert' ->
      let actual = getTranslationValue "I don't exist" None
      Expect.equal actual None "should return None" assert')

  Tests.test "T can give default values" (fun assert' ->
    let obs = CustomObservable()
    let values = HashSet<_>()

    let stream = T obs ("lastName", "Vorname")

    let sub =
      stream |> Observable.subscribe(fun next -> values.Add(next) |> ignore)

    let mx = {|
      ``es-mx`` = {| lastName = "Apellido" |}
    |}

    let fr = {|
      ``fr-fr`` = {| lastName = "Nom de famille" |}
    |}

    let us = {|
      ``en-us`` = {| lastName = "Last Name" |}
    |}

    obs.Broadcast(None, DeDe)
    obs.Broadcast(Some(mx |> box |> unbox), EsMx)
    obs.Broadcast(Some(fr |> box |> unbox), Unknown "fr-fr")
    obs.Broadcast(Some(us |> box |> unbox), EnUs)

    sub.Dispose()
    Expect.isTrue (values.Contains("Vorname")) "should contain Vorname" assert'

    Expect.isTrue
      (values.Contains("Last Name"))
      "should contain Last Name"
      assert'

    Expect.isTrue
      (values.Contains("Apellido"))
      "should contain Apellido"
      assert'

    Expect.isTrue
      (values.Contains("Nom de famille"))
      "should contain Nom de famille"
      assert')
]

Tests.testList "F# Test File Translations" tests
