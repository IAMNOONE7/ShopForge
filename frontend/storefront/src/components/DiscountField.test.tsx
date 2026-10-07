// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import type { Cart } from "../cart";
import { CartContext, type CartState } from "../cartContext";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { DiscountField } from "./DiscountField";

const mocks = vi.hoisted(() => ({ applyDiscount: vi.fn(), removeDiscount: vi.fn() }));
vi.mock("../cart", async original => ({ ...await original<typeof import("../cart")>(), ...mocks }));
const store: Store = { id: "store", name: "Shop", culture: "en-IE", currency: "EUR", logoUrl: null, providerKeys: [],
  theme: { primaryColor: "#000000", secondaryColor: "#ffffff", borderRadius: 4 } };
const cart: Cart = { items: [], count: 0, itemsTotal: 0, vatTotal: 0, changed: false, discount: null, discountProblem: null };
function Field({ initial = cart }: { initial?: Cart }) {
  const [current, setCurrent] = useState(initial);
  const [pending, setPending] = useState(false);
  const state: CartState = { status: "ready", cart: current, error: null, pending, adjusted: false,
    acknowledgeAdjustment: vi.fn(), apply: setCurrent, reload: vi.fn(), mutate: async change => {
      setPending(true);
      try { const result = await change(); setCurrent(result); return result; }
      catch { return null; }
      finally { setPending(false); }
    } };
  return <StoreContext value={store}><CartContext value={state}><DiscountField /></CartContext></StoreContext>;
}
beforeAll(initializeI18n);
beforeEach(async () => { vi.resetAllMocks(); await i18n.changeLanguage("en"); });
afterEach(cleanup);

describe("discount entry", () => {
  it("rejects an empty code and retains a failed draft without double-applying while pending", async () => {
    const user = userEvent.setup();
    render(<Field />);
    await user.click(screen.getByRole("button", { name: "Apply" }));
    expect(mocks.applyDiscount).not.toHaveBeenCalled();
    const input = screen.getByRole("textbox", { name: "Discount code" });
    expect(input.getAttribute("aria-invalid")).toBe("true");
    await user.type(input, " SAVE ");
    let reject!: () => void;
    mocks.applyDiscount.mockImplementation(() => new Promise((_, fail) => { reject = () => fail(new Error("offline")); }));
    await user.click(screen.getByRole("button", { name: "Apply" }));
    expect(screen.getByRole("button", { name: "Applying…" })).toHaveProperty("disabled", true);
    expect(input).toHaveProperty("readOnly", true);
    expect(mocks.applyDiscount).toHaveBeenCalledOnce();
    expect(mocks.applyDiscount).toHaveBeenCalledWith("SAVE");
    reject();
    await waitFor(() => expect(screen.getByRole("button", { name: "Apply" })).toHaveProperty("disabled", false));
    expect(input).toHaveProperty("value", " SAVE ");
    expect(screen.getByText("This discount code could not be applied.")).toBeTruthy();
    mocks.applyDiscount.mockResolvedValue({ ...cart, discount: { code: "SAVE", name: "Savings", amount: 12 } });
    await user.click(screen.getByRole("button", { name: "Apply" }));
    expect(await screen.findByText("Code applied")).toBeTruthy();
    expect(screen.getByText("Code SAVE applied.")).toBeTruthy();
    expect(screen.getByText("SAVE")).toBeTruthy();
    expect(screen.queryByText("This discount code could not be applied.")).toBeNull();
  });

  it("preserves a returned discount after failed removal and allows clearing an invalid server code", async () => {
    const user = userEvent.setup();
    mocks.removeDiscount.mockRejectedValueOnce(new Error("offline")).mockResolvedValue(cart);
    const view = render(<Field initial={{ ...cart, discount: { code: "SAVE", name: "Savings", amount: 12 } }} />);
    await user.click(screen.getByRole("button", { name: "Remove code" }));
    expect(await screen.findByText("The discount code could not be removed. Please try again.")).toBeTruthy();
    expect(screen.getByText("SAVE")).toBeTruthy();
    view.unmount();
    render(<Field initial={{ ...cart, discountProblem: "A backend explanation" }} />);
    expect(screen.queryByText("A backend explanation")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Remove code" }));
    await waitFor(() => expect(screen.queryByText("This discount code could not be applied.")).toBeNull());
    expect(screen.getByText("Discount code removed.")).toBeTruthy();
    expect(mocks.removeDiscount).toHaveBeenCalledTimes(2);
  });
});
