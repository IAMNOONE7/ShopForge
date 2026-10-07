// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import type { ProductSummary } from "../api";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { ProductCard } from "./ProductCard";

const store: Store = {
  id: "store", name: "Shop", culture: "cs-CZ", currency: "CZK", logoUrl: null,
  providerKeys: [], theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 },
};
const product: ProductSummary = {
  id: "listing", name: "Oak Table", slug: "oak-table", price: 649, available: 3,
  imageUrl: "/api/storefront/products/listing/images/photo", rating: 4.5, reviewCount: 0,
};

beforeAll(async () => { await initializeI18n(); await i18n.changeLanguage("en"); });
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });
afterAll(() => i18n.changeLanguage("en"));

function card(item: ProductSummary, priority = false, brand = store) {
  return <MemoryRouter><StoreContext value={brand}><ProductCard product={item} priority={priority} /></StoreContext></MemoryRouter>;
}

describe("product discovery cards", () => {
  it("shows the real price and aggregate stock without an unreviewed rating claim", () => {
    render(card(product));
    expect(screen.getByRole("link", { name: /Oak Table/ }).getAttribute("href")).toBe("/p/oak-table");
    expect(screen.getByText("649,00 Kč", { exact: false })).toBeTruthy();
    expect(screen.getByText("In stock")).toBeTruthy();
    expect(document.querySelector(".stars")).toBeNull();
    expect(screen.getByText("Oak Table").getAttribute("lang")).toBe("cs-CZ");
  });

  it("preserves a media frame and resets a failed photo when the image URL changes", () => {
    const view = render(card(product));
    const image = document.querySelector("img")!;
    expect(image.getAttribute("loading")).toBe("lazy");
    expect(image.getAttribute("width")).toBe("480");
    expect(image.getAttribute("height")).toBe("480");
    expect(image.getAttribute("alt")).toBe("");
    fireEvent.error(image);
    expect(screen.getByText("Image unavailable")).toBeTruthy();
    expect(document.querySelector(".product-card-media")).toBeTruthy();
    expect(screen.getByRole("link", { name: /Oak Table/ })).toBeTruthy();
    view.rerender(card({ ...product, imageUrl: "/other-photo" }, true));
    expect(document.querySelector("img")?.getAttribute("src")).toBe("/other-photo");
    expect(document.querySelector("img")?.getAttribute("loading")).toBe("eager");
    expect(document.querySelector("img")?.getAttribute("fetchpriority")).toBe("high");
  });

  it("shows only returned reviews and keeps a missing-image sold-out listing discoverable", () => {
    render(card({ ...product, available: 0, imageUrl: null, reviewCount: 2 }));
    expect(screen.getByText("Image unavailable")).toBeTruthy();
    expect(screen.getByText("Out of stock")).toBeTruthy();
    expect(document.querySelector(".stars")?.getAttribute("aria-label")).toContain("2");
    expect(screen.getByRole("link", { name: /Oak Table/ })).toBeTruthy();
  });

  it("uses Czech interface copy independently of the store's price culture", async () => {
    await i18n.changeLanguage("cs");
    render(card({ ...product, available: 0, imageUrl: null, price: 14.9 }, false, { ...store, culture: "en-IE", currency: "EUR" }));
    expect(screen.getByText("Vyprodáno")).toBeTruthy();
    expect(screen.getByText("€14.90")).toBeTruthy();
    expect(screen.getByText("Obrázek není dostupný")).toBeTruthy();
  });
});
