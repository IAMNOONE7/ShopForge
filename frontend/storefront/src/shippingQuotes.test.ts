import { describe, expect, it } from "vitest";
import { HttpError } from "./api/http";
import type { Cart, ShippingMethod } from "./cart";
import { readChangedShippingQuote, shippingMethodErrors, validMapParcel } from "./shippingQuotes";

const cart = { items: [{ variantId: "red", quantity: 2 }, { variantId: "blue", quantity: 1 }] } as Cart;
const method: ShippingMethod = { code: "parcel", name: "Parcel", price: 10, pricing: "perParcel", parcels: 2,
  mapParcel: { weightKg: 1.25, lengthCm: 20, widthCm: 15, depthCm: 10 }, requiresPickupPoint: false, pickupPointChoice: "none" };
const parcel = { contents: [{ variantId: "red", sku: "RED", quantity: 1 }], grossWeightGrams: 1200, bandUpToGrams: 2000, price: 8, alone: false };
const quote = { currency: "EUR", total: 16, isAvailable: true, unavailable: null, parcels: [parcel, {
  ...parcel, contents: [{ variantId: "red", sku: "RED", quantity: 1 }, { variantId: "blue", sku: "BLUE", quantity: 1 }],
}] };
const changed = (shippingQuote: unknown) => new HttpError("http", 409, { title: "The delivery price has changed", shippingQuote });

describe("changed delivery price", () => {
  it("keeps the server's parcels and total when they account for the whole cart", () => {
    expect(readChangedShippingQuote(changed(quote), "EUR", cart, method)).toEqual(quote);
  });
  it.each(["flat", "perParcel"])("keeps the %s cost distinct from the charged zero price", pricing => {
    const value = { ...quote, charged: 0, shippingDiscount: 16, freeShippingThreshold: 100,
      parcels: pricing === "flat" ? [] : quote.parcels };
    expect(readChangedShippingQuote(changed(value), "EUR", cart, { ...method, pricing: pricing as "flat" | "perParcel" })).toEqual(value);
    const paid = { ...value, charged: 16, shippingDiscount: 0, freeShippingThreshold: null };
    expect(readChangedShippingQuote(changed(paid), "EUR", cart, { ...method, pricing: pricing as "flat" | "perParcel" })).toEqual(paid);
  });
  it.each([
    { charged: -1 }, { charged: "0" }, { charged: null }, { charged: Infinity }, { charged: 17 },
    { shippingDiscount: -1 }, { shippingDiscount: 17 }, { shippingDiscount: 0 },
    { freeShippingThreshold: 0 }, { freeShippingThreshold: -1 }, { freeShippingThreshold: "100" },
    { charged: undefined }, { shippingDiscount: undefined }, { freeShippingThreshold: undefined },
  ])("rejects malformed or inconsistent discount facts %j", invalid => {
    const value = { ...quote, charged: 0, shippingDiscount: 16, freeShippingThreshold: 100, ...invalid };
    expect(() => readChangedShippingQuote(changed(value), "EUR", cart, method)).toThrow(HttpError);
  });
  it("accepts a zero new total and legitimate floating-point addition", () => {
    expect(readChangedShippingQuote(changed({ ...quote, total: 0, parcels: quote.parcels.map(p => ({ ...p, price: 0 })) }), "EUR", cart, method)?.total).toBe(0);
    expect(readChangedShippingQuote(changed({ ...quote, total: 0.3, parcels: [{ ...quote.parcels[0], price: 0.1 }, { ...quote.parcels[1], price: 0.2 }] }), "EUR", cart, method)?.total).toBe(0.3);
  });
  it("accepts a flat-price change with no parcels", () => {
    expect(readChangedShippingQuote(changed({ ...quote, parcels: [] }), "EUR", cart, { ...method, pricing: "flat" })?.total).toBe(16);
  });
  it.each([null, {}, { ...quote, currency: "CZK" }, { ...quote, total: -1 }, { ...quote, total: "16" },
    { ...quote, total: Infinity }, { ...quote, isAvailable: false }, { ...quote, unavailable: {} },
    { ...quote, parcels: [] }, { ...quote, total: 17 }, { ...quote, parcels: [null] },
    { ...quote, parcels: [{ ...parcel, grossWeightGrams: 2001 }] },
    { ...quote, parcels: [{ ...parcel, grossWeightGrams: 0 }] },
    { ...quote, parcels: [{ ...parcel, alone: "false" }] },
    { ...quote, parcels: [{ ...parcel, contents: [] }] },
    { ...quote, parcels: [{ ...parcel, contents: [{ variantId: "red", sku: "RED", quantity: 0 }] }] },
    { ...quote, total: 8, parcels: [{ ...parcel, contents: [{ variantId: "unknown", sku: "RED", quantity: 3 }] }] },
    { ...quote, total: 8, parcels: [parcel] },
    { ...quote, total: 8, parcels: [{ ...parcel, contents: [{ variantId: "red", sku: "RED", quantity: 2 }, { variantId: "red", sku: "RED", quantity: 1 }] }] },
  ])("refuses a malformed or unrelated replacement quote %j", value => {
    expect(() => readChangedShippingQuote(changed(value), "EUR", cart, method)).toThrow(HttpError);
  });
  it("does not treat an ordinary conflict or an in-flight write as a price update", () => {
    for (const error of [new HttpError("http", 409), new HttpError("network", null),
      new HttpError("http", 409, { title: "A request with this key is still in progress", shippingQuote: quote })])
      expect(readChangedShippingQuote(error, "EUR", cart, method)).toBeNull();
  });
  it("rejects parcel facts on a flat replacement quote", () => {
    expect(() => readChangedShippingQuote(changed(quote), "EUR", cart, { ...method, pricing: "flat" })).toThrow(HttpError);
  });
});

it("preserves literal shopper field reasons without trusting other problem shapes", () => {
  expect(shippingMethodErrors(new HttpError("http", 400, { errors: { shippingMethodCode: ["<b>Too heavy</b>", "", null as unknown as string] } }))).toEqual(["<b>Too heavy</b>"]);
  expect(shippingMethodErrors(new HttpError("http", 409, { errors: { shippingMethodCode: ["No"] } }))).toEqual([]);
  expect(shippingMethodErrors(new HttpError("http", 400, { errors: { shippingMethodCode: "No" as unknown as string[] } }))).toEqual([]);
});

it("accepts only the widget's positive numeric weight and complete nullable side group", () => {
  expect(validMapParcel(method.mapParcel)).toBe(true);
  expect(validMapParcel({ weightKg: 0.125, lengthCm: null, widthCm: null, depthCm: null })).toBe(true);
  for (const bad of [null, {}, { ...method.mapParcel, weightKg: 0 }, { ...method.mapParcel, weightKg: "1.25" },
    { ...method.mapParcel, lengthCm: null }, { ...method.mapParcel, lengthCm: 2.5 }]) expect(validMapParcel(bad)).toBe(false);
});
