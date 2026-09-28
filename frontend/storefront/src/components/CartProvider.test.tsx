// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Cart } from "../cart";
import { useCart } from "../cartContext";
import { CartProvider } from "./CartProvider";

const mocks = vi.hoisted(() => ({ getCart: vi.fn() }));
vi.mock("../cart", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../cart")>()),
  getCart: mocks.getCart,
}));

const emptyCart: Cart = {
  items: [],
  count: 0,
  itemsTotal: 0,
  vatTotal: 0,
  changed: false,
  discount: null,
  discountProblem: null,
};

afterEach(() => {
  cleanup();
  mocks.getCart.mockReset();
  window.sessionStorage.clear();
});

function Probe() {
  const { status, adjusted, reload, acknowledgeAdjustment } = useCart();
  return (
    <>
      <output>{status}</output>
      {adjusted && <span>adjusted</span>}
      <button type="button" onClick={reload}>Reload</button>
      <button type="button" onClick={acknowledgeAdjustment}>Acknowledge</button>
    </>
  );
}

describe("CartProvider reconciliation notice", () => {
  it("keeps a server adjustment through later clear responses until acknowledged", async () => {
    const user = userEvent.setup();
    mocks.getCart
      .mockResolvedValueOnce({ ...emptyCart, changed: true })
      .mockResolvedValueOnce(emptyCart);

    render(<CartProvider><Probe /></CartProvider>);
    expect(await screen.findByText("adjusted")).toBeTruthy();
    expect(window.sessionStorage.getItem("shopforge.cart.adjusted")).toBe("true");

    await user.click(screen.getByRole("button", { name: "Reload" }));
    await waitFor(() => expect(mocks.getCart).toHaveBeenCalledTimes(2));
    expect(screen.getByText("adjusted")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Acknowledge" }));
    expect(screen.queryByText("adjusted")).toBeNull();
    expect(window.sessionStorage.getItem("shopforge.cart.adjusted")).toBeNull();
  });
});
