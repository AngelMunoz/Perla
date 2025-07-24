export function buildUrl(
  kind: ContentKind,
  filename: string,
  section?: string,
  version?: string
) {
  const name = filename.endsWith(".html") ? filename : `${filename}.html`;
  const isBlogOrContent = kind === "Blogs" || kind === "Content";
  const prefix =
    window.location.hostname === "angelmunoz.github.io"
      ? "/Perla/assets"
      : "/assets";

  let url = `${prefix}/${kind.toLowerCase()}`;

  if (!isBlogOrContent && version) {
    url += `/${version ?? "v0"}`;
  }

  if (section) {
    url += `/${section}`;
  }

  url += `/${name}`;

  return url;
}
