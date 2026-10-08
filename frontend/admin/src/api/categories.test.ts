import { afterEach, expect, it, vi } from "vitest";
import { api, type Category, type CategoryInput } from "../api";

afterEach(() => vi.unstubAllGlobals());

it("keeps category writes store-scoped, preserves omitted SEO and sends attribute order unchanged", async () => {
  const saved: Category = { id: "category-a", name: "Chairs", slug: "chairs", sortOrder: -2, parentId: "parent-a",
    attributeIds: ["colour", "material"], seoTitle: "Saved title", seoDescription: "Saved description", pageText: "Saved page text" };
  const fetch = vi.fn().mockImplementation(() => Promise.resolve(Response.json(saved)));
  vi.stubGlobal("fetch", fetch);
  const input: CategoryInput = { name: saved.name, slug: saved.slug, sortOrder: saved.sortOrder, parentId: saved.parentId };
  const controller = new AbortController();
  await api.categories("store-a", controller.signal);
  expect(await api.createCategory("store-a", { ...input, slug: null })).toEqual(saved);
  expect(await api.updateCategory("store-a", "category-a", input)).toEqual(saved);
  fetch.mockImplementationOnce(() => Promise.resolve(Response.json(saved.attributeIds)));
  expect(await api.assignCategoryAttributes("store-a", "category-a", saved.attributeIds)).toEqual(saved.attributeIds);
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body])).toEqual([
    ["/api/admin/stores/store-a/categories", "GET", undefined],
    ["/api/admin/stores/store-a/categories", "POST", JSON.stringify({ ...input, slug: null })],
    ["/api/admin/stores/store-a/categories/category-a", "PUT", JSON.stringify(input)],
    ["/api/admin/stores/store-a/categories/category-a/attributes", "PUT", JSON.stringify({ attributeIds: ["colour", "material"] })],
  ]);
  expect(fetch.mock.calls[0][1].signal).toBe(controller.signal);
});
