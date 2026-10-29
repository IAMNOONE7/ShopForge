// @vitest-environment jsdom
import { StrictMode, useState } from "react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate, useParams } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { RouteEffects } from "./components/RouteEffects";
import { RouteContent } from "./components/RouteContent";
import { initializeI18n, i18n } from "./i18n";
import { useCurrentSlug, usePageMetadata } from "./usePageMetadata";
import { ContentPage } from "./pages/ContentPage";
import { ProductListPage } from "./pages/ProductListPage";
import { StoreContext } from "./storeContext";
import type { Store } from "./store";

const store: Store = { id: "store-a", name: "Dílna / Workshop", currency: "CZK", culture: "cs-CZ", logoUrl: null, providerKeys: [], theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 } };
const seo = { title: "Exact merchant title — Dílna", description: "Saved description", socialImageUrl: "https://images.example.test/share.png", noIndex: false, canonical: "https://primary.example.test/pages/terms" };
const words = { slug: "terms", title: "Terms", body: "Plain text.", seo };
const meta = (name: string) => document.head.querySelector<HTMLMetaElement>('meta[name="' + name + '"]')?.content;
const canonical = () => document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]')?.href;
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
function Shell() {
  const location = useLocation(), navigate = useNavigate();
  return <StoreContext value={store}><RouteEffects store={store} categories={[]} />
    <nav><Link to="/pages/terms">Terms route</Link><Link to="/pages/missing">Missing route</Link><Link to="/pages/other">Other route</Link><Link to="/cart">Cart route</Link><button onClick={() => void navigate(-1)}>Back</button></nav>
    <output data-testid="location">{location.pathname + location.search + location.hash}</output>
    <main id="main-content"><Outlet context={{ status: "ready", categories: [] }} /></main>
  </StoreContext>;
}
function route(path: string, elements = <Route path="pages/:slug" element={<ContentPage />} />) {
  return render(<StrictMode><MemoryRouter initialEntries={[path]}><Routes><Route element={<Shell />}>{elements}<Route path="cart" element={<h1>Your cart</h1>} /></Route></Routes></MemoryRouter></StrictMode>);
}
beforeAll(initializeI18n);
beforeEach(async () => { await i18n.changeLanguage("en"); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });

describe("metadata navigation ownership", () => {
  it("keeps a failed public render no-index when its page metadata effect never mounts", async () => {
    function BrokenPage(): never { throw new Error("Broken fixture page"); }
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    route("/pages/terms", <Route path="pages/:slug" element={<RouteContent><BrokenPage /></RouteContent>} />);
    await screen.findByRole("heading", { name: i18n.t("errors:pageLoadTitle") });
    expect(meta("robots")).toBe("noindex, nofollow"); expect(canonical()).toBeUndefined();
  });
  it.each(["en", "cs"])("uses resolved SEO without later heading/local UI changes overwriting it in %s", async language => {
    await i18n.changeLanguage(language); vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => json(words))); route("/pages/terms");
    await screen.findByRole("heading", { name: "Terms" }); await waitFor(() => expect(document.title).toBe(seo.title));
    expect(canonical()).toBe(seo.canonical); expect(document.head.querySelectorAll('meta[name="robots"]')).toHaveLength(1);
    act(() => { screen.getByRole("heading", { name: "Terms" }).textContent = "A changed visual heading"; });
    await waitFor(() => expect(document.title).toBe(seo.title));
    await i18n.changeLanguage(language === "cs" ? "en" : "cs"); expect(document.title).toBe(seo.title);
  });
  it("clears public tags on cart navigation, uses a private title and restores metadata through browser Back", async () => {
    const user = userEvent.setup(); vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => json(words))); route("/pages/terms");
    await waitFor(() => expect(canonical()).toBe(seo.canonical)); await user.click(screen.getByRole("link", { name: "Cart route" }));
    await waitFor(() => expect(document.title).toBe("Your cart · Dílna / Workshop")); expect(canonical()).toBeUndefined(); expect(meta("description")).toBeUndefined(); expect(meta("robots")).toBe("noindex, nofollow");
    await user.click(screen.getByRole("button", { name: "Back" })); await waitFor(() => expect(document.title).toBe(seo.title)); expect(canonical()).toBe(seo.canonical);
  });
  it.each([404, 503])("clears stale tags after a %i page response", async status => {
    const user = userEvent.setup(); vi.stubGlobal("fetch", vi.fn().mockImplementation(async (url: string) => url.endsWith("/terms") ? json(words) : json({ title: "Unavailable" }, status))); route("/pages/terms");
    await waitFor(() => expect(canonical()).toBe(seo.canonical)); await user.click(screen.getByRole("link", { name: "Missing route" })); await screen.findByRole("heading", { name: status === 404 ? "Page not found" : "This page could not be loaded" });
    expect(canonical()).toBeUndefined(); expect(meta("description")).toBeUndefined(); expect(meta("robots")).toBe("noindex, nofollow"); expect(document.title).not.toBe(seo.title);
  });
  it("clears tags during a new request and ignores a late reply from the old route", async () => {
    const user = userEvent.setup(), held = deferred<Response>(); let otherSignal: AbortSignal | undefined;
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async (url: string, init: RequestInit) => {
      if (url.endsWith("/other")) { otherSignal = init.signal as AbortSignal; return held.promise; }
      return json(words);
    })); route("/pages/terms");
    await waitFor(() => expect(canonical()).toBe(seo.canonical)); await user.click(screen.getByRole("link", { name: "Other route" }));
    expect(canonical()).toBeUndefined(); expect(meta("robots")).toBe("noindex, nofollow");
    await user.click(screen.getByRole("link", { name: "Cart route" })); expect(otherSignal?.aborted).toBe(true);
    await act(async () => held.resolve(json({ ...words, seo: { ...seo, title: "Late title" } }))); expect(document.title).toBe("Your cart · Dílna / Workshop"); expect(canonical()).toBeUndefined();
  });
  it("retains page content when optional runtime metadata is missing and stays no-index", async () => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => json({ slug: words.slug, title: words.title, body: words.body }))); route("/pages/terms");
    await screen.findByRole("heading", { name: "Terms" }); expect(meta("robots")).toBe("noindex, nofollow"); expect(canonical()).toBeUndefined();
    await waitFor(() => expect(document.title).toBe("Terms · Dílna / Workshop"));
  });
  it("drops tags and the metadata title on a same-page failed refresh without a heading mutation", async () => {
    function Page() {
      const [fresh, setFresh] = useState(true); usePageMetadata(fresh ? words : null, "content");
      return <><h1>Terms</h1><button onClick={() => setFresh(false)}>Fail refresh</button></>;
    }
    const user = userEvent.setup(); route("/pages/terms", <Route path="pages/:slug" element={<Page />} />);
    await waitFor(() => expect(document.title).toBe(seo.title)); await user.click(screen.getByRole("button", { name: "Fail refresh" }));
    await waitFor(() => expect(document.title).toBe("Terms · Dílna / Workshop")); expect(meta("robots")).toBe("noindex, nofollow"); expect(canonical()).toBeUndefined();
  });
});

