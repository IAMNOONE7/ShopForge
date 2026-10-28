import { afterEach, expect, it, vi } from "vitest";
import { onUnauthorized } from "./http";
import { tenantAuditApi } from "./tenantAudit";
import { auditRows, auditStore, defaultAuditQuery } from "../test/tenantAuditFixtures";

const signal = () => new AbortController().signal;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it("reads only the current tenant endpoint with documented filters, no-store and preserved record bytes", async () => {
  const query = { ...defaultAuditQuery, storeId: auditStore.id, action: auditRows[0].action, from: auditRows[0].recordedAt, to: "2026-10-28T08:30:00.1234568Z", page: 2, pageSize: 25 };
  const fetch = vi.fn().mockResolvedValue(json([auditRows[0]])); vi.stubGlobal("fetch", fetch); expect(await tenantAuditApi.read(query, signal())).toEqual([auditRows[0]]);
  const [url, options] = fetch.mock.calls[0]; expect(url).toBe(`/api/admin/audit?storeId=${auditStore.id}&action=order.refunded&from=2026-10-28T08%3A30%3A00.1234567Z&to=2026-10-28T08%3A30%3A00.1234568Z&page=2&pageSize=25`); expect(options).toMatchObject({ method: "GET", cache: "no-store", body: undefined }); expect(fetch).toHaveBeenCalledTimes(1);
});
it("accepts empty pages, nullable facts, unknown actor kinds and already-shortened plain details", async () => {
  const rows = [{ ...auditRows[2], actorKind: "FutureActor <literal>" }]; vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json(rows)).mockResolvedValueOnce(json([]))); expect(await tenantAuditApi.read(defaultAuditQuery, signal())).toEqual(rows); expect(await tenantAuditApi.read(defaultAuditQuery, signal())).toEqual([]);
});
it.each([null, {}, [null], [{ ...auditRows[0], id: "bad" }], [{ ...auditRows[0], recordedAt: "2026-02-30T00:00:00Z" }], [{ ...auditRows[0], actorName: undefined }], [{ ...auditRows[0], actorId: "bad" }], [{ ...auditRows[0], details: {} }], [{ ...auditRows[0], action: "" }], [auditRows[0], auditRows[0]]])("rejects malformed or ambiguous records %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(tenantAuditApi.read(defaultAuditQuery, signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([{ storeId: "cccccccc-1111-2222-3333-444444444444" }, { action: "another.action" }, { from: "2026-10-28T08:30:00.1234568Z" }, { to: auditRows[0].recordedAt }])("does not display rows violating an applied server filter %j", async filter => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json([auditRows[0]]))); await expect(tenantAuditApi.read({ ...defaultAuditQuery, ...filter }, signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it("rejects a response larger than the requested page and undocumented successful status", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json(auditRows)).mockResolvedValueOnce(new Response(null, { status: 204 }))); await expect(tenantAuditApi.read({ ...defaultAuditQuery, pageSize: 1 }, signal())).rejects.toMatchObject({ kind: "invalid-response" }); await expect(tenantAuditApi.read(defaultAuditQuery, signal())).rejects.toMatchObject({ kind: "invalid-response", status: 204 });
});
it.each([400, 401, 403, 404, 429, 503])("sanitizes a refusal %s without invalidating either global session or retrying", async status => {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: "private-event", traceId: "private-reference" }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "10" } })); vi.stubGlobal("fetch", fetch); const admin = vi.fn(), platform = vi.fn(), offAdmin = onUnauthorized(admin), offPlatform = onUnauthorized(platform, "platform");
  try { expect(await tenantAuditApi.read(defaultAuditQuery, signal()).catch((error: unknown) => error)).toMatchObject({ status, problem: null, traceId: null, retryAfter: "10" }); expect(fetch).toHaveBeenCalledTimes(1); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled(); } finally { offAdmin(); offPlatform(); }
});
it("does not send invalid filters or publish cancelled reads", async () => {
  const fetch = vi.fn().mockResolvedValue(json(auditRows)); vi.stubGlobal("fetch", fetch); await expect(tenantAuditApi.read({ ...defaultAuditQuery, page: 0 }, signal())).rejects.toBeInstanceOf(TypeError); expect(fetch).not.toHaveBeenCalled(); const request = new AbortController(); request.abort(); await expect(tenantAuditApi.read(defaultAuditQuery, request.signal)).rejects.toMatchObject({ name: "AbortError" });
});
