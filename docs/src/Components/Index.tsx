function FeatureListItem({ header, content }: FeatureListItemProps) {
  return (
    <li class="features__feature-list-item card with-box-shadow">
      <h3>{header}</h3>
      <p>{content}</p>
    </li>
  );
}

function FeatureList({ features }: FeatureListProps) {
  return (
    <ul class="features__feature-list">
      {features.map((feature) => (
        <FeatureListItem header={feature.header} content={feature.content} />
      ))}
    </ul>
  );
}

const rows = [
  [
    {
      header: "Fast",
      content: (
        <p>
          Perla uses <b>.NET</b> and <b>Go</b> under the hood for a performant
          development experience!
        </p>
      ),
    },
    {
      header: "Cross-Platform",
      content: "Windows, Linux, MacOS, even on your Raspberry Pi!",
    },
    {
      header: "Single binary or .NET Tool",
      content:
        "Download the tool or if you're a .NET user just install the dotnet tool.",
    },
    {
      header: "Typescript or JSX/TSX?",
      content:
        "From modern Javascript to Typescript, develop in your flavor of choice.",
    },
    {
      header: "No Local dependencies",
      content: (
        <>
          Forget about webpack, npm, and other complex tooling, Perla uses CDNs
          like:
          <ul class="link-list">
            <li>
              <sl-button
                type="text"
                target="_blank"
                href="https://jspm.org/docs/cdn"
                variant="primary"
                outline
              >
                JSPM
              </sl-button>
            </li>
            <li>
              <sl-button
                type="text"
                target="_blank"
                href="https://www.skypack.dev/"
                variant="primary"
                outline
              >
                Skypack
              </sl-button>
            </li>
            <li>
              <sl-button
                type="text"
                target="_blank"
                href="https://unpkg.com/"
                variant="primary"
                outline
              >
                Unpkg
              </sl-button>
            </li>
          </ul>
        </>
      ),
    },
    {
      header: "Unbundled Development",
      content:
        "Let the browser help you, reduce feedback loops between your code changes with unbundled development!",
    },
  ],
];

export function Index() {
  return (
    <article class="page index-page">
      <header class="index-header">
        <h1>Welcome to Perla!</h1>
        <p>Perla is a take on modern tooling for front-end development.</p>
        <p>
          A fast, and simple to use development server. No node.js knowledge
          required.
        </p>
      </header>

      <section class="action-buttons">
        <sl-button
          size="large"
          variant="primary"
          outline
          href={`#/content/install`}
        >
          Get Started
        </sl-button>
        <sl-button
          size="large"
          variant="primary"
          outline
          href={`#/content/index`}
        >
          Learn More
        </sl-button>
      </section>

      <section class="features">
        <header class="index-header">
          <h3>Features</h3>
        </header>
        {rows.map((row) => (
          <FeatureList features={row} />
        ))}
      </section>
    </article>
  );
}
