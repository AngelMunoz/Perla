import { registerRoute } from "https://esm.sh/workbox-routing@7.3.0";
import { StaleWhileRevalidate, CacheFirst } from "https://esm.sh/workbox-strategies@7.3.0";
import { setCacheNameDetails } from "https://esm.sh/workbox-core@7.3.0";

// Helper functions
const pathEndsWith = (url, ext) => url?.pathname?.endsWith(ext);
const hostContains = (url, ext) => url?.host?.includes(ext);
const isGet = (request) => request.method === "GET";
const isIndex = (url) =>
  url?.pathname === "/" ||
  url?.pathname === "" ||
  url?.pathname.endsWith("index.html");

// Set cache name details (optional)
setCacheNameDetails({
  prefix: "perla",
  suffix: "v1",
  precache: "precache",
  runtime: "runtime",
});

// Cache images with CacheFirst strategy
registerRoute(
  ({ request, url }) => isGet(request) && request.destination === "image",
  new CacheFirst({
    cacheName: "images",
  })
);

// Cache HTML files with StaleWhileRevalidate strategy
registerRoute(
  ({ request, url }) =>
    isGet(request) && !isIndex(url) && pathEndsWith(url, ".html"),
  new StaleWhileRevalidate({
    cacheName: "markdown",
  })
);

// Cache local scripts with StaleWhileRevalidate strategy
registerRoute(
  ({ request, url }) =>
    isGet(request) &&
    request.destination === "script" &&
    !url?.pathname.includes("~perla~") &&
    !(
      hostContains(url, "cdn.skypack.dev") ||
      hostContains(url, "cdn.jsdelivr.net") ||
      hostContains(url, "ga.jspm.io")
    ),
  new StaleWhileRevalidate({
    cacheName: "scripts",
  })
);

// Cache CDN scripts with CacheFirst strategy
registerRoute(
  ({ request, url }) =>
    isGet(request) &&
    request.destination === "script" &&
    !url?.pathname.includes("~perla~") &&
    (hostContains(url, "cdn.skypack.dev") ||
      hostContains(url, "cdn.jsdelivr.net") ||
      hostContains(url, "ga.jspm.io")),
  new CacheFirst({
    cacheName: "cdn-cache",
  })
);

// Cache styles with StaleWhileRevalidate strategy
registerRoute(
  ({ request }) => isGet(request) && request.destination === "style",
  new StaleWhileRevalidate({
    cacheName: "styles",
  })
);
