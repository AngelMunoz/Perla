import "./MarkdownContent.css?js";
import { useEffect } from "preact/hooks";
import { useSignal } from "@preact/signals";
import { fetchDocs } from "../highlight.js";
import { buildUrl } from "../utils.js";

const errorContent = (error: string) => [
  <p>Well, Well, Well... How the turntables have turned...</p>,
  <p>{error}</p>,
];

function getUrlForPage(
  kind: ContentKind | undefined,
  version: DocsVersion | undefined,
  filename: string,
  section?: string
) {
  const contentKind = kind ?? "Docs";
  let url = `https://github.com/AngelMunoz/Perla/edit/main/src/${contentKind.toLowerCase()}/assets`;
  if (version) {
    url += `/${version}`;
  }

  url += "/docs";

  if (section) {
    url += `/${section}`;
  }

  url += `/${filename}.md`;
  return url;
}

export function MarkdownContent({
  filename,
  section,
  version,
  contentKind,
}: MarkdownContentProps) {
  const content = useSignal("");
  const error = useSignal("");

  useEffect(() => {
    fetchDocs(buildUrl(contentKind ?? "Docs", filename, section, version))
      .then((response) => (content.value = response))
      .catch((err) => {
        content.value = "";
        error.value = err;
      });
  }, [filename, section]);

  let pageContent;

  if (content.value) {
    pageContent = (
      //@ts-ignore
      <article
        class="markdown-content"
        dangerouslySetInnerHTML={{ __html: content.value }}
      ></article>
    );
  } else {
    pageContent = (
      <article class="markdown-content markdown-error">
        {error.value ? errorContent(error.value) : null}
      </article>
    );
  }

  return (
    <section class="markdown-page">
      <header class="markdown-content__header">
        <sl-button
          type="text"
          target="_blank"
          href={getUrlForPage(contentKind, version, filename, section)}
        >
          Edit this page
        </sl-button>
      </header>
      {pageContent}
    </section>
  );
}
