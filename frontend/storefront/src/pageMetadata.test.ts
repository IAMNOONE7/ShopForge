// @vitest-environment jsdom
import { afterEach, describe, expect, it } from "vitest";
import { absoluteMetadataUrl, hasPageTitle, installPageMetadata, readPageSeo, serializeLinkedData } from "./pageMetadata";

const seo = { title: 'Exact title <script>literal</script> " &', description: 'Description <b>literal</b>', socialImageUrl: "https://images.example.test/cover.png#preview", noIndex: false, canonical: "https://primary.example.test/c/chairs?page=2" };
const meta = (name: string) => document.head.querySelector<HTMLMetaElement>('meta[name="' + name + '"], meta[property="' + name + '"]')?.content;
const link = (rel: string) => document.head.querySelector<HTMLLinkElement>('link[rel="' + rel + '"]')?.href;
afterEach(() => { document.head.querySelectorAll("[data-shopforge-seo], [data-test-resource]").forEach(node => node.remove()); document.head.removeAttribute("data-shopforge-title"); });

describe("public metadata", () => {
  it("keeps authoritative title, primary host, paging and exact inert attribute text", () => {
    const viewport = document.createElement("meta"); viewport.name = "viewport"; viewport.content = "width=device-width"; viewport.dataset.testResource = ""; document.head.append(viewport);
    const dispose = installPageMetadata({ seo, previousPage: "https://primary.example.test/c/chairs", nextPage: "https://primary.example.test/c/chairs?page=3", jsonLd: [{ name: "</script><script>window.injected=true</script>" }] }, "catalog");
    expect(document.title).toBe(seo.title); expect(meta("description")).toBe(seo.description); expect(meta("og:title")).toBe(seo.title);
    expect(meta("og:image")).toBe(seo.socialImageUrl); expect(meta("twitter:card")).toBe("summary_large_image");
    expect(meta("robots")).toBe("index, follow"); expect(link("canonical")).toBe(seo.canonical);
    expect(link("prev")).toBe("https://primary.example.test/c/chairs"); expect(link("next")).toBe("https://primary.example.test/c/chairs?page=3");
    expect(document.head.querySelector("script:not([src])")).toBeNull(); expect(document.head.querySelector("img")).toBeNull();
    expect(viewport.isConnected).toBe(true); dispose(); expect(viewport.isConnected).toBe(true); expect(hasPageTitle()).toBe(false);
  });
  it("removes missing optional tags and all public details on a private/loading/error state", () => {
    const old = installPageMetadata({ seo }, "product");
    const current = installPageMetadata({ seo: { ...seo, noIndex: true, description: null, socialImageUrl: null, canonical: null } }, "content");
    old(); expect(hasPageTitle()).toBe(true); expect(meta("robots")).toBe("noindex, follow");
    expect(meta("description")).toBeUndefined(); expect(meta("og:image")).toBeUndefined(); expect(link("canonical")).toBeUndefined(); expect(meta("twitter:card")).toBe("summary");
    const privatePage = installPageMetadata(null); current();
    expect(meta("robots")).toBe("noindex, nofollow"); expect(meta("og:title")).toBeUndefined(); expect(hasPageTitle()).toBe(false);
    expect(document.head.querySelectorAll('meta[name="robots"]')).toHaveLength(1); privatePage();
  });
  it("does not add catalog pagination to a product or content page", () => {
    const dispose = installPageMetadata({ seo, previousPage: "https://primary.example.test/", nextPage: "https://primary.example.test/?page=3" }, "product");
    expect(link("prev")).toBeUndefined(); expect(link("next")).toBeUndefined(); dispose();
  });
  it.each([undefined, null, {}, { ...seo, title: "" }, { ...seo, title: 10 }, { ...seo, description: undefined }, { ...seo, socialImageUrl: undefined }, { ...seo, noIndex: "false" }, { ...seo, canonical: undefined }, { ...seo, canonical: "javascript:alert(1)" }])("does not infer indexable metadata from malformed data: %j", value => {
    expect(readPageSeo(value)).toBeNull();
    const dispose = installPageMetadata({ seo: value }); expect(meta("robots")).toBe("noindex, nofollow"); expect(link("canonical")).toBeUndefined(); dispose();
  });
  it.each(["javascript:alert(1)", "data:image/png;base64,AA", "/relative", "//another.test/", "https://user:password@example.test/", "https://example.test/#fragment", "https://", "file:///tmp/file"])("refuses non-public canonical addresses: %s", value => {
    expect(absoluteMetadataUrl(value)).toBeNull();
  });
  it("allows absolute primary-domain addresses without replacing them with the current alias", () => {
    expect(absoluteMetadataUrl("https://other-primary.test/p/chair")).toBe("https://other-primary.test/p/chair");
    expect(absoluteMetadataUrl("http://shop-a.localhost:5185/")).toBe("http://shop-a.localhost:5185/");
    expect(readPageSeo({ ...seo, socialImageUrl: "javascript:alert(1)" })).toEqual({ ...seo, socialImageUrl: null });
  });
  it("preserves Product/Offer/breadcrumb facts and escapes script delimiters as JSON data", () => {
    const documents = [{ "@context": "https://schema.org", "@type": "Product", name: "</script><script>window.injected=true</script>&\u2028\u2029",
      offers: { "@type": "Offer", price: "2499.5", priceCurrency: "CZK", availability: "https://schema.org/InStock" } },
    { "@context": "https://schema.org", "@type": "BreadcrumbList", itemListElement: [{ "@type": "ListItem", position: 1, name: "Home", item: "https://primary.example.test/" }] }];
    const serialized = serializeLinkedData(documents)!;
    expect(JSON.parse(serialized)).toEqual(documents); expect(serialized).not.toMatch(/[<>&\u2028\u2029]/);
    const dispose = installPageMetadata({ seo, jsonLd: documents }, "product");
    const script = document.head.querySelector('script[type="application/ld+json"]')!;
    expect(script.textContent).toBe(serialized); expect(script.hasAttribute("src")).toBe(false);
    expect(document.head.querySelectorAll('script:not([type="application/ld+json"])')).toHaveLength(0);
    const privatePage = installPageMetadata(null); expect(document.head.querySelector('script[type="application/ld+json"]')).toBeNull(); dispose(); privatePage();
  });
  it.each([undefined, null, [], {}, [null], ["text"], [{ "@type": "Product" }], [{ "@context": "https://foreign.test/", "@type": "Product" }], [{ "@context": "https://schema.org", "@type": "Product", price: Infinity }]])("omits malformed structured data without synthesizing replacement facts: %j", value => {
    expect(serializeLinkedData(value)).toBeNull();
    const dispose = installPageMetadata({ seo, jsonLd: value }, "catalog"); expect(document.head.querySelector('script[type="application/ld+json"]')).toBeNull(); expect(document.title).toBe(seo.title); dispose();
  });
  it("does not emit linked data from content pages or without a proved canonical", () => {
    const jsonLd = [{ "@context": "https://schema.org", "@type": "Product" }];
    const content = installPageMetadata({ seo, jsonLd }, "content"); expect(document.head.querySelector('script[type="application/ld+json"]')).toBeNull(); content();
    const noAddress = installPageMetadata({ seo: { ...seo, canonical: null }, jsonLd }, "product"); expect(document.head.querySelector('script[type="application/ld+json"]')).toBeNull(); noAddress();
  });
});
