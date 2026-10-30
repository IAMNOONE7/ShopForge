import { afterEach, describe, expect, it, vi } from "vitest";
import { addToCart, applyDiscount, getCart, getCheckoutMethods, removeDiscount, removeFromCart, setCartQuantity } from "./cart";
import { validFreeShipping } from "./freeShipping";
const cart = { items: [], count: 0, itemsTotal: 100, vatTotal: 20, changed: false, discount: null, discountProblem: null, freeShipping: { threshold: 500, remaining: 400 } };
const methods = { destinationCountry: "CZ", paymentMethods: [], shippingMethods: [], freeShipping: cart.freeShipping };
const serve = (value: unknown) => { const fetch = vi.fn().mockResolvedValue(Response.json(value)); vi.stubGlobal("fetch", fetch); return fetch; };
afterEach(() => vi.unstubAllGlobals());
describe("server free-delivery progress", () => {
  it.each([undefined, null, { threshold: 500, remaining: 500 }, { threshold: 500, remaining: 0 }, { threshold: 0.123, remaining: 0.001 }])("preserves off, legacy, remaining and exactly reached values %j", async freeShipping => {
    expect(validFreeShipping(freeShipping)).toBe(true);
    serve({ ...cart, freeShipping }); expect((await getCart()).freeShipping).toEqual(freeShipping);
  });
  it.each([{}, [], "off", { threshold: 0, remaining: 0 }, { threshold: -1, remaining: 0 }, { threshold: "500", remaining: 400 },
    { threshold: 500, remaining: "0" }, { threshold: 500, remaining: -1 }, { threshold: 500, remaining: 501 },
    { threshold: Infinity, remaining: 0 }, { threshold: 500, remaining: NaN }])("rejects malformed progress on cart and destination reads %j", async freeShipping => {
    expect(validFreeShipping(freeShipping)).toBe(false);
    serve({ ...cart, freeShipping }); await expect(getCart()).rejects.toMatchObject({ kind: "invalid-response" });
    serve({ ...methods, freeShipping }); await expect(getCheckoutMethods("CZ")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each([() => addToCart("listing", "form", 1), () => setCartQuantity("listing", "form", 2), () => removeFromCart("listing", "form"), () => applyDiscount("SAVE"), removeDiscount])("checks new progress on every cart mutation without retrying", async change => {
    const fetch = serve({ ...cart, freeShipping: { threshold: 500, remaining: -1 } });
    await expect(change()).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("keeps the server's remaining amount without reconstructing it from the cart total", async () => {
    const value = { ...cart, freeShipping: { threshold: 500, remaining: 123.45 } }; serve(value);
    expect(await getCart()).toEqual(value);
    serve({ ...methods, freeShipping: value.freeShipping }); expect((await getCheckoutMethods("CZ")).freeShipping).toEqual(value.freeShipping);
  });
  it.each([{ discount: { code: "SAVE", name: "Save", amount: 0 } }, { discountProblem: "Still invalid" }])("refuses to acknowledge an unapplied code removal %j", async fields => {
    serve({ ...cart, ...fields }); await expect(removeDiscount()).rejects.toMatchObject({ kind: "invalid-response" });
  });
});
