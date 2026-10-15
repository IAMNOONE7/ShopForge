import { afterEach, expect, it, vi } from "vitest";
import { api, type AdminList, type AdminOrder } from "../api";
import { order, orderPage } from "../test/orderFixtures";
import { maxOrderPage } from "../utils/orderListQuery";

afterEach(() => vi.unstubAllGlobals());

it("requests the bounded first order page and retains its server metadata", async () => {
  const page: AdminList<AdminOrder> = { items: [{
    number: "2026-1001", placedAt: "2026-10-14T10:00:00Z", status: "AwaitingPayment",
    email: "shopper@example.test", hasAccount: false, grandTotal: 0, items: 1,
  }], totalCount: 26, page: 1, pageSize: 25, hasMore: true };
  const fetch = vi.fn().mockResolvedValue(Response.json(page));
  vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal;
  expect(await api.orders("store-a", signal)).toEqual(page);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store-a/orders?page=1&pageSize=25&sort=-placed");
  expect(fetch.mock.calls[0][1]).toMatchObject({ method: "GET", signal });
});

it("requests a later server page with encoded terms, sort and store identity", async () => {
  const page = { ...orderPage, items: [{ ...order, number: "2026-1051" }], page: 2, totalCount: 26 };
  const fetch = vi.fn().mockResolvedValue(Response.json(page)); vi.stubGlobal("fetch", fetch);
  expect(await api.orders("store/a", undefined, { page: 2, sort: "status", q: "50% & buyer_name" })).toEqual(page);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%2Fa/orders?page=2&pageSize=25&sort=status&q=50%25+%26+buyer_name");
  expect(fetch).toHaveBeenCalledTimes(1);
});

it.each([null, [], {}, { ...orderPage, items: [order, order] }, { ...orderPage, page: 2 }, { ...orderPage, pageSize: 200 },
  { ...orderPage, totalCount: -1 }, { ...orderPage, hasMore: true }, { ...orderPage, items: [{ ...order, placedAt: "bad date" }] },
  { ...orderPage, items: [{ ...order, items: "2" }] }])("rejects unusable list metadata or rows: %j", async (value) => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(value)));
  await expect(api.orders("store-a")).rejects.toMatchObject({ kind: "invalid-response" });
});

it("does not request unsafe offsets and encodes direct detail identities", async () => {
  const fetch = vi.fn().mockResolvedValue(Response.json({})); vi.stubGlobal("fetch", fetch);
  await expect(api.orders("store-a", undefined, { page: maxOrderPage + 1, sort: "-placed", q: "" })).rejects.toThrow(RangeError);
  expect(fetch).not.toHaveBeenCalled();
  await api.order("store/a", "2026/1 #");
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%2Fa/orders/2026%2F1%20%23");
});
