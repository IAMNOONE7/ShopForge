import { afterEach, describe, expect, it, vi } from "vitest";
import { getCheckoutMethods } from "./cart";
import { emptyAddress, isAddressComplete, isCountryCode } from "./components/checkout/address";

const methods = { destinationCountry: "CZ", paymentMethods: [{ code: "card", name: "Card" }],
  shippingMethods: [{ code: "courier", name: "Courier", price: 10, requiresPickupPoint: false, pickupPointChoice: "none" }] };
function serve(body: unknown, status = 200) {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}
afterEach(() => vi.unstubAllGlobals());

describe("destination-aware checkout method contract", () => {
  it("normalizes the query and requires the same response country, with cancellation", async () => {
    const fetch = serve(methods), controller = new AbortController();
    expect(await getCheckoutMethods(" cz ", controller.signal)).toEqual(methods);
    expect(fetch.mock.calls[0][0]).toBe("/api/storefront/checkout/methods?country=CZ");
    expect(fetch.mock.calls[0][1].signal).toBe(controller.signal);
  });
  it.each([null, "", "  "])("omits an unknown country %j without guessing a destination", async country => {
    const fetch = serve({ ...methods, destinationCountry: null });
    expect((await getCheckoutMethods(country)).destinationCountry).toBeNull();
    expect(fetch.mock.calls[0][0]).toBe("/api/storefront/checkout/methods");
  });
  it("preserves the server's country validation problem", async () => {
    const problem = { errors: { country: ["A country is its two-letter code, such as CZ."] } };
    const fetch = serve(problem, 400);
    await expect(getCheckoutMethods("c?" )).rejects.toMatchObject({ kind: "http", status: 400, problem });
    expect(fetch.mock.calls[0][0]).toBe("/api/storefront/checkout/methods?country=C%3F");
  });
  it.each([{}, null, { ...methods, destinationCountry: null }, { ...methods, destinationCountry: "cz" },
    { ...methods, destinationCountry: "SK" }, { ...methods, paymentMethods: null },
    { ...methods, shippingMethods: [null] }, { ...methods, shippingMethods: [{ ...methods.shippingMethods[0], price: -1 }] },
    { ...methods, paymentMethods: [methods.paymentMethods[0], methods.paymentMethods[0]] },
    { ...methods, shippingMethods: [{ ...methods.shippingMethods[0], pickupPointChoice: "future" }] }])("refuses an ambiguous or malformed offer %j", async answer => {
    serve(answer);
    await expect(getCheckoutMethods("CZ")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("accepts an empty authoritative offer for the requested country", async () => {
    const empty = { destinationCountry: "SK", shippingMethods: [], paymentMethods: [] };
    serve(empty);
    expect(await getCheckoutMethods("sk")).toEqual(empty);
  });
  it.each(["12", "ČZ", "C", "CZE", ""])("does not accept %j as a complete address country", country => {
    expect(isCountryCode(country)).toBe(false);
    expect(isAddressComplete({ ...emptyAddress(), fullName: "Buyer", line1: "Street", city: "City", postalCode: "123", country })).toBe(false);
  });
  it("accepts trimmed ASCII country codes without inventing an ISO allowlist", () => {
    expect(isCountryCode(" cz ")).toBe(true);
    expect(isCountryCode("ZZ")).toBe(true);
  });
});
