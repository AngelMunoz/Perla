import Navigo from "navigo";

import { effect, signal } from "@preact/signals";

export const Page: import("@preact/signals").Signal<
  [Page, `v${number}`, string | undefined, string | undefined]
> = signal<
  [Page, `v${number}` | undefined, string | undefined, string | undefined]
>(["Home"]);

const rootUrl =
  window.location.hostname === "angelmunoz.github.io" ? "/Perla/" : "/";
export const Router =
  //@ts-expect-error
  new Navigo(rootUrl, {
    hash: true,
    linksSelector: "a",
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
    Page.value = ["Blogs", , , "not-found"];
  });

console.log("Resolving at: " + window.location.href);

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
