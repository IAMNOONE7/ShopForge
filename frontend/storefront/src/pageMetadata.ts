export type PageSeo = { title: string; description: string | null; socialImageUrl: string | null; noIndex: boolean; canonical: string | null };
export type PublicMetadata = { seo?: PageSeo; jsonLd?: unknown[]; previousPage?: string | null; nextPage?: string | null };
type PageKind = "catalog" | "product" | "content";
const record = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const nullable = (value: unknown): value is string | null => value === null || typeof value === "string";
export function absoluteMetadataUrl(value: unknown, allowFragment = false): string | null {
  if (typeof value !== "string" || !/^https?:\/\//i.test(value)) return null;
  try {
    const url = new URL(value);
    return url.hostname && !url.username && !url.password && (allowFragment || !url.hash) ? value : null;
  } catch { return null; }
}
export function readPageSeo(value: unknown): PageSeo | null {
  if (!record(value) || typeof value.title !== "string" || !value.title.trim() || !nullable(value.description)
    || !nullable(value.socialImageUrl) || typeof value.noIndex !== "boolean" || !nullable(value.canonical)) return null;
  if (value.canonical !== null && absoluteMetadataUrl(value.canonical) === null) return null;
  return { title: value.title, description: value.description, socialImageUrl: absoluteMetadataUrl(value.socialImageUrl, true), noIndex: value.noIndex, canonical: value.canonical };
}
export function serializeLinkedData(value: unknown): string | null {
  if (!Array.isArray(value) || !value.length || !value.every(node => record(node) && node["@context"] === "https://schema.org" && typeof node["@type"] === "string" && node["@type"].length > 0)) return null;
  try {
    return JSON.stringify(value, (_key, item: unknown) => {
      if (item === undefined || ["function", "symbol", "bigint"].includes(typeof item) || typeof item === "number" && !Number.isFinite(item)) throw new TypeError("Invalid linked data.");
      return item;
    }).replace(/[<>&\u2028\u2029]/g, character => "\\u" + character.charCodeAt(0).toString(16).padStart(4, "0"));
  } catch { return null; }
}
const owners = new WeakMap<HTMLElement, symbol>();
export function hasPageTitle() { return document.head.hasAttribute("data-shopforge-title"); }
export function installPageMetadata(data: unknown, kind?: PageKind) {
  const head = document.head, owner = Symbol(), nodes: HTMLElement[] = [];
  owners.set(head, owner);
  // Remove only this app's metadata; viewport, encoding, icons and other resources belong to the shell.
  head.querySelectorAll("[data-shopforge-seo]").forEach(node => node.remove());
  head.removeAttribute("data-shopforge-title");
  const source = record(data) ? data : null, seo = readPageSeo(source?.seo);
  function meta(name: string, content: string | null, property = false) {
    if (content === null) return;
    const node = document.createElement("meta"); node.setAttribute(property ? "property" : "name", name);
    node.content = content; node.dataset.shopforgeSeo = ""; head.append(node); nodes.push(node);
  }
  function link(rel: string, value: unknown) {
    const href = absoluteMetadataUrl(value); if (href === null) return;
    const node = document.createElement("link"); node.rel = rel; node.href = href; node.dataset.shopforgeSeo = ""; head.append(node); nodes.push(node);
  }
  meta("robots", seo ? seo.noIndex ? "noindex, follow" : "index, follow" : "noindex, nofollow");
  if (seo) {
    document.title = seo.title; head.setAttribute("data-shopforge-title", "");
    meta("description", seo.description);
    link("canonical", seo.canonical);
    meta("og:title", seo.title, true); meta("og:description", seo.description, true);
    meta("og:url", seo.canonical, true); meta("og:image", seo.socialImageUrl, true);
    meta("og:type", "website", true);
    meta("twitter:card", seo.socialImageUrl ? "summary_large_image" : "summary");
    meta("twitter:title", seo.title); meta("twitter:description", seo.description); meta("twitter:image", seo.socialImageUrl);
    if (kind === "catalog" && seo.canonical !== null) { link("prev", source?.previousPage); link("next", source?.nextPage); }
    const linkedData = seo.canonical !== null && (kind === "catalog" || kind === "product") ? serializeLinkedData(source?.jsonLd) : null;
    if (linkedData !== null) {
      const node = document.createElement("script"); node.type = "application/ld+json"; node.textContent = linkedData;
      node.dataset.shopforgeSeo = ""; head.append(node); nodes.push(node);
    }
  }
  return () => {
    nodes.forEach(node => node.remove());
    if (owners.get(head) === owner) { owners.delete(head); head.removeAttribute("data-shopforge-title"); }
  };
}
