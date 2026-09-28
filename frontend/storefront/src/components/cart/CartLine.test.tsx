// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import type { Cart, CartLine as CartLineModel } from "../../cart";
import { CartContext, type CartState } from "../../cartContext";
import { i18n, initializeI18n } from "../../i18n";
import type { Store } from "../../store";
import { StoreContext } from "../../storeContext";
import { CartLine } from "./CartLine";

const mocks = vi.hoisted(() => ({
  setCartQuantity: vi.fn(),
  removeFromCart: vi.fn(),
}));
vi.mock("../../cart", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../cart")>()),
  setCartQuantity: mocks.setCartQuantity,
  removeFromCart: mocks.removeFromCart,
}));

const store: Store = {
  id: "store",
  name: "Store",
  currency: "EUR",
  culture: "en-IE",
  logoUrl: null,
  theme: { primaryColor: "#000000", secondaryColor: "#ffffff", borderRadius: 4 },
};
const line: CartLineModel = {
  storeProductId: "product-1",
  name: "Oak chair",
  slug: "oak-chair",
  unitPrice: 120,
  quantity: 1,
  lineTotal: 120,
  available: 5,
  imageUrl: null,
};
const cart = (item: CartLineModel | null, changed = false): Cart => ({
  items: item ? [item] : [],
  count: item?.quantity ?? 0,
  itemsTotal: item?.lineTotal ?? 0,
  vatTotal: item ? 20 : 0,
  changed,
  discount: null,
  discountProblem: null,
});

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  mocks.setCartQuantity.mockReset();
  mocks.removeFromCart.mockReset();
});
afterAll(() => i18n.changeLanguage("en"));

function renderLine(mutate: CartState["mutate"] = async (change) => change()) {
  const state: CartState = {
    status: "ready",
    cart: cart(line),
    error: null,
    pending: false,
    adjusted: false,
    acknowledgeAdjustment: vi.fn(),
    apply: vi.fn(),
    reload: vi.fn(),
    mutate,
  };
  const onRemoved = vi.fn();
  render(
    <MemoryRouter>
      <StoreContext value={store}>
        <CartContext value={state}>
          <ul><CartLine line={line} index={0} onRemoved={onRemoved} /></ul>
        </CartContext>
      </StoreContext>
    </MemoryRouter>,
  );
  return onRemoved;
}

describe("CartLine", () => {
  it("rejects a fractional quantity before sending a request", async () => {
    const user = userEvent.setup();
    renderLine();
    const input = screen.getByRole("spinbutton", { name: "Quantity of Oak chair" });
    await user.clear(input);
    await user.type(input, "2.5");
    await user.click(screen.getByRole("button", { name: "Update quantity of Oak chair" }));

    expect(await screen.findByText("Enter an allowed quantity.")).toBeTruthy();
    expect(mocks.setCartQuantity).not.toHaveBeenCalled();
    expect(input).toHaveProperty("value", "2.5");
  });

  it("keeps the edited draft when the server update fails", async () => {
    const user = userEvent.setup();
    renderLine(async () => null);
    const input = screen.getByRole("spinbutton", { name: "Quantity of Oak chair" });
    await user.clear(input);
    await user.type(input, "4");
    await user.click(screen.getByRole("button", { name: "Update quantity of Oak chair" }));

    expect(await screen.findByText("The cart was not changed. Check the error and try again.")).toBeTruthy();
    expect(input).toHaveProperty("value", "4");
  });

  it("reconciles the draft and message to a server-capped quantity", async () => {
    const user = userEvent.setup();
    const updated = { ...line, quantity: 2, lineTotal: 240, available: 2 };
    mocks.setCartQuantity.mockResolvedValue(cart(updated, true));
    renderLine();
    const input = screen.getByRole("spinbutton", { name: "Quantity of Oak chair" });
    await user.clear(input);
    await user.type(input, "5");
    await user.click(screen.getByRole("button", { name: "Update quantity of Oak chair" }));

    expect(mocks.setCartQuantity).toHaveBeenCalledWith("product-1", 5);
    expect(await screen.findByText("Only 2 items of Oak chair are available. The cart was adjusted.")).toBeTruthy();
    expect(input).toHaveProperty("value", "2");
  });

  it("serializes repeated update submissions", async () => {
    const user = userEvent.setup();
    let resolve!: (value: Cart) => void;
    mocks.setCartQuantity.mockImplementation(
      () => new Promise<Cart>((complete) => { resolve = complete; }),
    );
    renderLine();
    const update = screen.getByRole("button", { name: "Update quantity of Oak chair" });
    await user.click(update);
    await user.click(update);
    expect(mocks.setCartQuantity).toHaveBeenCalledTimes(1);
    resolve(cart(line));
    expect(await screen.findByText("Oak chair quantity is now 1 item.")).toBeTruthy();
  });
});
