// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import type { ProductDetail } from "../../api";
import type { Cart } from "../../cart";
import { CartContext, type CartState } from "../../cartContext";
import { CustomerContext, type CustomerState } from "../../customerContext";
import { i18n, initializeI18n } from "../../i18n";
import type { Store } from "../../store";
import { StoreContext } from "../../storeContext";
import { ProductPurchase } from "./ProductPurchase";

const mocks = vi.hoisted(() => ({ addToCart: vi.fn() }));
vi.mock("../../cart", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../cart")>()),
  addToCart: mocks.addToCart,
}));

const store: Store = {
  id: "store-a",
  name: "Store",
  currency: "CZK",
  culture: "cs-CZ",
  logoUrl: null,
  providerKeys: [],
  theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 4 },
};
const emptyCart: Cart = {
  items: [], count: 0, itemsTotal: 0, vatTotal: 0,
  changed: false, discount: null, discountProblem: null,
};
const cartState: CartState = {
  status: "ready", cart: emptyCart, error: null, pending: false, adjusted: false,
  acknowledgeAdjustment: vi.fn(), apply: vi.fn(), reload: vi.fn(),
  mutate: async (change) => change(),
};
const guest: CustomerState = {
  status: "guest", customer: null, error: null, apply: vi.fn(), retry: vi.fn(),
};
const product: ProductDetail = {
  id: "listing-a",
  slug: "hoodie",
  name: "Hoodie",
  description: null,
  price: 499,
  available: 10,
  optionNames: ["Size", "Colour"],
  variants: [
    { id: "small-red", optionValues: ["Small", "Red"], available: 0 },
    { id: "medium-red", optionValues: ["Medium", "Red"], available: 3 },
    { id: "medium-blue", optionValues: ["Medium", "Blue"], available: 7 },
  ],
  rating: 0,
  reviewCount: 0,
  images: [],
  categories: [],
  attributes: [],
};

function renderPurchase(item: ProductDetail = product, state = cartState) {
  return render(
    <MemoryRouter>
      <StoreContext value={store}>
        <CustomerContext value={guest}>
          <CartContext value={state}>
            <ProductPurchase product={item} />
          </CartContext>
        </CustomerContext>
      </StoreContext>
    </MemoryRouter>,
  );
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  mocks.addToCart.mockReset();
});
afterAll(() => i18n.changeLanguage("en"));

