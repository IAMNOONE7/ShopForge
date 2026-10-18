import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api";
import type { CatalogAction, CatalogKind } from "./catalogLifecycle";

afterEach(() => vi.unstubAllGlobals());
it.each(["products", "categories"] as CatalogKind[])("uses the bodyless lifecycle contracts for %s", async (kind) => {
  const fetch = vi.fn().mockImplementation(async () => new Response(null, { status: 204 })); vi.stubGlobal("fetch", fetch);
  for (const action of ["archive", "restore", "delete"] as CatalogAction[]) await api.changeCatalogLifecycle("store/a", kind, "record/a", action);
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body])).toEqual([
    [`/api/admin/stores/store%2Fa/${kind}/record%2Fa/archive`, "POST", undefined],
    [`/api/admin/stores/store%2Fa/${kind}/record%2Fa/restore`, "POST", undefined],
    [`/api/admin/stores/store%2Fa/${kind}/record%2Fa`, "DELETE", undefined],
  ]);
});
it("retains guarded refusals and rejects an unexpected success body", async () => {
  const problem = { title: "This listing cannot be deleted", detail: "It cannot be deleted because it has been ordered. Archive it instead to take it out of use and keep it." };
  const fetch = vi.fn().mockResolvedValueOnce(Response.json(problem, { status: 409 })).mockResolvedValueOnce(Response.json({ id: "wrong" })); vi.stubGlobal("fetch", fetch);
  await expect(api.changeCatalogLifecycle("s", "products", "p", "delete")).rejects.toMatchObject({ status: 409, problem });
  await expect(api.changeCatalogLifecycle("s", "products", "p", "restore")).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledTimes(2);
});
it("requests a separate archived category collection with cancellation", async () => {
  const fetch = vi.fn().mockImplementation(async () => Response.json([])); vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
  await api.categories("s", signal, true); await api.categories("s", signal);
  expect(fetch.mock.calls.map(([url]) => url)).toEqual(["/api/admin/stores/s/categories?archived=true", "/api/admin/stores/s/categories"]); expect(fetch.mock.calls[0][1].signal).toBe(signal);
});
