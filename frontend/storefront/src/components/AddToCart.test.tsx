// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import type { Cart } from "../cart";
import { CartContext, type CartState } from "../cartContext";
import { i18n, initializeI18n } from "../i18n";
import { AddToCart } from "./AddToCart";

const mocks = vi.hoisted(() => ({ addToCart: vi.fn() }));
vi.mock("../cart", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../cart")>()),
  addToCart: mocks.addToCart,
}));

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  mocks.addToCart.mockReset();
});
afterAll(() => i18n.changeLanguage("en"));

const emptyCart: Cart = {
  items: [],
  count: 0,
  itemsTotal: 0,
  vatTotal: 0,
  changed: false,
  discount: null,
  discountProblem: null,
};

function cartWith(quantity: number): Cart {
  return {
    ...emptyCart,
    items: [
      {
        storeProductId: "product-1",
        name: "Oak chair",
        slug: "oak-chair",
        unitPrice: 120,
        quantity,
        lineTotal: quantity * 120,
        available: 10,
        imageUrl: null,
      },
    ],
    count: quantity,
  };
}

function renderControl(options: {
  available?: number;
  cart?: Cart | null;
  mutate?: CartState["mutate"];
} = {}) {
  const mutate = options.mutate ?? (async (change) => change());
  const state: CartState = {
    status: "ready",
    cart: options.cart === undefined ? emptyCart : options.cart,
    error: null,
    pending: false,
    adjusted: false,
    acknowledgeAdjustment: vi.fn(),
    apply: vi.fn(),
    reload: vi.fn(),
    mutate,
  };
  return render(
    <MemoryRouter>
      <CartContext value={state}>
        <AddToCart
          storeProductId="product-1"
          productName="Oak chair"
          available={options.available ?? 10}
        />
      </CartContext>
    </MemoryRouter>,
  );
}

function PendingCartProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState(false);
  const state: CartState = {
    status: "ready",
    cart: emptyCart,
    error: null,
    pending,
    adjusted: false,
    acknowledgeAdjustment: vi.fn(),
    apply: vi.fn(),
    reload: vi.fn(),
    mutate: async (change) => {
      setPending(true);
      try {
        return await change();
      } finally {
        setPending(false);
      }
    },
  };
  return <CartContext value={state}>{children}</CartContext>;
}

describe("AddToCart", () => {
  it("submits the chosen quantity and reports the amount actually added", async () => {
    const user = userEvent.setup();
    mocks.addToCart.mockResolvedValue(cartWith(4));
    renderControl();

    const quantity = screen.getByRole("spinbutton", { name: "Quantity" });
    await user.clear(quantity);
    await user.type(quantity, "4");
    await user.click(screen.getByRole("button", { name: "Add Oak chair to cart" }));

    expect(mocks.addToCart).toHaveBeenCalledWith("product-1", 4);
    expect(await screen.findByText("Added 4 items to your cart.", { exact: false })).toBeTruthy();
    expect(screen.getByRole("link", { name: "View cart" })).toBeTruthy();
  });

  it("reports a server-capped result from the before and after line quantities", async () => {
    const user = userEvent.setup();
    mocks.addToCart.mockResolvedValue(cartWith(3));
    renderControl({ cart: cartWith(1) });

    const quantity = screen.getByRole("spinbutton", { name: "Quantity" });
    await user.clear(quantity);
    await user.type(quantity, "4");
    await user.click(screen.getByRole("button", { name: "Add Oak chair to cart" }));

    expect(
      await screen.findByText(
        "Added 2 items. Stock changed, so the quantity was limited.",
        { exact: false },
      ),
    ).toBeTruthy();
  });

  it("keeps an out-of-stock quantity and add action disabled", () => {
    renderControl({ available: 0 });
    expect(screen.getByRole("spinbutton", { name: "Quantity" })).toHaveProperty(
      "disabled",
      true,
    );
    expect(
      screen.getByRole("button", { name: "Add Oak chair to cart" }),
    ).toHaveProperty("disabled", true);
  });

  it("rejects quantities outside the current stock range without a request", async () => {
    const user = userEvent.setup();
    renderControl({ available: 5 });
    const quantity = screen.getByRole("spinbutton", { name: "Quantity" });
    await user.clear(quantity);
    await user.type(quantity, "6");
    await user.click(screen.getByRole("button", { name: "Add Oak chair to cart" }));

    expect(await screen.findByText("Enter an allowed quantity.")).toBeTruthy();
    expect(mocks.addToCart).not.toHaveBeenCalled();
  });

  it("ignores a repeated submit while the first cart write is pending", async () => {
    const user = userEvent.setup();
    let resolve!: (cart: Cart) => void;
    mocks.addToCart.mockImplementation(
      () => new Promise<Cart>((complete) => { resolve = complete; }),
    );
    renderControl();
    const button = screen.getByRole("button", { name: "Add Oak chair to cart" });

    await user.click(button);
    await user.click(button);
    expect(mocks.addToCart).toHaveBeenCalledTimes(1);

    resolve(cartWith(1));
    await waitFor(() => expect(screen.getByText("Added 1 item to your cart.", { exact: false })).toBeTruthy());
  });

  it("keeps focus on the guarded add button while its cart write is pending", async () => {
    const user = userEvent.setup();
    let resolve!: (cart: Cart) => void;
    mocks.addToCart.mockImplementation(
      () => new Promise<Cart>((complete) => { resolve = complete; }),
    );
    render(
      <MemoryRouter>
        <PendingCartProvider>
          <AddToCart
            storeProductId="product-1"
            productName="Oak chair"
            available={10}
          />
        </PendingCartProvider>
      </MemoryRouter>,
    );
    const button = screen.getByRole("button", { name: "Add Oak chair to cart" });

    await user.click(button);
    await waitFor(() => expect(button.getAttribute("aria-disabled")).toBe("true"));
    expect(document.activeElement).toBe(button);
    expect(button).toHaveProperty("disabled", false);

    resolve(cartWith(1));
    await screen.findByText("Added 1 item to your cart.", { exact: false });
  });
});
