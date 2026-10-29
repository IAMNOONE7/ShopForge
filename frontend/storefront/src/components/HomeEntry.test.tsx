// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StrictMode, type ComponentProps } from "react";
import { MemoryRouter } from "react-router";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { HomeEntry } from "./HomeEntry";

const store: Store = {
  id: "store-a", name: "Wooden Home", currency: "CZK", culture: "cs-CZ", logoUrl: null,
  providerKeys: [], theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 },
};
const categories = [{ name: "Furniture", slug: "furniture", parentSlug: null }];
const product = { id: "listing", slug: "table", name: "Oak Table", price: 499, available: 3,
  rating: 0, reviewCount: 0, imageUrl: "/photo" };
type EntryProps = Omit<ComponentProps<typeof HomeEntry>, "children">;
const pending: EntryProps = { categories: [], categoriesPending: true, productsPending: true, productsReady: false };
const ready: EntryProps = { categories, categoriesPending: false, productsPending: false, productsReady: true, product };

beforeAll(async () => { await initializeI18n(); await i18n.changeLanguage("en"); });
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });
afterAll(() => i18n.changeLanguage("en"));

function entry(props: EntryProps, brand = store, url = "/") {
  return <StrictMode><MemoryRouter initialEntries={[url]}><StoreContext value={brand}>
    <HomeEntry key={brand.id} {...props}>
      <h2 id="shop-products" tabIndex={-1}>All products</h2>
      <a href="/p/table">Catalog table</a>
    </HomeEntry>
  </StoreContext></MemoryRouter></StrictMode>;
}

describe("initial home presentation", () => {
  it.each(["cs", "en"])("announces one %s loading state without exposing unfinished catalog sections", async language => {
    await i18n.changeLanguage(language);
    render(entry(pending));
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe(store.name);
    expect(screen.getByRole("heading", { level: 1 }).closest('[aria-busy="true"]')).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe(i18n.t("catalog:loadingProducts"));
    expect(screen.queryByRole("heading", { level: 2 })).toBeNull();
    expect(screen.queryByRole("link")).toBeNull();
    expect(document.querySelector("img")).toBeNull();
  });

  it("waits for category metadata while preloading only the actual featured image", () => {
    const view = render(entry(pending));
    view.rerender(entry({ ...ready, categories: [], categoriesPending: true }));
    expect(screen.getByRole("status").textContent).toBe("Loading categories…");
    expect(screen.getByRole("link", { name: "Explore products" }).getAttribute("href")).toBe("#shop-products");
    expect(document.querySelector('link[rel="preload"][as="image"]')?.getAttribute("href")).toBe(product.imageUrl);
    expect(screen.queryByRole("link", { name: "Catalog table" })).toBeNull();
    view.rerender(entry(ready));
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.getByRole("link", { name: /Oak Table/ }).getAttribute("href")).toBe("/p/table");
    expect(screen.getByRole("link", { name: "Furniture" }).getAttribute("href")).toBe("/c/furniture");
    expect(screen.getByRole("link", { name: "Catalog table" })).toBeTruthy();
  });

  it("lets a keyboard shopper browse available products before categories return", async () => {
    const user = userEvent.setup();
    const view = render(entry({ ...ready, categories: [], categoriesPending: true }));
    const action = screen.getByRole("link", { name: "Explore products" });
    action.focus();
    await user.keyboard("{Enter}");
    const heading = screen.getByRole("heading", { level: 2, name: "All products" });
    await waitFor(() => expect(document.activeElement).toBe(heading));
    expect(screen.getByRole("link", { name: "Catalog table" })).toBeTruthy();
    expect(screen.queryByRole("status")).toBeNull();
    view.rerender(entry(ready));
    expect(screen.getByRole("heading", { name: "All products" })).toBe(heading);
    expect(document.activeElement).toBe(heading);
  });

  it("transfers a focused pending action to the real action when metadata arrives", () => {
    const view = render(entry({ ...ready, categories: [], categoriesPending: true }));
    screen.getByRole("link", { name: "Explore products" }).focus();
    view.rerender(entry(ready));
    expect(document.activeElement).toBe(screen.getByRole("link", { name: "Explore products" }));
  });

  it("does not steal focus from another control when loading finishes", () => {
    const button = document.createElement("button");
    document.body.append(button);
    try {
      const view = render(entry(pending));
      button.focus();
      view.rerender(entry(ready));
      expect(document.activeElement).toBe(button);
    } finally { button.remove(); }
  });

  it("restores a bookmarked product heading after the initial home becomes ready", async () => {
    const view = render(entry(pending, store, "/#shop-products"));
    expect(screen.queryByRole("heading", { level: 2 })).toBeNull();
    view.rerender(entry(ready, store, "/#shop-products"));
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { name: "All products" })));
  });

  it("keeps a shopper's newer focus outside a bookmarked loading home", async () => {
    const button = document.createElement("button");
    document.body.append(button);
    try {
      const view = render(entry(pending, store, "/#shop-products"));
      button.focus();
      view.rerender(entry(ready, store, "/#shop-products"));
      await new Promise(resolve => requestAnimationFrame(resolve));
      expect(document.activeElement).toBe(button);
    } finally { button.remove(); }
  });

  it("releases product-read errors immediately and keeps recovery visible during retries", () => {
    const view = render(entry(pending));
    view.rerender(entry({ ...pending, productsPending: false }));
    expect(screen.getByRole("link", { name: "Catalog table" })).toBeTruthy();
    expect(screen.queryByRole("status")).toBeNull();
    view.rerender(entry(pending));
    expect(screen.getByRole("link", { name: "Catalog table" })).toBeTruthy();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it.each([true, false])("keeps a %s-product photo-less response free of invented imagery", productsReady => {
    const view = render(entry(pending));
    view.rerender(entry({ categories: [], categoriesPending: false, productsPending: false, productsReady }));
    expect(screen.getByRole("link", { name: "Explore products" })).toBeTruthy();
    expect(screen.queryByRole("region", { name: "Browse by category" })).toBeNull();
    expect(document.querySelector(".discovery-product")).toBeNull();
    expect(document.querySelector('link[rel="preload"]')).toBeNull();
  });

  it("starts a new loading presentation for a different store", () => {
    const view = render(entry(ready));
    view.rerender(entry(pending, { ...store, id: "store-b", name: "Another shop" }));
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Another shop");
    expect(screen.getByRole("status")).toBeTruthy();
    expect(screen.queryByRole("link", { name: "Catalog table" })).toBeNull();
  });
});
