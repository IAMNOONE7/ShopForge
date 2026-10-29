// @vitest-environment jsdom
import { cleanup, render } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it } from "vitest";
import { initializeI18n, i18n } from "../../i18n";
import { StoreContext } from "../../storeContext";
import type { Store } from "../../store";
import { ShippingPrice } from "./ShippingPrice";

const store: Store = { id: "store", name: "Store", culture: "cs-CZ", currency: "CZK", logoUrl: null,
  providerKeys: [], theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 4 } };
beforeAll(initializeI18n);
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });

describe("delivery price presentation", () => {
  it.each(["cs", "en"])("names the old and charged prices in %s independently of the store's currency", async language => {
    await i18n.changeLanguage(language);
    render(<StoreContext value={store}><ShippingPrice price={0} fullPrice={1200} /></StoreContext>);
    const price = document.querySelector(".shipping-price")!;
    expect(price.querySelector("del .sr-only")?.textContent).toBe(i18n.t("checkout:shippingBeforeDiscount") + ": ");
    expect(price.querySelector("del .currency-value")?.textContent).toBe(new Intl.NumberFormat("cs-CZ", { style: "currency", currency: "CZK" }).format(1200));
    expect(price.querySelector(":scope > span > .sr-only")?.textContent).toBe(i18n.t("checkout:shippingCharged") + ": ");
    expect(price.querySelector(":scope > span > .currency-value")?.textContent).toBe(new Intl.NumberFormat("cs-CZ", { style: "currency", currency: "CZK" }).format(0));
  });
  it.each(["JPY", "KWD"])("uses the store currency's %s precision for original and charged values", currency => {
    const culture = "en", price = currency === "JPY" ? 0 : 0.123, fullPrice = currency === "JPY" ? 1200 : 1234.567;
    render(<StoreContext value={{ ...store, culture, currency }}><ShippingPrice price={price} fullPrice={fullPrice} /></StoreContext>);
    const values = [...document.querySelectorAll(".currency-value")].map(node => node.textContent);
    const format = new Intl.NumberFormat(culture, { style: "currency", currency });
    expect(values).toEqual([format.format(fullPrice), format.format(price)]);
  });
  it.each([undefined, 0, 20])("does not advertise a discount when fullPrice is %j and price is unchanged", fullPrice => {
    const price = fullPrice ?? 20;
    render(<StoreContext value={store}><ShippingPrice price={price} fullPrice={fullPrice} /></StoreContext>);
    expect(document.querySelector("del")).toBeNull();
    expect(document.querySelectorAll(".currency-value")).toHaveLength(1);
  });
});
