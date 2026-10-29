import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { parcelCard, parcelInput, parcelMethod, parcelQuote, parcelService, parcelForms } from "../test/parcelFixtures";
import { checkedQuote, parcelShippingApi, quoteReasons } from "./parcelShipping";
const response = (value: unknown, status = 200) => Response.json(value, { status });
afterEach(() => vi.unstubAllGlobals());
describe("parcel shipping contracts", () => {
  it("reads the complete service catalog without fabricating limits, prices or null caps", async () => {
    const fetch = vi.fn().mockResolvedValue(response([parcelService])); vi.stubGlobal("fetch", fetch); expect(await parcelShippingApi.services("store/a")).toEqual([parcelService]); expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%2Fa/carrier-services");
  });
  it.each([null, {}, [{ ...parcelService, kind: "unknown" }], [{ ...parcelService, largest: null }], [{ ...parcelService, maxWeightGrams: 0 }], [{ ...parcelService, sourceDate: "2026-02-30" }], [parcelService, parcelService]])("rejects an incomplete service catalog %j", async value => { vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(value))); await expect(parcelShippingApi.services("a")).rejects.toMatchObject({ kind: "invalid-response" }); });
  it("replaces exactly the whole card with zero price and unchanged method terms", async () => {
    const saved = { ...parcelMethod, ratesFrom: parcelCard.source, ratesPricedOn: parcelCard.pricedOn, rateBands: parcelCard.bands };
    const fetch = vi.fn().mockResolvedValue(response(saved)); vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
    expect(await parcelShippingApi.rates("store/a", parcelMethod, parcelCard, signal)).toEqual({ ...saved, code: "zbox" });
  });
  it("uses an encoded rate route and transmits no unrelated method fields", async () => {
    const method = { ...parcelMethod, code: "zbox/a" }, saved = { ...method, ratesFrom: parcelCard.source, ratesPricedOn: null, rateBands: parcelCard.bands };
    const fetch = vi.fn().mockResolvedValue(response(saved)); vi.stubGlobal("fetch", fetch); await parcelShippingApi.rates("store/a", method, parcelCard);
    expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%2Fa/shipping-methods/zbox%2Fa/rates"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual(parcelCard); expect(fetch).toHaveBeenCalledOnce();
  });
  it.each(["code", "providerKey", "isActive", "carrierService", "price", "rateBands", "ratesFrom", "ratesPricedOn"])("refuses an unapplied or unrelated %s replacement without retry", async field => {
    const wrong = { code: "other", providerKey: "manual", isActive: true, carrierService: "other-service", price: 1, rateBands: [{ upToGrams: 1, price: 0 }], ratesFrom: "carrier", ratesPricedOn: "2026-10-01" };
    const fetch = vi.fn().mockResolvedValue(response({ ...parcelMethod, ratesFrom: parcelCard.source, ratesPricedOn: null, rateBands: parcelCard.bands, [field]: wrong[field as keyof typeof wrong] })); vi.stubGlobal("fetch", fetch);
    await expect(parcelShippingApi.rates("a", parcelMethod, parcelCard)).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("preserves unrelated parcel terms and rates on a legacy method PUT that omits pricing", async () => {
    const fetch = vi.fn().mockResolvedValue(response(parcelMethod)); vi.stubGlobal("fetch", fetch);
    await api.updateShippingMethod("a", parcelMethod.code, { name: parcelMethod.name, price: 0, vatRate: 21, isActive: false, requiresPickupPoint: true, maxWeightGrams: null, countries: ["CZ"], taxClass: "standard" }); expect(JSON.parse(fetch.mock.calls[0][1].body)).not.toHaveProperty("pricing");
  });
  it("accepts longest-first parcel sides returned by a measured method save", async () => {
    const { code: _code, providerKey: _provider, ratesFrom: _source, ratesPricedOn: _date, rateBands: _bands, ...input } = { ...parcelMethod, parcelLengthMm: 100, parcelWidthMm: 300, parcelHeightMm: 200 };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response({ ...parcelMethod, parcelLengthMm: 300, parcelWidthMm: 200, parcelHeightMm: 100 })));
    expect((await api.updateShippingMethod("a", parcelMethod.code, input)).parcelLengthMm).toBe(300);
  });
  it("previews exact scoped line identities without a mutation header or retry", async () => {
    const fetch = vi.fn().mockResolvedValue(response(parcelQuote)); vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
    expect(await parcelShippingApi.quote("store/a", parcelMethod, "CZK", parcelInput, signal)).toEqual(parcelQuote);
    expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%2Fa/shipping-methods/zbox/quote"); expect(fetch.mock.calls[0][1].signal).toBe(signal); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual(parcelInput); expect(fetch).toHaveBeenCalledOnce();
  });
  it.each(quoteReasons)("preserves the unavailable reason %s, literal detail and zero total", reason => {
    const value = { isAvailable: false, currency: "CZK", total: 0, parcels: [], unavailable: { reason, detail: "Carrier limit 2000 g <b>literal</b>", variantId: parcelInput.lines[0].variantId } };
    expect(checkedQuote(value, "CZK", parcelInput, true)).toEqual(value);
  });
  it.each([null, {}, { ...parcelQuote, currency: "EUR" }, { ...parcelQuote, isAvailable: false }, { ...parcelQuote, parcels: [] }, { ...parcelQuote, total: 159 }, { ...parcelQuote, total: -1 },
    { ...parcelQuote, parcels: [{ ...parcelQuote.parcels[0], contents: [{ variantId: parcelMethod.code, sku: "FOREIGN", quantity: 2 }] }] }, { ...parcelQuote, parcels: [parcelQuote.parcels[0]] },
    { ...parcelQuote, parcels: [{ ...parcelQuote.parcels[0], grossWeightGrams: 2001 }] }])("rejects a foreign, incomplete or inconsistent quote %j", value => expect(() => checkedQuote(value, "CZK", parcelInput, true)).toThrow());
  it.each([true, false])("preserves zero charged delivery independently from cost and parcels (perParcel: %s)", perParcel => {
    const value = { ...parcelQuote, charged: 0, shippingDiscount: parcelQuote.total, freeShippingThreshold: 1000,
      parcels: perParcel ? parcelQuote.parcels : [] };
    expect(checkedQuote(value, "CZK", parcelInput, perParcel)).toEqual(value);
    const paid = { ...value, charged: parcelQuote.total, shippingDiscount: 0, freeShippingThreshold: null };
    expect(checkedQuote(paid, "CZK", parcelInput, perParcel)).toEqual(paid);
  });
  it.each([
    { charged: -1 }, { charged: "0" }, { charged: null }, { charged: Infinity }, { charged: 159 },
    { shippingDiscount: -1 }, { shippingDiscount: 159 }, { shippingDiscount: 0 },
    { freeShippingThreshold: 0 }, { freeShippingThreshold: -1 }, { freeShippingThreshold: "1000" },
    { charged: undefined }, { shippingDiscount: undefined }, { freeShippingThreshold: undefined },
    { charged: 0.001, shippingDiscount: 157.999 },
  ])("rejects an inconsistent or invalid customer charge %j", invalid => {
    expect(() => checkedQuote({ ...parcelQuote, charged: 0, shippingDiscount: 158, freeShippingThreshold: 1000, ...invalid }, "CZK", parcelInput, true)).toThrow();
  });
  it("keeps a flat quote’s total distinct from a parcel breakdown", () => { expect(checkedQuote({ ...parcelQuote, total: 0, parcels: [] }, "CZK", parcelInput, false).total).toBe(0); });
  it("checks the real company catalog form collection without inventing a detail endpoint", async () => {
    const fetch = vi.fn().mockResolvedValue(response(parcelForms)); vi.stubGlobal("fetch", fetch); expect(await parcelShippingApi.forms()).toEqual(parcelForms); expect(fetch.mock.calls[0][0]).toBe("/api/admin/products");
  });
  it("discards late aborted reads and previews", async () => {
    const controller = new AbortController(); controller.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(parcelQuote)));
    await expect(parcelShippingApi.quote("a", parcelMethod, "CZK", parcelInput, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
  });
});
