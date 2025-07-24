[esbuild]: https://esbuild.github.io/
[esbuild has some caveats]: https://esbuild.github.io/content-types#typescript-caveats

# Transpilation

Perla supports Typescript, JSX, TSX, and of course Modern Javascript, Mainly thanks to [esbuild].

Perla downloads a local copy of esbuild for your OS and architecture, this esbuild binary will be reused for all perla projects, so you don't have multiple copies around. Since We're not in a node environment, we only support what esbuild supports and sometimes even less, depending on how close are we with their latest versions.

At dev time, we simply find the requested file from the browser and transpile it back as the corresponding JS file.

Yes! we do it on the fly! this is thanks to the speed of **Go** and **.NET** this transpilation is barely (if at all) noticeable, you can feel confident that whatever you're developing you won't get bothered by annoying compilation phases or bundling at all.

For Typescript, Javascript, TSX, JSX support, You don't need to do anything special they work out of the box.

> [Esbuild has some caveats] when it comes to typescript support.
