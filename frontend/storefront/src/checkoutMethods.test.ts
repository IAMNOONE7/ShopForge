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

const parcelMethod = { ...methods.shippingMethods[0], pricing: "perParcel", parcels: 2,
  mapParcel: { weightKg: 1.25, lengthCm: 31, widthCm: 20, depthCm: 10 } };
it("keeps the cart quote, parcel count, map units and separately unavailable methods", async () => {
  const offer = { ...methods, shippingMethods: [parcelMethod], unavailableShippingMethods: [
    { code: "locker", name: "Locker", reason: "tooLarge", variantId: "red" },
    { code: "van", name: "Van", reason: "notToCountry", variantId: null },
  ] };
  serve(offer);
  expect(await getCheckoutMethods("CZ")).toEqual(offer);
});
it("accepts the current flat and empty-cart contracts", async () => {
  const offer = { ...methods, shippingMethods: [{ ...methods.shippingMethods[0], pricing: "flat", parcels: null, mapParcel: null }], unavailableShippingMethods: [] };
  serve(offer);
  expect(await getCheckoutMethods("CZ")).toEqual(offer);
});
it.each([
  { ...parcelMethod, pricing: "future" }, { ...parcelMethod, parcels: 0 }, { ...parcelMethod, parcels: 1.5 },
  { ...parcelMethod, mapParcel: null }, { ...parcelMethod, mapParcel: { ...parcelMethod.mapParcel, weightKg: -1 } },
  { ...parcelMethod, mapParcel: { ...parcelMethod.mapParcel, depthCm: null } },
  { ...methods.shippingMethods[0], pricing: "flat" },
  { ...parcelMethod, pricing: "flat" },
])("refuses unusable parcel terms %j", async method => {
  serve({ ...methods, shippingMethods: [method] });
  await expect(getCheckoutMethods("CZ")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([null, {}, [null], [{ code: "no", name: "No", reason: "future", variantId: null }],
  [{ code: "courier", name: "No", reason: "missingData", variantId: null }],
  [{ code: "no", name: "No", reason: "tooHeavy" }],
  [{ code: "no", name: "No", reason: "tooManyParcels", variantId: {} }],
])("refuses malformed or contradictory unavailable methods %j", async unavailableShippingMethods => {
  serve({ ...methods, unavailableShippingMethods });
  await expect(getCheckoutMethods("CZ")).rejects.toMatchObject({ kind: "invalid-response" });
});

it.each(["flat", "perParcel"])("preserves %s full delivery cost independently of a charged zero", async pricing => {
  const method = pricing === "perParcel" ? parcelMethod : { ...methods.shippingMethods[0], pricing, parcels: null, mapParcel: null };
  const value = { ...methods, shippingMethods: [{ ...method, price: 0, fullPrice: 20 }] };
  serve(value); expect(await getCheckoutMethods("CZ")).toEqual(value);
});
it.each([null, -1, "20", 9, Number.MAX_VALUE * 2])("refuses an invalid full delivery cost %j", async fullPrice => {
  serve({ ...methods, shippingMethods: [{ ...methods.shippingMethods[0], fullPrice }] });
  await expect(getCheckoutMethods("CZ")).rejects.toMatchObject({ kind: "invalid-response" });
});
