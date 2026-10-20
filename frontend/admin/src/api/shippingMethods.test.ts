import { afterEach, expect, it, vi } from "vitest";
import { api, type ShippingMethod } from "../api";
const row: ShippingMethod = { code: "parcel", name: "Parcel", providerKey: "manual", price: 0, vatRate: 0, isActive: false, requiresPickupPoint: false, maxWeightGrams: null, countries: [], taxClass: null };
afterEach(() => vi.unstubAllGlobals());
it.each([null, "books", "unavailable-legacy"])("reads required saved assignment %s without replacing zero, null or false", async taxClass => {
  const saved = { ...row, taxClass }, fetch = vi.fn().mockResolvedValue(Response.json([saved])); vi.stubGlobal("fetch", fetch);
  expect(await api.shippingMethods("a/b")).toEqual([saved]); expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/shipping-methods");
});
it.each([null, {}, [{ ...row, taxClass: undefined }], [{ ...row, taxClass: "" }], [{ ...row, taxClass: 0 }], [{ ...row, countries: undefined }],
  [{ ...row, maxWeightGrams: undefined }], [{ ...row, maxWeightGrams: 0 }], [{ ...row, maxWeightGrams: 2147483648 }], [{ ...row, isActive: undefined }],
  [{ ...row, requiresPickupPoint: null }], [{ ...row, price: -1 }], [{ ...row, vatRate: 101 }], [{ ...row, name: null }], [{ ...row, countries: ["CZE"] }], [row, row]])("rejects incomplete replacement facts instead of inventing defaults: %j", async result => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(result))); await expect(api.shippingMethods("a")).rejects.toMatchObject({ kind: "invalid-response" });
});
it("keeps empty methods distinct from malformed data and honors cancellation after a response", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json([]))); expect(await api.shippingMethods("a")).toEqual([]);
  const controller = new AbortController(); vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => { controller.abort(); return Response.json([row]); }));
  await expect(api.shippingMethods("a", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it.each(["books", null])("updates the complete contract with explicit class %s and encoded identifiers", async taxClass => {
  const { code: _code, providerKey: _provider, ...input } = { ...row, taxClass };
  const fetch = vi.fn().mockResolvedValue(Response.json({ ...row, code: "parcel/a", taxClass })); vi.stubGlobal("fetch", fetch);
  await api.updateShippingMethod("a/b", "parcel/a", input);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/shipping-methods/parcel%2Fa");
  expect(fetch.mock.calls[0][1].method).toBe("PUT"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual(input);
});
it.each([null, "books"])("creates through the existing full POST with explicit class %s", async taxClass => {
  const { code: _code, isActive: _active, ...input } = { ...row, taxClass };
  const fetch = vi.fn().mockResolvedValue(Response.json({ ...row, taxClass })); vi.stubGlobal("fetch", fetch); await api.createShippingMethod("a", input);
  expect(fetch.mock.calls[0][1].method).toBe("POST"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ ...input, isActive: true });
});
it.each([undefined, { ...row, code: "another" }, { ...row, taxClass: undefined }])("rejects a missing, foreign or incomplete update acknowledgment without retrying: %j", async result => {
  const fetch = vi.fn().mockResolvedValue(result === undefined ? new Response(null, { status: 204 }) : Response.json(result)); vi.stubGlobal("fetch", fetch);
  const { code: _code, providerKey: _provider, ...input } = row; await expect(api.updateShippingMethod("a", "parcel", input)).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
});
