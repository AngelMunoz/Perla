import Navigo from "navigo";

import { effect, signal } from "@preact/signals";

export const Page: import("@preact/signals").Signal<
  [Page, `v${number}`, string | undefined, string | undefined]
> = signal<
  [Page, `v${number}` | undefined, string | undefined, string | undefined]
>(["Home"]);

const rootUrl = "/";
export const Router =
  //@ts-expect-error
  new Navigo(rootUrl, {
    hash: true,
    linksSelector: "a",
    strategy: "ALL",
  });

const setHeaderPosition = ({ params }: Match) => {
  const id = params?.id;
  const el = document.querySelector(`#${id}`);
  el?.scrollIntoView(true);
};

Router.hooks({
  after: setHeaderPosition,
  already: setHeaderPosition,
});

Router.on("", () => (Page.value = ["Home"]))
  .on("Perla", () => (Page.value = ["Home"]))
  .on("content/:filename", ({ data }: { data?: MarkdownContentProps }) => {
    if (!data?.filename) return;
    Page.value = ["Content", data.version ?? "v1", data.section, data.filename];
  })
  .on(
    ":version/docs/:section/:filename",
    ({ data }: { data?: MarkdownContentProps }) => {
      if (!data?.filename) return;
      Page.value = ["Docs", data?.version ?? "v1", data.section, data.filename];
    }
  )
  .on("blogs/:filename", ({ data }: { data?: MarkdownContentProps }) => {
    if (!data?.filename) return;
    Page.value = ["Blogs", data.version ?? "v1", data.section, data.filename];
  })
  .on("blogs", () => {
    Page.value = ["Blogs"];
  })
  .notFound(() => {
    console.warn("Page not found");
    Page.value = ["Blogs", undefined, undefined, "not-found"];
  });

console.log("Resolving at: " + window.location.href);
console.log("Router root URL: " + rootUrl);
console.log("Router strategy: ALL");

effect(() => {
  const [page, version, section, filename] = Page.value;
  console.log(
    `Navigated to ${page} with version ${version}, section ${section}, and filename ${filename}`
  );
});

addEventListener("DOMContentLoaded", () => {
  Router.resolve();
  const location: Match = Router.getCurrentLocation();
  setHeaderPosition(location);
  Router.updatePageLinks();
});

setTimeout(() => {
  const location: Match = Router.getCurrentLocation();
  setHeaderPosition(location);
  Router.updatePageLinks();
}, 1000);
