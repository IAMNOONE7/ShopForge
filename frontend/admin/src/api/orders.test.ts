import { afterEach, expect, it, vi } from "vitest";
import { api, type AdminList, type AdminOrder } from "../api";

afterEach(() => vi.unstubAllGlobals());

it("requests the bounded first order page and retains its server metadata", async () => {
  const page: AdminList<AdminOrder> = { items: [{
    number: "2026-1001", placedAt: "2026-10-14T10:00:00Z", status: "AwaitingPayment",
    email: "shopper@example.test", hasAccount: false, grandTotal: 0, items: 1,
  }], totalCount: 51, page: 1, pageSize: 50, hasMore: true };
  const fetch = vi.fn().mockResolvedValue(Response.json(page));
  vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal;
  expect(await api.orders("store-a", signal)).toEqual(page);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store-a/orders?page=1&pageSize=50&sort=-placed");
  expect(fetch.mock.calls[0][1]).toMatchObject({ method: "GET", signal });
});
