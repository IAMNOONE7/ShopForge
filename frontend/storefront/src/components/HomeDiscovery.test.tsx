// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import type { Category, ProductSummary } from "../api";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { HomeDiscovery } from "./HomeDiscovery";
import { StoreBrand } from "./StoreBrand";

const store: Store = {
  id: "store-a", name: "Wooden Home", currency: "CZK", culture: "cs-CZ",
  logoUrl: null, providerKeys: [],
  theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 },
};
const categories: Category[] = [
  { name: "Furniture", slug: "furniture", parentSlug: null },
  { name: "Tables", slug: "tables", parentSlug: "furniture" },
  { name: "Kitchen", slug: "kitchen", parentSlug: null },
];
const product: ProductSummary = {
  id: "listing", slug: "oak-table", name: "Oak Table", price: 499, available: 3,
  rating: 0, reviewCount: 0, imageUrl: "/api/storefront/products/listing/images/photo",
};

beforeAll(async () => { await initializeI18n(); });
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });
afterAll(() => i18n.changeLanguage("en"));

function renderDiscovery(items = categories, item?: ProductSummary) {
  return render(<MemoryRouter><StoreContext value={store}>
    <HomeDiscovery categories={items} product={item} />
  </StoreContext></MemoryRouter>);
}

describe("store discovery", () => {
  it("uses the real brand and links children within their own category, without promotional claims", () => {
    renderDiscovery();
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Wooden Home");
    const directory = screen.getByRole("region", { name: "Browse by category" });
    const table = within(directory).getByRole("link", { name: "Tables" });
    expect(table.getAttribute("href")).toBe("/c/tables");
    expect(table.closest(".category-entry")?.querySelector(".category-entry-title")?.textContent).toContain("Furniture");
    expect(screen.getByRole("link", { name: "Explore products" }).getAttribute("href")).toBe("#shop-products");
    expect(screen.queryByRole("img")).toBeNull();
  });

  it("does not fabricate categories or product imagery for an empty store", () => {
    renderDiscovery([]);
    expect(screen.getByRole("heading", { level: 1 })).toBeTruthy();
    expect(screen.queryByRole("region", { name: "Browse by category" })).toBeNull();
    expect(document.querySelector(".discovery-product")).toBeNull();
  });

  it("renders an actual catalog image and keeps the product link and media frame after an image failure", () => {
    renderDiscovery(categories, product);
    const link = screen.getByRole("link", { name: /Oak Table/ });
    expect(link.getAttribute("href")).toBe("/p/oak-table");
    const image = link.querySelector("img")!;
    expect(image.getAttribute("src")).toBe(product.imageUrl);
    expect(image.getAttribute("width")).toBe("640");
    expect(image.getAttribute("height")).toBe("480");
    expect(image.getAttribute("fetchpriority")).toBe("high");
    fireEvent.error(image);
    expect(within(link).getByText("Image unavailable")).toBeTruthy();
    expect(link.querySelector(".discovery-product-media")).toBeTruthy();
    expect(link.textContent).toContain("499");
    expect(link.textContent).toContain("Kč");
  });

  it("translates discovery controls while retaining the store's names and currency culture", async () => {
    await i18n.changeLanguage("cs");
    renderDiscovery(categories, product);
    expect(screen.getByRole("link", { name: "Prohlédnout produkty" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Procházet podle kategorií" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "Kitchen" }).getAttribute("lang")).toBe("cs-CZ");
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe(store.name);
  });
});

describe("store wordmark", () => {
  it("falls back to the real store name when a supplied logo fails and resets for a new logo", () => {
    const renderBrand = (url: string) => <MemoryRouter><StoreContext value={{ ...store, logoUrl: url }}>
      <StoreBrand />
    </StoreContext></MemoryRouter>;
    const view = render(renderBrand("/broken-logo"));
    fireEvent.error(screen.getByRole("img", { name: store.name }));
    expect(screen.queryByRole("img")).toBeNull();
    expect(screen.getByRole("link", { name: store.name }).textContent).toBe(store.name);
    view.rerender(renderBrand("/new-logo"));
    expect(screen.getByRole("img", { name: store.name }).getAttribute("src")).toBe("/new-logo");
  });
});
