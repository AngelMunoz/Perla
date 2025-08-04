export const PERLA_SESSION_START = Symbol("__perla-session-start");
export const PERLA_SUITE_START = Symbol("__perla-suite-start");
export const PERLA_SUITE_END = Symbol("__perla-suite-end");
export const PERLA_TEST_PASS = Symbol("__perla-test-pass");
export const PERLA_TEST_FAILED = Symbol("__perla-test-failed");
export const PERLA_SESSION_END = Symbol("__perla-session-end");
export const PERLA_TEST_IMPORT_FAILED = Symbol("__perla-test-import-failed");
export const PERLA_TEST_RUN_FINISHED = Symbol("__perla-test-run-finished");

/**
 * @param { PERLA_SESSION_START
 * | PERLA_SUITE_START
 * | PERLA_SUITE_END
 * | PERLA_TEST_PASS
 * | PERLA_TEST_FAILED
 * | PERLA_SESSION_END
 * | PERLA_TEST_IMPORT_FAILED
 * | PERLA_TEST_RUN_FINISHED } event
 * @param {{ stats?: object; test?: object; suite?: object, message?: string, stack?: string }} payload
 * @returns {Promise<void>}
 */
export async function postEvent(event, runId, payload) {
  const browser = document
    .querySelector("meta[perla-browser=true]")
    ?.getAttribute("browser");
  try {
    await fetch("/~perla~/testing/events", {
      method: "POST",
      body: JSON.stringify({
        event: event.description,
        runId,
        browser,
        ...payload,
      }),
    }).then((res) => (!res.ok ? Promise.reject(res.status) : undefined));
    console.debug(event.description);
  } catch (err) {
    console.error(
      "Failed to notify events to the dev server, this is likely a bug in Perla",
      err
    );
  }
}

export async function getFileList() {
  try {
    const result = await fetch("/~perla~/testing/files").then((res) =>
      res.ok ? res.json() : Promise.reject(res.status)
    );
    return result ?? [];
  } catch (err) {
    console.error(
      "Failed to request the testing settings to the dev server, this is likely a bug in Perla",
      err
    );
    return [];
  }
}

export async function getFrameworkOptions() {
  try {
    const result = await fetch("/~perla~/testing/framework-options").then(
      (res) => (res.ok ? res.json() : Promise.reject(res.status))
    );
    return result ?? {};
  } catch (err) {
    console.error(
      "Failed to request the testing settings to the dev server, this is likely a bug in Perla",
      err
    );
  }
}

export async function getPerlaTestEnv() {
  try {
    const result = await fetch("/~perla~/testing/environment").then((res) =>
      res.ok ? res.json() : Promise.reject(res.status)
    );
    return result ?? {};
  } catch (err) {
    console.error(
      "Failed to request the testing settings to the dev server, this is likely a bug in Perla",
      err
    );
  }
}

/**
 * @typedef {Window & { __perlaClientLogForwarding?: boolean }} PerlaWindow
 */
(function () {
  var win = window;
  if (win.__perlaClientLogForwarding) return;
  win.__perlaClientLogForwarding = true;

  function sendLogToServer(payload) {
    try {
      fetch("/~perla~/log-client-error", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify([payload]),
        keepalive: true,
      });
    } catch (e) {
      // fail silently
    }
  }

  window.addEventListener("error", function (event) {
    if (
      event &&
      event.message &&
      event.message.includes("Failed to resolve module specifier")
    ) {
      sendLogToServer({
        level: "error",
        message: event.message,
        url: window.location.href,
        userAgent: navigator.userAgent,
        timestamp: new Date().toISOString(),
        stack: event.error && event.error.stack ? event.error.stack : undefined,
        extra: {},
      });
    }
  });
})();
