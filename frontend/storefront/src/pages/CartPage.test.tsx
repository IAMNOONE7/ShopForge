// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import type { Cart, CartLine } from "../cart";
import { CartContext, type CartState } from "../cartContext";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { CartPage } from "./CartPage";

const mocks = vi.hoisted(() => ({
  setCartQuantity: vi.fn(),
  removeFromCart: vi.fn(),
}));
vi.mock("../cart", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../cart")>()),
  setCartQuantity: mocks.setCartQuantity,
  removeFromCart: mocks.removeFromCart,
}));

const store: Store = {
  id: "store",
  name: "Store",
  currency: "EUR",
  culture: "en-IE",
  logoUrl: null,
  providerKeys: [],
  theme: { primaryColor: "#000000", secondaryColor: "#ffffff", borderRadius: 4 },
};
const oak: CartLine = {
  storeProductId: "oak",
  variantId: "oak-variant",
  optionValues: [],
  name: "Oak chair",
  slug: "oak-chair",
  unitPrice: 120,
  quantity: 1,
  lineTotal: 120,
  available: 10,
  imageUrl: null,
};
const beech: CartLine = {
  storeProductId: "beech",
  variantId: "beech-variant",
  optionValues: [],
  name: "Beech table",
  slug: "beech-table",
  unitPrice: 300,
  quantity: 1,
  lineTotal: 300,
  available: 10,
  imageUrl: null,
};
function makeCart(items: CartLine[]): Cart {
  return {
    items,
    count: items.reduce((sum, item) => sum + item.quantity, 0),
    itemsTotal: items.reduce((sum, item) => sum + item.lineTotal, 0),
    vatTotal: items.reduce((sum, item) => sum + item.lineTotal / 6, 0),
    changed: false,
    discount: null,
    discountProblem: null,
  };
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  mocks.setCartQuantity.mockReset();
  mocks.removeFromCart.mockReset();
  window.sessionStorage.clear();
});
afterAll(() => i18n.changeLanguage("en"));

function StatefulCart({ children, initial }: { children: ReactNode; initial: Cart }) {
  const [cart, setCart] = useState(initial);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<unknown | null>(null);
  const state: CartState = {
    status: "ready",
    cart,
    error,
    pending,
    adjusted: false,
    acknowledgeAdjustment: vi.fn(),
    apply: setCart,
    reload: vi.fn(),
    mutate: async (change) => {
      setPending(true);
      setError(null);
      try {
        const updated = await change();
        setCart(updated);
        return updated;
      } catch (cause) {
        setError(cause);
        return null;
      } finally {
        setPending(false);
      }
    },
  };
  return <CartContext value={state}>{children}</CartContext>;
}

function renderPage(initial: Cart) {
  return render(
    <MemoryRouter>
      <StoreContext value={store}>
        <StatefulCart initial={initial}>
          <CartPage />
        </StatefulCart>
      </StoreContext>
    </MemoryRouter>,
  );
}

describe("CartPage", () => {
  it("keeps two forms of one listing independent when updating and removing", async () => {
    const user = userEvent.setup();
    const red: CartLine = {
      ...oak, storeProductId: "hoodie", variantId: "hoodie-red",
      optionValues: ["S", "Red"], name: "Hoodie", slug: "hoodie",
    };
    const blue: CartLine = {
      ...red, variantId: "hoodie-blue", optionValues: ["M", "Blue"],
    };
    mocks.setCartQuantity.mockResolvedValue(makeCart([
      { ...red, quantity: 2, lineTotal: 240 }, blue,
    ]));
    mocks.removeFromCart.mockResolvedValue(makeCart([blue]));
    renderPage(makeCart([red, blue]));

    expect(screen.getByText("S / Red")).toBeTruthy();
    expect(screen.getByText("M / Blue")).toBeTruthy();
    const redQuantity = screen.getByRole("spinbutton", { name: "Quantity of Hoodie (S / Red)" });
    const blueQuantity = screen.getByRole("spinbutton", { name: "Quantity of Hoodie (M / Blue)" });
    await user.clear(redQuantity);
    await user.type(redQuantity, "2");
    await user.click(screen.getByRole("button", { name: "Update quantity of Hoodie (S / Red)" }));
    expect(mocks.setCartQuantity).toHaveBeenCalledWith("hoodie", "hoodie-red", 2);
    await waitFor(() => expect(redQuantity).toHaveProperty("value", "2"));
    expect(blueQuantity).toHaveProperty("value", "1");

    await user.click(screen.getByRole("button", { name: "Remove Hoodie (S / Red) from cart" }));
    expect(mocks.removeFromCart).toHaveBeenCalledWith("hoodie", "hoodie-red");
    await waitFor(() => expect(document.activeElement).toBe(
      document.getElementById("cart-line-link-hoodie:hoodie-blue"),
    ));
    expect(screen.getByText("M / Blue")).toBeTruthy();
    expect(screen.queryByText("S / Red")).toBeNull();
  });

  it("keeps a provider-cancelled order discoverable from the cart", () => {
    const path =
      "/order/2026-1?token=01a0ddab-3a87-70e9-8b84-6513eff79718";
    window.sessionStorage.setItem(
      "shopforge.checkout.recovery",
      JSON.stringify({ orderPath: path }),
    );
    renderPage(makeCart([]));

    expect(screen.getByText("Your order was already created")).toBeTruthy();
    expect(
      screen.getByRole("link", { name: "View your order" }).getAttribute("href"),
    ).toBe(path);
    expect(window.sessionStorage.getItem("shopforge.checkout.recovery")).not.toBeNull();
  });

  it("moves focus to the next row and then the empty heading after removals", async () => {
    const user = userEvent.setup();
    mocks.removeFromCart
      .mockResolvedValueOnce(makeCart([beech]))
      .mockResolvedValueOnce(makeCart([]));
    renderPage(makeCart([oak, beech]));

    await user.click(screen.getByRole("button", { name: "Remove Oak chair from cart" }));
    const next = await screen.findByRole("link", { name: "Beech table" });
    await waitFor(() => expect(document.activeElement).toBe(next));

    await user.click(screen.getByRole("button", { name: "Remove Beech table from cart" }));
    const empty = await screen.findByRole("heading", { name: "Your cart is empty" });
    await waitFor(() => expect(document.activeElement).toBe(empty));
    expect(screen.getByRole("link", { name: "Browse all products" })).toBeTruthy();
  });

  it("retains the server cart totals and edited draft after an offline update", async () => {
    const user = userEvent.setup();
    mocks.setCartQuantity.mockRejectedValue(new TypeError("offline"));
    renderPage(makeCart([oak]));
    const input = screen.getByRole("spinbutton", { name: "Quantity of Oak chair" });

    await user.clear(input);
    await user.type(input, "4");
    await user.click(screen.getByRole("button", { name: "Update quantity of Oak chair" }));

    expect(await screen.findByText("The change could not be saved. Please try again.")).toBeTruthy();
    expect(input).toHaveProperty("value", "4");
    expect(document.querySelector(".cart-total strong")?.textContent).toBe("€120.00");
  });
});
