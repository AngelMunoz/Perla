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

//@ts-check

const worker = new Worker("/~perla~/worker.js");
worker.postMessage({ event: "connect" });

function replaceCssContent(data) {
  if (data.target === "style" && data.url) {
    const style = document.querySelector(`style[url="${data.url}"]`);
    if (!style) return;
    style.innerHTML = data.content?.replace(/(?:\r\n|\r|\n)/g, "\n") || "";
    style.setAttribute("url", data.url);
    return;
  }
  if (data.target === "link" && data.href) {
    const links = Array.from(
      document.querySelectorAll('link[rel="stylesheet"]')
    );
    const match = links.find((link) => {
      const raw = link.getAttribute("href");
      if (!raw) return false;
      const a = document.createElement("a");
      a.href = raw;
      const resolved = a.pathname;
      return raw === data.href || resolved === data.href;
    });
    if (!match) return;
    const newHref = data.href.split("?")[0] + "?t=" + Date.now();
    match.setAttribute("href", newHref);
    return;
  }
  // Fallback: reload if neither found
  console.warn("Unable to find style or link for", data);
  console.warn("Reloading in 1.5s...");
  setTimeout(() => {
    window.location.reload();
  }, 1500);
}

function showOverlay({ error }) {
  console.log("show overlay", error);
}

worker.addEventListener("message", function ({ data }) {
  switch (data?.event) {
    case "reload":
      return window.location.reload();
    case "replace-css":
      return replaceCssContent(data);
    case "compile-err":
      return showOverlay(data);
    default:
      return console.log("Unknown message:", data);
  }
});
