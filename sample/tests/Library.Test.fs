module Tests.App

open System
open System.Collections.Generic

open Fable.Core
open Tests.TestingUtils

open Translations

open Types

open type Testing

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

Describe(
  "Translations",
  (fun () ->
    It(
      "matchTranslationLanguage with None should not bring anything",
      fun () ->
        let actual =
          matchTranslationLanguage(None, Language.FromString("es-mx"))

        expect(actual).``to``.``not``.exist |> ignore
    )

    It(
      "getTranslationValue to not find anything in a None map",
      fun () ->
        let actual = getTranslationValue "I don't exist" None

        expect(actual).``to``.``not``.exist |> ignore
    )

    It(
      "T can give default values",
      fun () ->
        let obs = CustomObservable()
        let values = HashSet<_>()

        let stream = T obs ("lastName", "Vorname")

        let sub =
          stream
          |> Observable.subscribe(fun next -> values.Add(next) |> ignore)

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
        expect(values).``to``.``include``("Vorname")
        expect(values).``to``.``include``("Last Name")
        expect(values).``to``.``include``("Apellido")
        expect(values).``to``.``include``("Nom de famille")
        obs.Complete()
    ))
)
