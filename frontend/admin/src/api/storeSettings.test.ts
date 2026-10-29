import { afterEach, describe, expect, it, vi } from "vitest";
import { api, type AdminStore, type StoreSettings } from "../api";
import { metadataStore } from "../test/metadataFixtures";
import { checkedSettingsSave, validFreeShipping } from "./storeSettings";

const store: AdminStore = { ...metadataStore, freeShipping: { isOn: false, threshold: 1000 } };
const input: StoreSettings = { name: store.name, currency: store.currency, culture: store.culture,
  theme: store.theme, returnWindowDays: store.returnWindowDays, company: store.company, freeShipping: { isOn: true, threshold: 1000 } };
afterEach(() => vi.unstubAllGlobals());
describe("store free-delivery contracts", () => {
  it.each(["CZK", "JPY", "KWD"])("accepts only a positive threshold in %s precision", currency => {
    expect(validFreeShipping({ isOn: false, threshold: null }, currency)).toBe(true);
    expect(validFreeShipping({ isOn: true, threshold: currency === "JPY" ? 1000 : currency === "KWD" ? 1000.123 : 1000.12 }, currency)).toBe(true);
    expect(validFreeShipping({ isOn: true, threshold: currency === "JPY" ? 1.1 : currency === "KWD" ? 1.1234 : 1.123 }, currency)).toBe(false);
  });
  it.each([undefined, null, {}, [], { isOn: true, threshold: null }, { isOn: "false", threshold: 1000 },
    { isOn: false, threshold: 0 }, { isOn: false, threshold: -1 }, { isOn: true, threshold: Infinity },
    { isOn: true, threshold: "1000" }, { isOn: false }, { threshold: 1000 }])("does not turn missing or malformed values into an off setting %j", value => {
    expect(validFreeShipping(value, "CZK")).toBe(false);
  });
  it("sends both replacement values to the existing encoded route, with cancellation and no retry", async () => {
    const result = { ...store, id: "store/a", freeShipping: input.freeShipping }, signal = new AbortController().signal;
    const fetch = vi.fn().mockResolvedValue(Response.json(result)); vi.stubGlobal("fetch", fetch);
    expect(checkedSettingsSave(await api.updateStore("store/a", input, signal), { ...store, id: "store/a" }, input)).toEqual(result);
    expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%2Fa");
    expect(fetch.mock.calls[0][1].signal).toBe(signal);
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual(input); expect(fetch).toHaveBeenCalledOnce();
  });
  it("omits an unchanged group and accepts the current server values without overwriting them", async () => {
    const { freeShipping: _free, ...unchanged } = input;
    const result = { ...store, freeShipping: { isOn: true, threshold: 1500 } };
    const fetch = vi.fn().mockResolvedValue(Response.json(result)); vi.stubGlobal("fetch", fetch);
    expect(checkedSettingsSave(await api.updateStore(store.id, unchanged), store, unchanged)).toEqual(result);
    expect(JSON.parse(fetch.mock.calls[0][1].body)).not.toHaveProperty("freeShipping"); expect(fetch).toHaveBeenCalledOnce();
  });
  it("accepts the server's canonical culture when the merchant entered a different case", () => {
    expect(checkedSettingsSave({ ...store, culture: "en-IE", freeShipping: input.freeShipping }, store, { ...input, culture: "en-ie" }).culture).toBe("en-IE");
  });
  it.each([{ id: "another-store" }, { freeShipping: { isOn: false, threshold: 1000 } }, { freeShipping: null },
    { freeShipping: undefined }, { freeShipping: { isOn: true, threshold: 0 } }, { currency: "EUR" },
    { company: undefined }, { name: "Unrelated" }, { returnWindowDays: 99 }])("does not acknowledge an unapplied or unrelated response %j", fields => {
    expect(() => checkedSettingsSave({ ...store, freeShipping: input.freeShipping, ...fields } as AdminStore, store, input)).toThrow();
  });
});
