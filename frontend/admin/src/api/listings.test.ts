import { afterEach, expect, it, vi } from "vitest";
import { api, type StoreProduct } from "../api";

const item: StoreProduct = { id: "listing-a", productId: "physical-a", sku: "S", name: "Table", slug: "table", description: null,
  price: 100, vatRate: 21, isVisible: true, sortOrder: 0, categoryIds: [], seoTitle: "Saved title", seoDescription: null, seoSocialImageUrl: null, seoNoIndex: true };
const first = { items: [{ ...item, id: "earlier", isVisible: false }], page: 1, pageSize: 200, totalCount: 201, hasMore: true };
const last = { items: [item], page: 2, pageSize: 200, totalCount: 201, hasMore: false };
afterEach(() => vi.unstubAllGlobals());
function responses(...values: unknown[]) { const fetch = vi.fn(); values.forEach((value) => fetch.mockResolvedValueOnce(Response.json(value))); vi.stubGlobal("fetch", fetch); return fetch; }

it("uses server pages, sort and escaped search rather than narrowing only loaded rows", async () => {
  const fetch = responses(first); const signal = new AbortController().signal;
  expect(await api.storeProducts("store-a", signal, { page: 2, pageSize: 25, q: "50% & SKU", sort: "-price" })).toEqual(first);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store-a/products?page=2&pageSize=25&sort=-price&q=50%25+%26+SKU");
  expect(fetch.mock.calls[0][1].signal).toBe(signal);
});
it("resolves a canonical listing ID beyond the first page without inventing a detail endpoint", async () => {
  const fetch = responses(first, last);
  expect(await api.findStoreProduct("store-a", item.id)).toEqual(item);
  expect(fetch.mock.calls.map(([url]) => url)).toEqual(["/api/admin/stores/store-a/products?page=1&pageSize=200", "/api/admin/stores/store-a/products?page=2&pageSize=200"]);
});
it("checks publication across all pages and ends early when a visible listing is found", async () => {
  const fetch = responses(first, last, { ...first, items: [item] });
  expect(await api.hasVisibleStoreProduct("store-a")).toBe(true);
  expect(await api.hasVisibleStoreProduct("store-a")).toBe(true);
  expect(fetch).toHaveBeenCalledTimes(3);
});
it("distinguishes no active match from a later-page failure and stops aborted scans", async () => {
  const fetch = responses(first, { ...last, items: [] }, first);
  expect(await api.findStoreProduct("store-a", "absent")).toBeNull();
  fetch.mockRejectedValueOnce(new Error("offline"));
  await expect(api.findStoreProduct("store-a", item.id)).rejects.toMatchObject({ kind: "network" });
  const controller = new AbortController(); responses(first); controller.abort();
  await expect(api.findStoreProduct("store-a", item.id, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it("writes independent listing, category and typed values payloads, omitting existing SEO", async () => {
  const fetch = responses(item, item, { values: { safe: false, length: 0 } });
  const input = { name: "New name", slug: "table", price: 0, vatRate: 0, isVisible: false, sortOrder: -1, description: null };
  await api.updateStoreProduct("store-a", item.id, input); await api.assignCategories("store-a", item.id, []); await api.setProductAttributes("store-a", item.id, { safe: false, length: 0 });
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, JSON.parse(options.body)])).toEqual([
    ["/api/admin/stores/store-a/products/listing-a", "PUT", input],
    ["/api/admin/stores/store-a/products/listing-a/categories", "PUT", { categoryIds: [] }],
    ["/api/admin/stores/store-a/products/listing-a/attributes", "PUT", { values: { safe: false, length: 0 } }],
  ]);
});
