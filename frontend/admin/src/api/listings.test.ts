import { afterEach, expect, it, vi } from "vitest";
import { api, type StoreProduct } from "../api";

const item: StoreProduct = { id: "abcdef01-1111-2222-3333-444444444444", productId: "11111111-1111-1111-1111-111111111111", sku: "S", name: "Table", slug: "table", description: null,
  price: 100, vatRate: 21, isVisible: true, sortOrder: 0, categoryIds: [], archivedAt: null, taxClass: "standard", seoTitle: "Saved title", seoDescription: null, seoSocialImageUrl: null, seoNoIndex: true };
const first = { items: [{ ...item, id: "22222222-2222-2222-2222-222222222222", isVisible: false }], page: 1, pageSize: 200, totalCount: 201, hasMore: true };
const last = { items: [item], page: 2, pageSize: 200, totalCount: 201, hasMore: false };
afterEach(() => vi.unstubAllGlobals());
function responses(...values: unknown[]) { const fetch = vi.fn(); values.forEach((value) => fetch.mockResolvedValueOnce(Response.json(value))); vi.stubGlobal("fetch", fetch); return fetch; }

it("uses server pages, sort and escaped search rather than narrowing only loaded rows", async () => {
  const fetch = responses(first); const signal = new AbortController().signal;
  expect(await api.storeProducts("store-a", signal, { page: 2, pageSize: 25, q: "50% & SKU", sort: "-price" })).toEqual(first);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store-a/products?page=2&pageSize=25&sort=-price&q=50%25+%26+SKU");
  expect(fetch.mock.calls[0][1].signal).toBe(signal);
});
it.each([null, "2026-10-12T10:00:00+02:00"])("reads one listing directly with recorded retirement %s", async (archivedAt) => {
  const record = { ...item, archivedAt }; const fetch = responses(record); const signal = new AbortController().signal;
  expect(await api.findStoreProduct("store/a", item.id.toUpperCase(), signal)).toEqual(record);
  expect(fetch).toHaveBeenCalledOnce();
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/store%2Fa/products/${item.id.toUpperCase()}`);
  expect(fetch.mock.calls[0][1].signal).toBe(signal);
});
it("checks publication across all pages and ends early when a visible listing is found", async () => {
  const fetch = responses(first, last, { ...first, items: [item] });
  expect(await api.hasVisibleStoreProduct("store-a")).toBe(true);
  expect(await api.hasVisibleStoreProduct("store-a")).toBe(true);
  expect(fetch).toHaveBeenCalledTimes(3);
});
it("keeps actual publication discovery paged and distinguishes absent rows from a failed read", async () => {
  const fetch = responses(first, { ...last, items: [] }, first);
  expect(await api.hasVisibleStoreProduct("store-a")).toBe(false);
  fetch.mockRejectedValueOnce(new Error("offline"));
  await expect(api.hasVisibleStoreProduct("store-a")).rejects.toMatchObject({ kind: "network" });
  const controller = new AbortController(); responses(first); controller.abort();
  await expect(api.hasVisibleStoreProduct("store-a", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it.each([403, 404, 503])("does not fall back to a collection after direct HTTP %s", async (status) => {
  const fetch = vi.fn().mockResolvedValue(Response.json({ title: "Unavailable" }, { status })); vi.stubGlobal("fetch", fetch);
  if (status === 404) expect(await api.findStoreProduct("store-a", "foreign/id")).toBeNull();
  else await expect(api.findStoreProduct("store-a", "foreign/id")).rejects.toMatchObject({ kind: "http", status });
  expect(fetch).toHaveBeenCalledOnce();
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store-a/products/foreign%2Fid");
});
it.each([
  null, [], {}, { ...item, id: "22222222-2222-2222-2222-222222222222" },
  { ...item, productId: "wrong" }, { ...item, archivedAt: undefined }, { ...item, archivedAt: "not a date" },
  { ...item, archivedAt: "42" }, { ...item, archivedAt: "" }, { ...item, price: -1 },
  { ...item, price: "100" }, { ...item, vatRate: 101 }, { ...item, categoryIds: ["wrong"] },
  { ...item, taxClass: undefined }, { ...item, seoNoIndex: undefined }, { ...item, sortOrder: 2147483648 },
])("rejects malformed direct data before it can enable listing writes (%j)", async value => {
  const fetch = responses(value);
  await expect(api.findStoreProduct("store-a", item.id)).rejects.toMatchObject({ kind: "invalid-response" });
  expect(fetch).toHaveBeenCalledOnce();
});
it("cancels before sending and discards a late direct answer", async () => {
  const controller = new AbortController(); const fetch = responses(item); controller.abort();
  await expect(api.findStoreProduct("store-a", item.id, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
  expect(fetch).not.toHaveBeenCalled();
  const next = new AbortController();
  fetch.mockReset();
  fetch.mockImplementationOnce(async () => { next.abort(); return Response.json(item); });
  await expect(api.findStoreProduct("store-a", item.id, next.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it("checks lifecycle facts on collection rows and keeps archived collection reads separate", async () => {
  const retired = { ...item, archivedAt: "2026-10-12T10:00:00Z" };
  const fetch = responses({ ...first, items: [retired] }, { ...first, items: [retired] }, { ...first, items: [{ ...item, archivedAt: undefined }] });
  expect((await api.storeProducts("s", undefined, { archived: true })).items).toEqual([retired]);
  await expect(api.storeProducts("s")).rejects.toMatchObject({ kind: "invalid-response" });
  await expect(api.storeProducts("s")).rejects.toMatchObject({ kind: "invalid-response" });
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/s/products?page=1&pageSize=25&archived=true");
});
it("writes independent listing, category and typed values payloads, omitting existing SEO", async () => {
  const fetch = responses(item, item, { values: { safe: false, length: 0 } });
  const input = { name: "New name", slug: "table", price: 0, vatRate: 0, isVisible: false, sortOrder: -1, description: null, taxClass: item.taxClass };
  await api.updateStoreProduct("store-a", item.id, input); await api.assignCategories("store-a", item.id, []); await api.setProductAttributes("store-a", item.id, { safe: false, length: 0 });
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, JSON.parse(options.body)])).toEqual([
    [`/api/admin/stores/store-a/products/${item.id}`, "PUT", input],
    [`/api/admin/stores/store-a/products/${item.id}/categories`, "PUT", { categoryIds: [] }],
    [`/api/admin/stores/store-a/products/${item.id}/attributes`, "PUT", { values: { safe: false, length: 0 } }],
  ]);
});
