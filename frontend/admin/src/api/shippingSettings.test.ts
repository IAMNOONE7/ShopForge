import { afterEach, describe, expect, it, vi } from "vitest";
import { parseShippingSetting, shippingSettingsApi } from "./shippingSettings";

const settings = { packagingWeightGrams: 300, fillPercent: 70, largestParcelOrder: 10 };
const reply = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
describe("parcel settings contracts", () => {
  it("reads authoritative defaults and saves only the three scoped settings, including zero", async () => {
    const values = { packagingWeightGrams: 0, fillPercent: 100, largestParcelOrder: 100 }, signal = new AbortController().signal;
    const fetch = vi.fn().mockResolvedValueOnce(reply({ ...settings, unknown: "ignored" })).mockResolvedValueOnce(reply(values)); vi.stubGlobal("fetch", fetch);
    expect(await shippingSettingsApi.read("store/a", signal)).toEqual(settings); expect(await shippingSettingsApi.save("store/a", values, signal)).toEqual(values);
    expect(fetch.mock.calls.map(([path, init]) => [path, init.method, init.body, init.signal])).toEqual([
      ["/api/admin/stores/store%2Fa/shipping-settings", "GET", undefined, signal],
      ["/api/admin/stores/store%2Fa/shipping-settings", "PUT", JSON.stringify(values), signal],
    ]);
  });
  it.each([null, {}, { ...settings, fillPercent: undefined }, { ...settings, packagingWeightGrams: "300" }, { ...settings, packagingWeightGrams: -1 }, { ...settings, packagingWeightGrams: 10001 }, { ...settings, fillPercent: 0 }, { ...settings, fillPercent: 101 }, { ...settings, largestParcelOrder: 0 }, { ...settings, largestParcelOrder: 101 }, { ...settings, fillPercent: 70.5 }])("rejects incomplete or out-of-contract reads rather than supplying defaults: %j", value => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(value))); return expect(shippingSettingsApi.read("store")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("does not confirm unapplied, malformed or refused writes and never resends", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(settings)).mockResolvedValueOnce(reply({})).mockResolvedValueOnce(reply({ errors: { fillPercent: ["Invalid"] } }, 400)); vi.stubGlobal("fetch", fetch);
    for (let index = 0; index < 3; index++) await expect(shippingSettingsApi.save("store", { ...settings, fillPercent: 80 })).rejects.toBeDefined();
    expect(fetch).toHaveBeenCalledTimes(3);
  });
  it("discards a late aborted read", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(settings))); const controller = new AbortController(); controller.abort();
    await expect(shippingSettingsApi.read("store", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
  });
  it.each([
    ["packagingWeightGrams", "0", 0], ["packagingWeightGrams", " 10000 ", 10000], ["packagingWeightGrams", "", null], ["packagingWeightGrams", "-1", null], ["packagingWeightGrams", "10001", null],
    ["fillPercent", "1", 1], ["fillPercent", "100", 100], ["fillPercent", "0", null], ["fillPercent", "70,5", null], ["fillPercent", "1e2", null],
    ["largestParcelOrder", "1", 1], ["largestParcelOrder", "100", 100], ["largestParcelOrder", "101", null], ["largestParcelOrder", "1.5", null],
  ] as const)("parses %s=%s against whole-number contract bounds", (field, raw, expected) => expect(parseShippingSetting(field, raw)).toBe(expected));
});
