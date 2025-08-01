import { expect } from "@esm-bundle/chai";
import {
  matchTranslationLanguage,
  getTranslationValue,
  T,
} from "/fable_output/Translations.js";
import { Language } from "/fable_output/Types.js";

class CustomObservable {
  _observers = new Set();

  Subscribe(observer) {
    this._observers.add(observer);
    return {
      Dispose: () => {
        if (this._observers.has(observer)) this._observers.delete(observer);
      },
    };
  }

  Broadcast(value) {
    for (const obs of this._observers) {
      try {
        obs.OnNext(value);
      } catch (err) {
        obs.OnError(err);
      }
    }
    return this;
  }

  Complete() {
    for (const obs of this._observers) {
      try {
        obs.OnCompleted();
      } catch (err) {
        obs.OnError(err);
      } finally {
        obs.OnComplete();
      }
    }
    this._observers.clear();
  }
}

describe("Translations", () => {
  it("matchTranslationLanguage with None should not bring anything", () => {
    const actual = matchTranslationLanguage(null, Language.FromString("es-mx"));
    expect(actual).to.not.exist;
  });

  it("getTranslationValue to not find anything in a None map", () => {
    const actual = getTranslationValue("I don't exist", null);
    expect(actual).to.not.exist;
  });

  it("T can give default values", () => {
    const obs = new CustomObservable();
    const stream = T(obs, "lastName", "Vorname");
    const values = new Set();
    const sub = stream.Subscribe({
      OnNext(value) {
        values.add(value);
      },
      OnCompleted() {},
      OnError(err) {},
    });
    obs
      .Broadcast([
        { "es-mx": { lastName: "Apellido" } },
        Language.FromString("es-mx"),
      ])
      .Broadcast([null, Language.FromString("de-de")])
      .Broadcast([
        { "fr-fr": { lastName: "Nom de famille" } },
        Language.FromString("fr-fr"),
      ])
      .Broadcast([
        { "en-us": { lastName: "Last Name" } },
        Language.FromString("en-us"),
      ]);

    sub.Dispose();
    expect(values).to.include("Vorname");
    expect(values).to.include("Last Name");
    expect(values).to.include("Apellido");
    expect(values).to.include("Nom de famille");
    obs.Complete();
  });
});
