import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api";
import type { BulkListingChange } from "./bulkListings";

afterEach(() => vi.unstubAllGlobals());
it.each([
  { action: "visibility", isVisible: false }, { action: "visibility", isVisible: true },
  { action: "price", set: 0 }, { action: "price", byPercent: -10.5 },
  { action: "categories", addCategoryIds: ["sale"], removeCategoryIds: ["old"] },
  { action: "tax-class", taxClass: "books" }, { action: "tax-class", taxClass: null },
  { action: "archive", archived: true }, { action: "archive", archived: false },
] satisfies BulkListingChange[])("sends only explicit deduplicated IDs and the $action contract", async change => {
  const fetch = vi.fn().mockResolvedValue(Response.json({ asked: 2, changed: 1 })); vi.stubGlobal("fetch", fetch);
  expect(await api.changeBulkListings("store/a", ["a", "b", "a"], change)).toEqual({ asked: 2, changed: 1 });
  const { action, ...details } = change;
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/store%2Fa/products/bulk/${action}`);
  expect(fetch.mock.calls[0][1].method).toBe("POST"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ storeProductIds: ["a", "b"], ...details }); expect(fetch).toHaveBeenCalledTimes(1);
});
it("accepts zero changed and exactly 500 IDs without claiming failed rows", async () => {
  const fetch = vi.fn().mockResolvedValue(Response.json({ asked: 500, changed: 0 })); vi.stubGlobal("fetch", fetch);
  expect(await api.changeBulkListings("s", Array.from({ length: 500 }, (_, i) => `id-${i}`), { action: "visibility", isVisible: false })).toEqual({ asked: 500, changed: 0 });
});
it.each([{ ids: [] }, { ids: Array.from({ length: 501 }, (_, i) => `id-${i}`) }])("refuses an out-of-bound selection before sending", async ({ ids }) => {
  const fetch = vi.fn(); vi.stubGlobal("fetch", fetch);
  await expect(api.changeBulkListings("s", ids, { action: "archive", archived: true })).rejects.toBeInstanceOf(RangeError); expect(fetch).not.toHaveBeenCalled();
});
it.each([undefined, null, {}, { asked: 1, changed: 0 }, { asked: 2, changed: -1 }, { asked: 2, changed: 3 }, { asked: 2, changed: 0.5 }, { asked: "2", changed: 1 }])("rejects malformed or mismatched counts as an unconfirmed outcome: %j", async result => {
  const fetch = vi.fn().mockResolvedValue(result === undefined ? new Response(null, { status: 204 }) : Response.json(result)); vi.stubGlobal("fetch", fetch);
  await expect(api.changeBulkListings("s", ["a", "b"], { action: "visibility", isVisible: false })).rejects.toMatchObject({ kind: "invalid-response" });
});
it("preserves a refusal without automatically resending", async () => {
  const problem = { errors: { addCategoryIds: ["One or more categories were not found."] } };
  const fetch = vi.fn().mockResolvedValue(Response.json(problem, { status: 400 })); vi.stubGlobal("fetch", fetch);
  await expect(api.changeBulkListings("s", ["a"], { action: "categories", addCategoryIds: ["gone"], removeCategoryIds: [] })).rejects.toMatchObject({ status: 400, problem }); expect(fetch).toHaveBeenCalledTimes(1);
});
