// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it } from "vitest";
import { initializeI18n, i18n } from "../../i18n";
import { StoreContext } from "../../storeContext";
import type { Store } from "../../store";
import { FreeShippingProgress } from "./FreeShippingProgress";
const store: Store = { id: "store", name: "Store", currency: "CZK", culture: "cs-CZ", logoUrl: null, providerKeys: [], theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 4 } };
beforeAll(initializeI18n);
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });
describe("free-delivery progress presentation", () => {
  it.each(["cs", "en"])("shows the returned remaining amount in %s with the store's money formatting", async language => {
    await i18n.changeLanguage(language);
    render(<StoreContext value={store}><FreeShippingProgress value={{ threshold: 1000, remaining: 123.45 }} /></StoreContext>);
    expect(screen.getByRole("heading", { name: i18n.t("cart:freeShippingHeading") })).toBeTruthy();
    const amounts = [...document.querySelectorAll(".currency-value")].map(node => node.textContent);
    const format = new Intl.NumberFormat(store.culture, { style: "currency", currency: store.currency });
    expect(amounts).toEqual([format.format(123.45), format.format(1000)]);
    expect(document.querySelector('[aria-live="polite"]')).toBeTruthy();
  });
  it.each(["cs", "en"])("describes a reached threshold without promising an available delivery method in %s", async language => {
    await i18n.changeLanguage(language); render(<StoreContext value={store}><FreeShippingProgress value={{ threshold: 1000, remaining: 0 }} /></StoreContext>);
    expect(screen.getByRole("heading", { name: i18n.t("cart:freeShippingReached") })).toBeTruthy();
    expect(screen.getByText(i18n.t("cart:freeShippingAvailability"))).toBeTruthy();
    expect(document.querySelectorAll(".currency-value")).toHaveLength(1);
  });
  it.each([undefined, null])("does not invent progress when it is %j", value => {
    render(<StoreContext value={store}><FreeShippingProgress value={value} /></StoreContext>); expect(document.querySelector(".free-shipping-progress")).toBeNull();
  });
  it("hides an old quote during a cart change and then announces only the new server state", () => {
    const view = render(<StoreContext value={store}><FreeShippingProgress value={{ threshold: 1000, remaining: 123.45 }} pending /></StoreContext>);
    expect(document.querySelector(".free-shipping-progress")).toBeNull();
    view.rerender(<StoreContext value={store}><FreeShippingProgress value={{ threshold: 1000, remaining: 0 }} /></StoreContext>);
    expect(screen.getByRole("heading", { name: i18n.t("cart:freeShippingReached") })).toBeTruthy();
  });
  it.each(["JPY", "KWD"])("uses %s precision without assuming the UI currency", currency => {
    render(<StoreContext value={{ ...store, currency, culture: "en" }}><FreeShippingProgress value={{ threshold: 1000, remaining: currency === "JPY" ? 123 : 0.123 }} /></StoreContext>);
    const format = new Intl.NumberFormat("en", { style: "currency", currency });
    expect(document.querySelector(".currency-value")?.textContent).toBe(format.format(currency === "JPY" ? 123 : 0.123));
  });
});
