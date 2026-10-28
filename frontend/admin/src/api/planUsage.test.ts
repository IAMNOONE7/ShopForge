import { afterEach, expect, it, vi } from "vitest";
import { HttpError, onUnauthorized } from "./http";
import { planLimitRefusal, planUsageApi } from "./planUsage";
import { planUsageFixture } from "../test/planUsageFixtures";

const signal = () => new AbortController().signal;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it("reads only the current company’s plan, uncached and without guessed parameters", async () => {
  const fetch = vi.fn().mockResolvedValue(json(planUsageFixture)); vi.stubGlobal("fetch", fetch); const request = signal(); expect(await planUsageApi.read(request)).toEqual(planUsageFixture);
  expect(fetch).toHaveBeenCalledExactlyOnceWith("/api/admin/plan", expect.objectContaining({ method: "GET", signal: request, cache: "no-store", body: undefined }));
});
it("preserves null metadata/caps, zero caps/counts, missing counters and unknown counter names", async () => {
  const data = { code: null, name: null, maxStores: null, maxProducts: 0, usage: [{ name: "future <metric>", value: 0 }] }; vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(data))); expect(await planUsageApi.read(signal())).toEqual(data);
});
it.each([
  null, [], {}, { ...planUsageFixture, maxStores: undefined }, { ...planUsageFixture, maxStores: -1 }, { ...planUsageFixture, maxProducts: 0.5 },
  { ...planUsageFixture, code: 3 }, { ...planUsageFixture, name: " " }, { ...planUsageFixture, usage: null },
  { ...planUsageFixture, usage: [null] }, { ...planUsageFixture, usage: [{ name: "stores", value: -1 }] },
  { ...planUsageFixture, usage: [{ name: "", value: 1 }] }, { ...planUsageFixture, usage: [{ name: "stores", value: "2" }] },
  { ...planUsageFixture, usage: [{ name: "stores", value: 1 }, { name: "stores", value: 2 }] },
])("rejects ambiguous or malformed usage %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(planUsageApi.read(signal())).rejects.toMatchObject({ kind: "invalid-response", status: 200 });
});
it("does not infer empty or uncapped usage from an undocumented 204", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 }))); await expect(planUsageApi.read(signal())).rejects.toMatchObject({ kind: "invalid-response", status: 204 });
});
it.each([401, 403, 404, 429, 503])("sanitizes read refusal %s without global session events or retries", async status => {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: "private-company", traceId: "private-reference" }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "10" } })); vi.stubGlobal("fetch", fetch);
  const admin = vi.fn(), platform = vi.fn(), offAdmin = onUnauthorized(admin), offPlatform = onUnauthorized(platform, "platform");
  try { expect(await planUsageApi.read(signal()).catch((error: unknown) => error)).toMatchObject({ status, problem: null, traceId: null, retryAfter: "10" }); expect(fetch).toHaveBeenCalledTimes(1); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled(); } finally { offAdmin(); offPlatform(); }
});
it("discards a successful but cancelled read", async () => {
  const request = new AbortController(); request.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(planUsageFixture))); await expect(planUsageApi.read(request.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it("recognizes only the actual store/product plan refusal, never another conflict or an invitation quota", () => {
  for (const resource of ["stores", "products"] as const) {
    const title = `This plan allows no more ${resource}`; expect(planLimitRefusal(new HttpError("http", 409, { title }), resource)).toBe(true);
    for (const error of [new HttpError("http", 400, { title }), new HttpError("invalid-response", 409, { title }), new HttpError("http", 409, { title: "A product with this SKU already exists" }), new HttpError("http", 409, { title: "The host name is already used by another store" }), new HttpError("http", 409, { detail: title }), new HttpError("http", 409, { title: "That address already belongs to somebody on ShopForge" })]) expect(planLimitRefusal(error, resource)).toBe(false);
  }
  expect(planLimitRefusal(new HttpError("http", 409, { title: "This plan allows no more products" }), "stores")).toBe(false);
});
