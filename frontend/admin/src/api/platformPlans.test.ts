import { afterEach, expect, it, vi } from "vitest";
import { onUnauthorized } from "./http";
import { platformPlansApi, samePlan } from "./platformPlans";
import { platformPlan, newPlatformPlan } from "../test/platformPlanFixtures";

const signal = () => new AbortController().signal;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const values = { code: "growth", name: "Growth", maxStores: 5, maxProducts: null };
afterEach(() => vi.unstubAllGlobals());
it("uses checked list/create/update contracts without tenant, default, delete or assignment writes", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json([platformPlan])).mockResolvedValueOnce(json(newPlatformPlan, 201)).mockResolvedValueOnce(json({ ...platformPlan, name: "Changed", maxStores: 0 })); vi.stubGlobal("fetch", fetch); const request = signal();
  expect(await platformPlansApi.list(request)).toEqual([platformPlan]); expect(await platformPlansApi.create(values, request)).toEqual(newPlatformPlan); await platformPlansApi.update(platformPlan, { ...platformPlan, name: "Changed", maxStores: 0 }, request);
  expect(fetch.mock.calls.map(([path, options]) => [path, options.method, options.body, options.signal])).toEqual([
    ["/api/platform/plans", "GET", undefined, request], ["/api/platform/plans", "POST", JSON.stringify(values), request],
    [`/api/platform/plans/${platformPlan.id}`, "PUT", '{"name":"Changed","maxStores":0,"maxProducts":0}', request],
  ]); expect(fetch.mock.calls.every(([, options]) => options.cache === "no-store" && !options.headers?.["Idempotency-Key"])).toBe(true);
});
it("accepts an empty catalogue and a catalogue with no recorded default", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json([])).mockResolvedValueOnce(json([newPlatformPlan]))); expect(await platformPlansApi.list(signal())).toEqual([]); expect(await platformPlansApi.list(signal())).toEqual([newPlatformPlan]);
});
it.each([null, {}, [null], [{ ...platformPlan, id: "bad" }], [{ ...platformPlan, name: " " }], [{ ...platformPlan, name: "x".repeat(101) }], [{ ...platformPlan, code: "x".repeat(41) }], [{ ...platformPlan, maxStores: -1 }], [{ ...platformPlan, maxProducts: 1.5 }], [{ ...platformPlan, maxStores: 2147483648 }], [{ ...platformPlan, isDefault: null }], [{ ...platformPlan, maxStores: undefined }]])("rejects malformed catalogue data %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(platformPlansApi.list(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([
  [platformPlan, { ...newPlatformPlan, id: platformPlan.id.toUpperCase() }],
  [platformPlan, { ...newPlatformPlan, code: platformPlan.code }],
  [platformPlan, { ...newPlatformPlan, isDefault: true }],
].map(rows => [rows]))("refuses duplicate identities, codes and default markers", async rows => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(rows))); await expect(platformPlansApi.list(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([201, 202, 203, 206])("requires a list 200, refusing plausible data on %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json([platformPlan], status))); await expect(platformPlansApi.list(signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it.each([200, 202])("requires a create 201, refusing %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(newPlatformPlan, status))); await expect(platformPlansApi.create(values, signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it.each([201, 202])("requires an update 200, refusing %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(platformPlan, status))); await expect(platformPlansApi.update(platformPlan, platformPlan, signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it.each(["code", "name", "maxStores", "maxProducts", "isDefault"] as const)("refuses a create response with inconsistent %s", async field => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ ...newPlatformPlan, [field]: field === "isDefault" ? true : field.startsWith("max") ? 9 : "other" }, 201))); await expect(platformPlansApi.create(values, signal())).rejects.toMatchObject({ kind: "invalid-response", status: 201 });
});
it.each(["id", "code", "name", "maxStores", "maxProducts", "isDefault"] as const)("refuses an update response with inconsistent %s", async field => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ ...platformPlan, [field]: field === "isDefault" ? false : field.startsWith("max") ? 9 : field === "id" ? newPlatformPlan.id : "other" }))); await expect(platformPlansApi.update(platformPlan, platformPlan, signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it("keeps only known validation field names and strips echoed material", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { name: ["private-plan"], maxStores: ["private-value"], "private-field": ["private-data"] }, detail: "private-detail", traceId: "private-trace" }), { status: 400, headers: { "Content-Type": "application/problem+json" } })));
  const error = await platformPlansApi.create(values, signal()).catch((value: unknown) => value); expect(error).toMatchObject({ status: 400, problem: { errors: { name: [], maxStores: [] } }, traceId: null }); expect(JSON.stringify(error)).not.toContain("private-");
});
it.each([401, 403, 404, 409, 429, 503])("isolates status %s from both auth scopes without retry or private diagnostics", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ detail: "private-data", traceId: "private-trace" }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "10" } })); vi.stubGlobal("fetch", fetch);
  const admin = vi.fn(), platform = vi.fn(), offAdmin = onUnauthorized(admin), offPlatform = onUnauthorized(platform, "platform");
  try { for (const action of [() => platformPlansApi.list(signal()), () => platformPlansApi.create(values, signal()), () => platformPlansApi.update(platformPlan, platformPlan, signal())]) expect(await action().catch((error: unknown) => error)).toMatchObject({ status, problem: null, traceId: null, retryAfter: "10" }); expect(fetch).toHaveBeenCalledTimes(3); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled(); } finally { offAdmin(); offPlatform(); }
});
it("drops late reads and replies after cancellation", async () => {
  const request = new AbortController(); request.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json([platformPlan])).mockResolvedValueOnce(json(newPlatformPlan, 201)).mockResolvedValueOnce(json(platformPlan)));
  for (const action of [() => platformPlansApi.list(request.signal), () => platformPlansApi.create(values, request.signal), () => platformPlansApi.update(platformPlan, platformPlan, request.signal)]) await expect(action()).rejects.toMatchObject({ name: "AbortError" });
});
it("compares every saved term and accepts only GUID case differences", () => {
  expect(samePlan(platformPlan, { ...platformPlan, id: platformPlan.id.toUpperCase() })).toBe(true);
  for (const field of ["id", "code", "name", "maxStores", "maxProducts", "isDefault"] as const) expect(samePlan(platformPlan, { ...platformPlan, [field]: field === "isDefault" ? false : field.startsWith("max") ? 7 : "different" })).toBe(false);
});
