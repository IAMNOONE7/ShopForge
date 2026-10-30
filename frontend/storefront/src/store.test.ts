import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchStore } from "./store";

afterEach(() => vi.unstubAllGlobals());

describe("public store delivery information", () => {
  it.each([undefined, null, "200", 0, -1, {}, 123.456])("keeps optional threshold facts truthful without preventing browsing (%j)", async freeShippingFrom => {
    const store = { id: "store-a", name: "Shop", currency: "KWD", culture: "en-IE", freeShippingFrom, deliveryFrom: 0 };
    const fetch = vi.fn().mockResolvedValue(Response.json(store)); vi.stubGlobal("fetch", fetch);
    const signal = new AbortController().signal;
    expect(await fetchStore(signal)).toEqual({ ...store, freeShippingFrom: freeShippingFrom === 123.456 ? 123.456 : null });
    expect(fetch.mock.calls[0][0]).toBe("/api/storefront/store");
    expect(fetch.mock.calls[0][1].signal).toBe(signal);
    expect(fetch).toHaveBeenCalledOnce();
  });
});