describe("variant purchase", () => {
  it("shows the exact completed choice and keeps options and quantity locked during a cart mutation", async () => {
    const user = userEvent.setup();
    const view = renderPurchase();
    await user.click(screen.getByRole("radio", { name: "Medium" }));
    expect(screen.queryByText("Medium / Red")).toBeNull();
    await user.click(screen.getByRole("radio", { name: "Red" }));
    expect(screen.getByText("Medium / Red").getAttribute("lang")).toBe("cs-CZ");
    expect(screen.getByText("Selected options")).toBeTruthy();
    view.unmount();
    renderPurchase(product, { ...cartState, pending: true });
    expect(screen.getByRole("group", { name: "Size" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("group", { name: "Colour" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("spinbutton", { name: "Quantity" })).toHaveProperty("disabled", true);
    expect(mocks.addToCart).not.toHaveBeenCalled();
  });
  it("requires every option, clears an impossible combination, and blocks sold-out forms", async () => {
    const user = userEvent.setup();
    renderPurchase();
    const add = screen.getByRole("button", { name: "Add Hoodie to cart" });
    expect(add).toHaveProperty("disabled", true);
    expect(screen.getAllByText("Choose each option to see availability.").length).toBeGreaterThan(0);

    const small = screen.getByRole("radio", { name: /Small/ });
    small.focus();
    await user.keyboard(" ");
    expect(small).toHaveProperty("checked", true);
    await user.click(screen.getByRole("radio", { name: "Blue" }));
    expect(small).toHaveProperty("checked", false);
    expect(screen.getByText(/Other choices were cleared/)).toBeTruthy();
    expect(add).toHaveProperty("disabled", true);

    await user.click(screen.getByRole("radio", { name: "Small Out of stock" }));
    await user.click(screen.getByRole("radio", { name: "Red" }));
    expect(screen.getAllByText("Out of stock").length).toBeGreaterThan(0);
    expect(add).toHaveProperty("disabled", true);
    expect(mocks.addToCart).not.toHaveBeenCalled();
  });

  it("caps quantity using selected stock and sends its variant ID", async () => {
    const user = userEvent.setup();
    renderPurchase();
    await user.click(screen.getByRole("radio", { name: "Medium" }));
    await user.click(screen.getByRole("radio", { name: "Red" }));
    expect(screen.getByText("Only 3 left in stock")).toBeTruthy();
    const quantity = screen.getByRole("spinbutton", { name: "Quantity" });
    expect(quantity).toHaveProperty("max", "3");
    await user.clear(quantity);
    await user.type(quantity, "4");
    await user.click(screen.getByRole("button", { name: "Add Hoodie to cart" }));
    expect(await screen.findByText("Enter an allowed quantity.")).toBeTruthy();
    expect(mocks.addToCart).not.toHaveBeenCalled();

    await user.click(screen.getByRole("radio", { name: "Blue" }));
    expect(screen.getByText("7 items in stock")).toBeTruthy();
    const nextQuantity = screen.getByRole("spinbutton", { name: "Quantity" });
    expect(nextQuantity).toHaveProperty("value", "1");
    await user.clear(nextQuantity);
    await user.type(nextQuantity, "4");
    mocks.addToCart.mockResolvedValue({
      ...emptyCart,
      count: 4,
      itemsTotal: 1996,
      items: [{
        storeProductId: "listing-a", variantId: "medium-blue", optionValues: ["Medium", "Blue"], name: "Hoodie", slug: "hoodie",
        unitPrice: 499, quantity: 4, lineTotal: 1996, available: 7, imageUrl: null,
      }],
    });
    await user.click(screen.getByRole("button", { name: "Add Hoodie to cart" }));
    await waitFor(() => expect(mocks.addToCart).toHaveBeenCalledWith("listing-a", "medium-blue", 4));
    expect(await screen.findByText("Added 4 items to your cart.", { exact: false })).toBeTruthy();
  });

  it("keeps a single-variant product simple and sends its ID", async () => {
    const user = userEvent.setup();
    renderPurchase({
      ...product,
      optionNames: [],
      variants: [{ id: "only-form", optionValues: [], available: 2 }],
      available: 2,
    });
    expect(screen.queryByRole("radio")).toBeNull();
    expect(screen.getByText("Only 2 left in stock")).toBeTruthy();
    mocks.addToCart.mockResolvedValue({
      ...emptyCart,
      count: 1,
      itemsTotal: 499,
      items: [{
        storeProductId: "listing-a", variantId: "only-form", optionValues: [], name: "Hoodie", slug: "hoodie",
        unitPrice: 499, quantity: 1, lineTotal: 499, available: 2, imageUrl: null,
      }],
    });
    await user.click(screen.getByRole("button", { name: "Add Hoodie to cart" }));
    await waitFor(() => expect(mocks.addToCart).toHaveBeenCalledWith("listing-a", "only-form", 1));
  });

  it("does not guess which variant to buy when the backend supplies no usable combination", () => {
    renderPurchase({
      ...product,
      optionNames: [],
      variants: [
        { id: "form-a", optionValues: [], available: 2 },
        { id: "form-b", optionValues: [], available: 3 },
      ],
    });
    expect(screen.queryByRole("radio")).toBeNull();
    expect(screen.queryByRole("button", { name: "Add Hoodie to cart" })).toBeNull();
    expect(screen.getByText(/options are unavailable right now/)).toBeTruthy();
  });

  it("uses Czech UI copy while leaving store-owned option values as supplied", async () => {
    await i18n.changeLanguage("cs");
    renderPurchase();
    expect(screen.getAllByText("Zvolte každou možnost pro zobrazení dostupnosti.").length).toBeGreaterThan(0);
    expect(screen.getByRole("radio", { name: "Medium" })).toBeTruthy();
    await i18n.changeLanguage("en");
  });
});
