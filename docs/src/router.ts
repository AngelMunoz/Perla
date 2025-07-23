import Navigo from "navigo";

import { signal } from "@preact/signals";

export const Page: import("@preact/signals").Signal<Page> =
  signal<Page>("Home");

export const Router =
  //@ts-expect-error
  new Navigo("/", {
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
  .on("/content/:filename", ({ data }: { data?: MarkdownContentProps }) => {
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
    Page.value = ["Blogs", "v1", undefined, "not-found"];
  });

Router.resolve();

setTimeout(() => {
  const location: Match = Router.getCurrentLocation();
  setHeaderPosition(location);
}, 1000);
