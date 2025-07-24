/**
 *
 * @param {ToCSection} Props
 */
function TocSection({ label, sections, open }) {
  const elements =
    sections?.map?.(({ title, url }) => (
      <sl-tree-item>
        <sl-button variant="text" href={url}>
          {title}
        </sl-button>
      </sl-tree-item>
    )) ?? [];

  return (
    <sl-tree-item expanded={open}>
      {label}
      {elements}
    </sl-tree-item>
  );
}

/**
 *
 * @param {Record<string, Record<string, ToCSection>>} versioned
 * @param {number} i
 */
function VersionedSection(versioned, i) {
  return Object.entries(versioned).map(([version, section]) => {
    const sections = [];
    for (const [_, { label, sections: sec }] of Object.entries(section)) {
      sections.push(<TocSection label={label} sections={sec} open={i === 0} />);
    }
    return (
      <sl-tree-item expanded={i === 0}>
        <header>{version}</header>
        {sections}
      </sl-tree-item>
    );
  });
}

/**
 *
 * @param {{ hidden: boolean; gettingStarted: ToCSection; versions: Record<string, Record<string, ToCSection>>[] }} param0
 * @returns
 */
export function Sidenav({ gettingStarted, versions, hidden }) {
  const elements = versions?.map?.(VersionedSection) ?? [];

  return (
    <aside class="perla-sidenav">
      <sl-tree>
        <TocSection
          label={gettingStarted.label}
          sections={gettingStarted.sections}
          open={true}
        />
        {elements}
      </sl-tree>
    </aside>
  );
}
