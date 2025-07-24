[package manager]: #v1/docs/features/package-manager
[jspm]: https://jspm.io/
[jspm generator]: https://generator.jspm.io/
[jspm generator api]: https://jspm.org/docs/cdn#jspm-generator

# Import Maps

> For a full reference on Import Maps Check the Import Maps Repository: https://github.com/WICG/import-maps

Rather than relying on local NPM packages, the approach we took with Perla is different, we try to rely on the browser as much as possible.

Import Maps are a way to tell the browser how to load dependencies as the import-maps repository says:

> This proposal allows control over what URLs get fetched by JavaScript import statements and import() expressions. This allows "bare import specifiers", such as import moment from "moment", to work.
>
> The mechanism for doing this is via an import map which can be used to control the resolution of module specifiers generally. As an introductory example, consider the code

With Perla, we generate an import map for your project using the [JSPM Generator API][jspm generator api], which is then injected into your `index.html` at dev and build time. You will find this import map in your repository under the name `perla.json.importmap`. For example, this very documentation website (which is dogfooding Perla) has an _import map file_.

> **Note:** If you are using Node.js, you can achieve a similar workflow by using the [JSPM CLI][jspm]. In Perla, we use the [JSPM Generator API][jspm generator api] to provide this functionality outside of Node.js, so you get the similar benefits directly in the browser-oriented workflow.

A typical import map looks like this:

```json
{
  "imports": {
    "date-fns": "https://ga.jspm.io/npm:date-fns@2.29.3/index.js",
    "react": "https://ga.jspm.io/npm:react@19.1.0/index.js",
    "react-dom": "https://ga.jspm.io/npm:react-dom@19.1.0/index.js",
    "react-dom/client": "https://ga.jspm.io/npm:react-dom@19.1.0/client.js"
  },
  "scopes": {
    "https://ga.jspm.io/": {
      "scheduler": "https://ga.jspm.io/npm:scheduler@0.26.0/index.js"
    }
  }
}
```

### JSPM

Big shout out to the [JSPM] folks who are making an incredible job of providing reliable software that allows import maps to move forward and get adoption, in perla we leverage the [jspm generator api] to be able to bring import map resolution outside nodejs, please check them out as they are doing the good work that allows others to keep moving forward

> What's the fuzz about import maps? Try the online [JSPM Generator]!
