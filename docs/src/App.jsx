import "./App.css?js";
import { useSignal, signal, computed } from "@preact/signals";
import { Page } from "./router.js";
import { Index } from "./Components/Index.js";
import { Sidenav } from "./Components/Sidenav.js";
import { MarkdownContent } from "./Components/MarkdownContent.js";
import { BlogList } from "./Components/BlogList.js";
import Blogs from "./blogs.json?js";
import Toc from "./toc.json?js";

const route = signal("Home");

const gettingStarted = Toc["GettingStarted"];

const versions =
  Object.entries(Toc)
    .reduce((current, [key, content]) => {
      if (key === "GettingStarted") {
        return current;
      }
      const versioned = {
        [key]: /** @type {Record<string, ToCSection>} */ (content),
      };
      current.push(versioned);
      return current;
    }, /** @type {Record<String, Record<string, ToCSection>>[]} */ ([]))
    ?.reverse?.() ?? [];

const sidenav = (
  <Sidenav
    hidden={route.value === "Home"}
    gettingStarted={gettingStarted}
    versions={versions}
  />
);

/**
 *
 * @param {OffCanvasProps} props
 * @returns
 */
function OffCanvas({ isOpen, onClose }) {
  return (
    <sl-drawer
      label="Table of Contents"
      open={isOpen}
      placement="start"
      onsl-after-hide={() => (console.log("dude"), onClose?.())}
    >
      <div class="off-canvas-sidenav">{sidenav}</div>
      {onClose ? (
        <sl-button slot="footer" variant="primary" onClick={() => onClose()}>
          Close
        </sl-button>
      ) : null}
    </sl-drawer>
  );
}

/**
 *
 * @param {NavbarProps} param0
 * @returns
 */
function Navbar({ requestMenu }) {
  return (
    <>
      <nav class="perla-nav with-box-shadow">
        <section>
          <sl-button
            class="menu-btn"
            variant="text"
            size="large"
            onClick={() => requestMenu?.()}
          >
            Menu
          </sl-button>
          <sl-button href={`/#/`} variant="text" size="large">
            Perla
          </sl-button>
        </section>
        <section class="nav-links">
          <ul class="link-list">
            <li>
              <sl-button href={"/#/content/index"} variant="text">
                Docs
              </sl-button>
            </li>
            <li>
              <sl-button href="/#/v0/docs/features/development" variant="text">
                V0 Docs
              </sl-button>
            </li>
            <li>
              <sl-button href="/#/blogs" variant="text">
                Blog
              </sl-button>
            </li>
            <li>
              <sl-button
                target="_blank"
                href="https://github.com/AngelMunoz/Perla"
                variant="text"
              >
                Github
              </sl-button>
            </li>
          </ul>
        </section>
      </nav>
    </>
  );
}

/**
 * @type {{ value: DocsVersion }}
 */
const version = signal("v1");
const content = computed(() => {
  const [page, ver, section, pageName] = Page.value;
  if (page === "Home") {
    return <Index />;
  } else if (page === "Blogs") {
    return <BlogList blogs={Blogs} />;
  } else {
    version.value = ver;
    return (
      <MarkdownContent
        version={ver}
        filename={pageName}
        section={section}
        contentKind={page}
      />
    );
  }
});

function NoticesBanner() {
  return (
    <sl-alert variant="primary" open closable>
      <sl-icon slot="icon" name="info-circle"></sl-icon>
      <strong>Perla V1.0.0 betas are out!</strong>
      <p>
        Hello there, the next Perla version is in the works as well as the
        documentation website, please keep in mind that some of the docs (even
        those in the v1 section) are still out of date, we're updating them as
        soon as we can.
        <br /> Feeling Adventurous? Get the latest bits:
        <br />
        <strong>dotnet tool install --global Perla --prerelease</strong>
      </p>
    </sl-alert>
  );
}

export function App() {
  const isOpen = useSignal(false);

  return (
    <>
      <Navbar
        requestMenu={() => {
          isOpen.value = true;
        }}
      />
      <OffCanvas
        isOpen={isOpen}
        onClose={() => {
          isOpen.value = false;
        }}
      />
      <NoticesBanner />
      <main class={`${route.value}`}>
        {sidenav}
        {content}
      </main>
      <footer></footer>
    </>
  );
}
