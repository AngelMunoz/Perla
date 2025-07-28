# Perla v1 around the corner (2025-0smthing)

Hello Folks it has been a while...

Perla was around the corner by December 2022 however, there was a particular use case that I wasn't able to fulfill at the time. I was trying so hard back then to make it happen, but disappointing users was a really hard blow for me.

It was the last drop in the glass of my burnout. I simply stopped working on Perla for a couple of years. I almost vanished from the F# community as well, it has something really hard to deal with as Perla is my biggest child project to date.

In any case I think back then my scope was too broad and working alone on such a big project was not sustainable.

With the rise of LLMs, which are particularly useful for things I don't want to work with (boilerplate code, hard blocking problems, repetitive tasks, thanks to ADHD).
I felt it was time to revisit Perla and see how it could work.

## What is Perla actually?

Perla is a Single Page Application (SPA) development server focused on web-standards-first features. Inspired by tools like [vite] and [snowpack] and written in F#.

> ### What does it actually mean "web-standards-first"?
>
> Leverage the following web standard features:
>
> - ES Modules
> - Import Maps
>
> With these two features, Perla can provide a development experience similar enough to the Node.js tooling we're used to and without the complexity.
>
> With that in mind, Perla is not a bundler, not a minifier or a transpiler, and at the core it is a development server that serves your files as they are, without any transformation.
>
> That being said, Perla does provide esbuild integration for those who need it, meaning we do have some support for transpilation, bundling, and minification. Rather than opt out (as it used to be), esbuild is now an opt-in feature.

Perla is also a small "package manager" as it leverages the [JSPM] API to generate production grade import maps that can be used to resolve your dependencies at runtime via the browser's native import map support.

The good thing about using import maps and CDNs is that you're more protected against supply chain attacks that are common in the node.js ecosystem. As the browser runs in a sandboxed environment, and it does not execute any code in your machine, it is much harder to exploit.

Perla also aims to have a test runner similar to [Web Test Runner] so your tests run in the browser; The exact same environment your application runs in. This is a great way to ensure that your tests are actually testing the same code that will run in production. The test runner is in the works, and it is likely to be released post v1 due to the complexities and inexperience of working with test runners.

Perla will try to give you ergonomics around features that need to be glued together specially around import maps.

That being said let's talk about some of the features that will be available in Perla v1.

### Local Dependencies

Perla will also support a newer feature that wasn't included in the 2022 effort:

When the [JSPM] API released it's 4.0 version, it introduced a new feature that allows you to list the files you would need if a given set of dependencies were to be part of your project. This allows us to know what files we need to download in a way that the existing tooling understands.

### Fable

As an F# developer myself I can't help but favor a bit the F# ecosystem.

Perla will automatically run Fable projects if specified in the `perla.json` configuration file. For F# developers using perla with it's defaults is a breeze, basically a 0 config setup with no particular effort.

Fable will autostart and the output files will be served by Perla, thanks to the fact that Fable outputs browser compatible javascript files.

### Import Maps

While import maps were also an integral part of the 2022 effort the package management story around it was very deficient and very brittle. This time we revisited the approach and figured out that [JSPM] is as commited (well... they are more commited as they're part of the standards efforts) as us to making import maps a first class citizen in the web ecosystem so leveraging their in the package management resolution and "browserification" of the dependencies is what we think the way forward.

### Give it a try!

Follow the instructions in the [perla docs] and let's get started.

Let's start a new F# Fable project:

```bash
dotnet tool install -g Perla --prerelease
perla new Sample -t fempty
cd Sample
dotnet tool restore # Given that we're using Fable
perla serve
```

This will create a new F# Fable project using the fable empty template, and
start the Perla development server. You can then open your browser and navigate to `http://localhost:7331` to see your new project in action.

for non Fable Projects the process is quite similar.

Install Perla

- Linux/MacOS:

  ```bash
  curl -fsSL https://raw.githubusercontent.com/AngelMunoz/Perla/dev/install.sh | bash
  ```

- Windows:

  ```bash
  iwr https://raw.githubusercontent.com/AngelMunoz/Perla/dev/install.ps1 -useb | iex
  ```

> **_Note_**: Running the scripts above will install Perla in your local profile and attempt to add it to your PATH environment variable. If you're not comfortable with that, you can download the latest release from the [Perla releases page](https://github.com/AngelMunoz/Perla/releases).

```bash
perla new Sample -t basic
cd Sample
perla serve
```

This will create a new project that has a simple HTML file and a JavaScript file that will be served by Perla. You can then open your browser and navigate to `http://localhost:7331` to see your new project in action.

[vite]: https://vitejs.dev/
[snowpack]: https://www.snowpack.dev/
[JSPM]: https://jspm.io/
[Web Test Runner]: https://modern-web.dev/docs/test-runner/overview/
[perla docs]: https://angelmunoz.github.io/Perla/#content/install