describe("current public slugs", () => {
  function Product() {
    const { slug } = useParams(); const moved = useCurrentSlug("product", slug, "current-chair");
    usePageMetadata(moved ? null : { seo }, "product"); return <h1>{moved ? "Moving" : "Chair"}</h1>;
  }
  it("replaces a product alias while preserving variant query/hash and avoids a Back redirect loop", async () => {
    const user = userEvent.setup(); route("/cart", <><Route path="p/:slug" element={<Product />} /><Route path="pages/:slug" element={<Link to="/p/old-chair?variant=large#reviews">Product alias</Link>} /></>);
    await user.click(screen.getByRole("link", { name: "Terms route" })); await user.click(screen.getByRole("link", { name: "Product alias" }));
    await waitFor(() => expect(screen.getByTestId("location").textContent).toBe("/p/current-chair?variant=large#reviews"));
    await user.click(screen.getByRole("button", { name: "Back" })); expect(screen.getByTestId("location").textContent).toBe("/pages/terms");
  });
  it.each(["/c/old-chairs?q=oak&sort=price&f.material=wood&page=3#results", "/?category=old-chairs&q=oak&sort=price&f.material=wood&page=3#results"])("rereads a category alias with the original query intact: %s", async path => {
    const fetch = vi.fn().mockImplementation(async (url: string) => {
      const params = new URL(url, "http://fixture.test").searchParams;
      return json({ items: [], totalCount: 0, page: 3, pageSize: 24, filters: [], path: [{ name: "Chairs", slug: "chairs", parentSlug: null }], children: [], pageText: null, seo: { ...seo, canonical: "https://primary.example.test/c/chairs" }, jsonLd: [], previousPage: null, nextPage: null, requested: params.toString() });
    }); vi.stubGlobal("fetch", fetch); route(path, <><Route index element={<ProductListPage />} /><Route path="c/:slug" element={<ProductListPage />} /></>);
    await waitFor(() => expect(screen.getByTestId("location").textContent).toContain(path.startsWith("/c/") ? "/c/chairs?" : "/?category=chairs&"));
    const location = screen.getByTestId("location").textContent!, query = new URL(location, "http://fixture.test").searchParams;
    expect(query.get("q")).toBe("oak"); expect(query.get("f.material")).toBe("wood"); expect(query.get("sort")).toBe("price"); expect(query.get("page")).toBe("3"); expect(location.endsWith("#results")).toBe(true);
    await waitFor(() => expect(canonical()).toBe("https://primary.example.test/c/chairs"));
    const last = String(fetch.mock.calls.at(-1)?.[0]); expect(last).toContain("category=chairs"); expect(last).toContain("f.material=wood"); expect(last).toContain("page=3");
  });
  it.each(["../account", "https://foreign.test/", "", "Bad Slug"])("does not navigate to malformed returned slug %s", returned => {
    function Invalid() { const { slug } = useParams(); useCurrentSlug("product", slug, returned); return <h1>Current</h1>; }
    route("/p/old", <Route path="p/:slug" element={<Invalid />} />); expect(screen.getByTestId("location").textContent).toBe("/p/old");
  });
});
