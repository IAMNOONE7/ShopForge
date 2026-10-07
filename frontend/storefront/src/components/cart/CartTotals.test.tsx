// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it } from "vitest";
import type { Cart } from "../../cart";
import { i18n, initializeI18n } from "../../i18n";
import type { Store } from "../../store";
import { StoreContext } from "../../storeContext";
import { CartTotals } from "./CartTotals";

const store: Store = { id: "store", name: "Shop", culture: "en-IE", currency: "EUR", logoUrl: null, providerKeys: [],
  theme: { primaryColor: "#000000", secondaryColor: "#ffffff", borderRadius: 4 } };
const cart: Cart = { items: [{ storeProductId: "hoodie", variantId: "red", name: "Hoodie", slug: "hoodie", optionValues: ["S", "Red"],
  quantity: 2, unitPrice: 60, lineTotal: 120, available: 8, imageUrl: null }], count: 2, itemsTotal: 108, vatTotal: 18,
  changed: false, discount: { code: "SAVE", name: "Ten percent", amount: 12 }, discountProblem: null };
beforeAll(initializeI18n);
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });

describe("shopping totals", () => {
  it("shows the returned line subtotal and discount while preserving the already discounted server total", () => {
    render(<StoreContext value={store}><CartTotals cart={cart} /></StoreContext>);
    expect(screen.getByText("€120.00")).toBeTruthy();
    expect(screen.getByText("−€12.00")).toBeTruthy();
    expect(document.querySelector(".cart-total strong")?.textContent).toBe("€108.00");
    expect(screen.getByText("Includes €18.00 VAT. Shipping is added at checkout.")).toBeTruthy();
  });

  it("keeps shipping separate and labels the preview when the discount contract cannot quote shipping savings", () => {
    render(<StoreContext value={store}><CartTotals cart={{ ...cart, itemsTotal: 120, discount: { ...cart.discount!, amount: 0 } }}
      shipping={{ code: "courier", name: "Courier", price: 10, requiresPickupPoint: false, pickupPointChoice: "none" }} /></StoreContext>);
    expect(screen.getByText("Code applied")).toBeTruthy();
    expect(screen.getByText("€10.00")).toBeTruthy();
    expect(screen.getByText("Estimated total")).toBeTruthy();
    expect(document.querySelector(".checkout-total strong")?.textContent).toBe("€130.00");
    expect(screen.getByText(/including any shipping discount/)).toBeTruthy();
    expect(screen.queryByText("−€0.00")).toBeNull();
  });

  it("translates labels into Czech while retaining the store's EUR formatting", async () => {
    await i18n.changeLanguage("cs");
    render(<StoreContext value={store}><CartTotals cart={cart} /></StoreContext>);
    expect(screen.getByText("Zboží (2)")).toBeTruthy();
    expect(screen.getByText("Sleva")).toBeTruthy();
    expect(document.querySelector(".cart-total strong")?.textContent).toBe("€108.00");
  });
});
