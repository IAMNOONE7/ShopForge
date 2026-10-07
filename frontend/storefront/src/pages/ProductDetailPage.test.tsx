// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import type { ProductDetail } from "../api";
import { CartContext, type CartState } from "../cartContext";
import { CustomerContext, type CustomerState } from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { ProductDetailPage } from "./ProductDetailPage";

const mocks = vi.hoisted(() => ({ getProduct: vi.fn(), getReviews: vi.fn(), getWishlist: vi.fn(), writeReview: vi.fn() }));
vi.mock("../api", async (original) => ({ ...await original<typeof import("../api")>(), getProduct: mocks.getProduct, getReviews: mocks.getReviews }));
vi.mock("../account", async (original) => ({ ...await original<typeof import("../account")>(), getWishlist: mocks.getWishlist, writeReview: mocks.writeReview }));
const store: Store = {
  id: "store", name: "Shop", culture: "cs-CZ", currency: "CZK", logoUrl: null,
  providerKeys: [], theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 },
};
const cart: CartState = {
  status: "ready", cart: null, error: null, pending: false, adjusted: false,
  acknowledgeAdjustment: vi.fn(), apply: vi.fn(), reload: vi.fn(), mutate: vi.fn(),
};
const guest: CustomerState = { status: "guest", customer: null, error: null, apply: vi.fn(), retry: vi.fn() };
const buyer: CustomerState = { ...guest, status: "authenticated", customer: { email: "alice@example.test", firstName: "Alice", lastName: "Novak", phone: null } };
const product: ProductDetail = {
  id: "chair", slug: "chair", name: "Oak chair", description: "Solid oak.\n\n<script>merchant text</script>",
  price: 649, available: 8, optionNames: [], variants: [{ id: "variant", optionValues: [], available: 8 }],
  rating: 0, reviewCount: 0, images: [],
  categories: [
    { name: "Furniture", slug: "furniture", parentSlug: null, path: [{ name: "Furniture", slug: "furniture", parentSlug: null }] },
    { name: "Chairs", slug: "chairs", parentSlug: "furniture", path: [
      { name: "Furniture", slug: "furniture", parentSlug: null }, { name: "Chairs", slug: "chairs", parentSlug: null },
    ] },
  ],
  attributes: [{ code: "material", name: "Material", type: "text", unit: null, value: "Oak" }],
};
beforeAll(initializeI18n);
beforeEach(async () => {
  vi.resetAllMocks();
  await i18n.changeLanguage("en");
  mocks.getProduct.mockResolvedValue(product);
  mocks.getReviews.mockResolvedValue({ canWrite: false, reviews: [] });
  mocks.getWishlist.mockResolvedValue([]);
  mocks.writeReview.mockResolvedValue(undefined);
});
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });

function page(customer = guest, brand = store) {
  return render(<MemoryRouter initialEntries={["/p/old-chair"]}>
    <StoreContext value={brand}><CustomerContext value={customer}><CartContext value={cart}>
      <Link to="/p/another">Next product</Link>
      <Routes><Route path="/p/:slug" element={<ProductDetailPage />} /></Routes>
    </CartContext></CustomerContext></StoreContext>
  </MemoryRouter>);
}

describe("product purchase page", () => {
  it("uses the deepest supplied category trail and renders only real information as plain text", async () => {
    page();
    expect(await screen.findByRole("heading", { level: 1, name: "Oak chair" })).toBeTruthy();
    const trail = screen.getByRole("navigation", { name: "Product breadcrumb" });
    expect(within(trail).getByRole("link", { name: "Furniture" }).getAttribute("href")).toBe("/c/furniture");
    const parent = within(trail).getByRole("link", { name: "Chairs" });
    expect(parent.getAttribute("href")).toBe("/c/chairs");
    expect(parent.closest("li")?.className).toBe("product-breadcrumb-parent");
    expect(within(trail).getByText("Oak chair").getAttribute("aria-current")).toBe("page");
    expect(screen.getByText("649,00 Kč", { exact: false })).toBeTruthy();
    const information = screen.getByRole("navigation", { name: "Product information" });
    expect(within(information).getByRole("link", { name: "Description" }).getAttribute("href")).toBe("#product-description-heading");
    expect(screen.getByText("<script>merchant text</script>")).toBeTruthy();
    expect(document.querySelector(".product-description script")).toBeNull();
    expect(screen.getByRole("rowheader", { name: "Material" })).toBeTruthy();
    expect(screen.getByRole("region", { name: "Buying options" })).toBeTruthy();
    expect(screen.queryByRole("link", { name: /out of 5/ })).toBeNull();
  });

  it("keeps an uncategorized sold-out product clear without empty sections and uses the store price culture", async () => {
    await i18n.changeLanguage("cs");
    mocks.getProduct.mockResolvedValue({ ...product, categories: [], attributes: [], description: null, available: 0,
      variants: [{ id: "variant", optionValues: [], available: 0 }] });
    page(guest, { ...store, culture: "en-IE", currency: "EUR" });
    await screen.findByRole("heading", { name: "Oak chair" });
    expect(screen.getByText("€649.00")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Přidat Oak chair do košíku" })).toHaveProperty("disabled", true);
    expect(screen.queryByRole("navigation", { name: "Informace o produktu" })).toBeNull();
    expect(document.querySelector(".product-information")).toBeNull();
    expect(within(screen.getByRole("navigation", { name: "Drobečková navigace produktu" })).getByRole("link").getAttribute("href")).toBe("/");
  });

  it("starts a new product review flow after navigating away from a submitted review", async () => {
    const user = userEvent.setup();
    mocks.getProduct.mockImplementation((slug: string) => Promise.resolve({ ...product, id: slug, slug }));
    mocks.getReviews.mockResolvedValue({ canWrite: true, reviews: [] });
    page(buyer);
    await user.type(await screen.findByRole("textbox", { name: "Your review" }), "A solid chair.");
    await user.click(screen.getByRole("button", { name: "Send review" }));
    expect(await screen.findByText(/your review will appear once/)).toBeTruthy();
    await user.click(screen.getByRole("link", { name: "Next product" }));
    expect(await screen.findByRole("textbox", { name: "Your review" })).toHaveProperty("value", "");
    expect(screen.queryByText(/your review will appear once/)).toBeNull();
    expect(mocks.getReviews).toHaveBeenCalledWith("another", expect.any(AbortSignal));
    expect(mocks.writeReview).toHaveBeenCalledTimes(1);
  });
});
