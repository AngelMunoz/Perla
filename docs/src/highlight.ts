import hljs from "highlight.js/lib/core";
import javascript from "highlight.js/lib/languages/javascript";
import text from "highlight.js/lib/languages/plaintext";
import fsharp from "highlight.js/lib/languages/fsharp";
import bash from "highlight.js/lib/languages/bash";
import json from "highlight.js/lib/languages/json";
import xml from "highlight.js/lib/languages/xml";
import diff from "highlight.js/lib/languages/diff";

//@ts-ignore
hljs.registerLanguage("", text);
//@ts-ignore
hljs.registerLanguage("javascript", javascript);
//@ts-ignore
hljs.registerLanguage("fsharp", fsharp);
//@ts-ignore
hljs.registerLanguage("bash", bash);
//@ts-ignore
hljs.registerLanguage("json", json);
//@ts-ignore
hljs.registerLanguage("html", xml);
//@ts-ignore
hljs.registerLanguage("diff", diff);

const parser = new DOMParser();
export async function fetchDocs(url: string) {
  const response = await fetch(url).then((response) => {
    if (!response.ok) {
      return Promise.reject(new Error(response.statusText));
    }
    return response.text();
  });
  const elements = parser.parseFromString(response, "text/html");
  elements?.querySelectorAll?.("pre code")?.forEach?.((element) => {
    if (!element) return;
    //@ts-ignore
    hljs.highlightElement(element);
  });
  return elements.body.innerHTML;
}
