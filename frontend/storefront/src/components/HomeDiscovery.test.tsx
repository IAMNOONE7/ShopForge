// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import type { Category, ProductSummary } from "../api";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { formatPrice, StoreContext } from "../storeContext";
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
  it.each(["cs", "en"])("shows the current store threshold with qualified availability in %s", async language => {
    await i18n.changeLanguage(language);
    const view = render(<MemoryRouter><StoreContext value={{ ...store, freeShippingFrom: 1234.56 }}><HomeDiscovery categories={[]} /></StoreContext></MemoryRouter>);
    const note = document.querySelector(".discovery-delivery")!;
    expect(note.querySelector(".currency-value")?.textContent).toBe(formatPrice(1234.56, store));
    expect(note.textContent).toContain(i18n.t("discovery:deliveryAvailability"));
    expect(screen.getByRole("link", { name: i18n.t("discovery:exploreProducts") }).getAttribute("href")).toBe("#shop-products");
    view.rerender(<MemoryRouter><StoreContext value={{ ...store, freeShippingFrom: null }}><HomeDiscovery categories={[]} /></StoreContext></MemoryRouter>);
    expect(document.querySelector(".discovery-delivery")).toBeNull();
  });

  it.each([undefined, null, 0, -1, Infinity, NaN])("does not advertise an unavailable or malformed threshold %s", freeShippingFrom => {
    render(<MemoryRouter><StoreContext value={{ ...store, freeShippingFrom }}><HomeDiscovery categories={[]} /></StoreContext></MemoryRouter>);
    expect(document.querySelector(".discovery-delivery")).toBeNull();
  });

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

  it("gives each root a heading, names its immediate children and gives leaf categories a real browse action", () => {
    renderDiscovery([...categories, { name: "Dining tables", slug: "dining", parentSlug: "tables" }]);
    expect(screen.getByRole("heading", { level: 3, name: "Furniture" })).toBeTruthy();
    expect(screen.getByRole("heading", { level: 3, name: "Kitchen" })).toBeTruthy();
    const children = screen.getByRole("list", { name: "Subcategories of Furniture" });
    expect(within(children).getByRole("link", { name: "Tables" }).getAttribute("href")).toBe("/c/tables");
    expect(within(children).queryByRole("link", { name: "Dining tables" })).toBeNull();
    expect(screen.getByRole("link", { name: "Browse products in Kitchen" }).getAttribute("href")).toBe("/c/kitchen");
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

  it.each([
    ["en", "cs-CZ", "CZK", 123456789.12],
    ["cs", "en-US", "USD", 123456789.12],
    ["en", "en-KW", "KWD", 123456789.123],
    ["cs", "ja-JP", "JPY", 123456789],
    ["cs", "cs-CZ", "CZK", 0],
  ])("preserves the featured price with %s UI and %s/%s store formatting", async (language, culture, currency, price) => {
    await i18n.changeLanguage(language);
    const brand = { ...store, culture, currency };
    render(<MemoryRouter><StoreContext value={brand}>
      <HomeDiscovery categories={categories} product={{ ...product, price }} />
    </StoreContext></MemoryRouter>);
    const link = screen.getByRole("link", { name: /Oak Table/ });
    const value = link.querySelector("strong .currency-value")!;
    expect(value.textContent).toBe(formatPrice(price, brand));
    expect(value.getAttribute("lang")).toBe(culture);
    expect(link.getAttribute("href")).toBe("/p/oak-table");
  });

  it("adds only returned imagery, resets a failed image on a new URL and removes an absent feature", () => {
    const discovery = (item?: ProductSummary) => <MemoryRouter><StoreContext value={store}>
      <HomeDiscovery categories={categories} product={item} />
    </StoreContext></MemoryRouter>;
    const view = render(discovery());
    expect(document.querySelector(".discovery-product")).toBeNull();
    view.rerender(discovery(product));
    const image = document.querySelector(".discovery-product img")!;
    expect(image.getAttribute("src")).toBe(product.imageUrl);
    fireEvent.error(image);
    expect(screen.getByText("Image unavailable")).toBeTruthy();
    view.rerender(discovery({ ...product, imageUrl: "/replacement-photo" }));
    expect(document.querySelector(".discovery-product img")?.getAttribute("src")).toBe("/replacement-photo");
    expect(screen.queryByText("Image unavailable")).toBeNull();
    view.rerender(discovery());
    expect(document.querySelector(".discovery-product")).toBeNull();
    expect(screen.getByRole("link", { name: "Explore products" }).getAttribute("href")).toBe("#shop-products");
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
