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

QUnit.module("Translations", function () {
  QUnit.test(
    "matchTranslationLanguage with None should not bring anything",
    function (assert) {
      const actual = matchTranslationLanguage(
        null,
        Language.FromString("es-mx")
      );
      assert.notOk(actual);
    }
  );

  QUnit.test(
    "getTranslationValue to not find anything in a None map",
    function (assert) {
      const actual = getTranslationValue("I don't exist", null);
      assert.notOk(actual);
    }
  );

  QUnit.test("T can give default values", function (assert) {
    const obs = new CustomObservable();
    const stream = T(obs, "lastName", "Vorname");
    const sub = stream.Subscribe({
      OnNext(value) {
        assert.step(value);
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
    assert.verifySteps(["Apellido", "Vorname", "Nom de famille", "Last Name"]);
  });
});
