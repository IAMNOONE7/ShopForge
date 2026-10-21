import { afterEach, expect, it, vi } from "vitest";
import { platformApi } from "./platform";
import { onUnauthorized, requestJson } from "./http";
const id = "11111111-1111-7111-8111-111111111111", other = "22222222-2222-7222-8222-222222222222";
const user = { id, email: "operator@example.test", isTwoFactorEnabled: true };
const row = { id, name: "Workshop", status: "Active", planCode: null, usage: [{ name: "stores", value: 0 }] };
function reply(value: unknown, status = 200) { const fetch = vi.fn().mockImplementation(async () => status === 204 ? new Response(null, { status }) : Response.json(value, { status })); vi.stubGlobal("fetch", fetch); return fetch; }
afterEach(() => vi.unstubAllGlobals());
it("reads the separate operator identity without leaking extra fields", async () => {
  const fetch = reply({ ...user, extra: "ignored" }), controller = new AbortController(); expect(await platformApi.me(controller.signal)).toEqual(user);
  expect(fetch.mock.calls[0][0]).toBe("/api/platform/auth/me"); expect(fetch.mock.calls[0][1].signal).toBe(controller.signal);
});
it.each([null, {}, { ...user, id: "wrong" }, { ...user, isTwoFactorEnabled: undefined }, { ...user, role: "Owner" }, { ...user, tenantId: id }])("refuses an incomplete or tenant-scoped identity: %j", async value => {
  reply(value); await expect(platformApi.me()).rejects.toMatchObject({ kind: "invalid-response" });
});
it("sends credentials only in the separate POST and keeps an opaque challenge unauthenticated", async () => {
  const fetch = reply({ twoFactorRequired: true, ticket: "opaque-ticket", user: null });
  expect(await platformApi.login(user.email, "password")).toEqual({ twoFactorRequired: true, ticket: "opaque-ticket", user: null });
  expect(fetch.mock.calls[0][0]).toBe("/api/platform/auth/login"); expect(fetch.mock.calls[0][1].method).toBe("POST"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ email: user.email, password: "password" });
});
it("completes a challenge with its exact ticket/code body and a checked operator response", async () => {
  const fetch = reply({ twoFactorRequired: false, ticket: null, user }); expect((await platformApi.completeTwoFactor("opaque-ticket", "ABCD-EFGH")).user).toEqual(user);
  expect(fetch.mock.calls[0][0]).toBe("/api/platform/auth/two-factor"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ ticket: "opaque-ticket", code: "ABCD-EFGH" });
});
it.each([null, { twoFactorRequired: true, ticket: "", user: null }, { twoFactorRequired: true, ticket: "ticket", user }, { twoFactorRequired: false, ticket: "ticket", user }, { twoFactorRequired: false, ticket: null, user: null }])("refuses contradictory sign-in facts: %j", async value => {
  reply(value); await expect(platformApi.login(user.email, "password")).rejects.toMatchObject({ kind: "invalid-response" });
});
it("does not accept another challenge as completion", async () => { reply({ twoFactorRequired: true, ticket: "another", user: null }); await expect(platformApi.completeTwoFactor("ticket", "code")).rejects.toMatchObject({ kind: "invalid-response" }); });
it.each([200, 204])("accepts the backend's empty sign-out response %i", async status => {
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status })); vi.stubGlobal("fetch", fetch); await platformApi.logout(); expect(fetch.mock.calls[0][0]).toBe("/api/platform/auth/logout"); expect(fetch.mock.calls[0][1].body).toBeUndefined();
});
it("refuses a sign-out response with an unexpected body", async () => { reply({}); await expect(platformApi.logout()).rejects.toMatchObject({ kind: "invalid-response" }); });
it("retains unknown status, nullable plan and exact zero/unknown usage instead of guessing capabilities", async () => {
  reply([{ ...row, status: "FutureStatus", usage: [{ name: "future-metric", value: 0 }] }]); expect(await platformApi.tenants()).toEqual([{ ...row, status: "FutureStatus", usage: [{ name: "future-metric", value: 0 }] }]);
});
it.each([null, {}, [row, row], [{ ...row, planCode: undefined }], [{ ...row, usage: undefined }], [{ ...row, usage: [{ name: "stores", value: -1 }] }], [{ ...row, usage: [{ name: "stores", value: 1.5 }] }], [{ ...row, usage: [{ name: "stores", value: 9007199254740992 }] }], [{ ...row, usage: [row.usage[0], row.usage[0]] }]])("refuses incomplete company facts: %j", async value => {
  reply(value); await expect(platformApi.tenants()).rejects.toMatchObject({ kind: "invalid-response" });
});
it("keeps empty data distinct and ignores a read canceled after its response", async () => {
  reply([]); expect(await platformApi.tenants()).toEqual([]);
  const controller = new AbortController(); vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => { controller.abort(); return Response.json([row]); })); await expect(platformApi.tenants(controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it.each(["suspend", "resume"] as const)("uses the existing bodyless %s POST and accepts its empty usage without replacing list counts", async action => {
  const saved = { ...row, status: action === "suspend" ? "Suspended" : "Active", usage: [] }, fetch = reply(saved); expect(await platformApi.changeTenant(id, action)).toEqual(saved);
  expect(fetch.mock.calls[0][0]).toBe(`/api/platform/tenants/${id}/${action}`); expect(fetch.mock.calls[0][1].method).toBe("POST"); expect(fetch.mock.calls[0][1].body).toBeUndefined(); expect(fetch.mock.calls[0][1].headers).toBeUndefined();
});
it.each([{ ...row, id: other, status: "Suspended" }, row, { ...row, status: "Suspended", usage: undefined }, null])("does not acknowledge a missing, foreign or incorrect suspension reply: %j", async value => {
  const fetch = reply(value); await expect(platformApi.changeTenant(id, "suspend")).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
});
it("rejects unsafe identifiers before any network write", async () => { const fetch = reply(row); await expect(platformApi.changeTenant("../other", "suspend")).rejects.toBeInstanceOf(TypeError); expect(fetch).not.toHaveBeenCalled(); });
it.each([400, 401, 403, 409, 429, 503])("discards echoed credentials, tickets and diagnostics from status %s without replaying", async status => {
  const secret = "private-auth-canary", fetch = reply({ detail: secret, traceId: secret, errors: { password: [secret], ticket: [secret], code: [secret] } }, status);
  const error = await platformApi.login(user.email, secret).catch(error => error); expect(error).toMatchObject({ status, problem: null, traceId: null, retryAfter: null }); expect(JSON.stringify(error)).not.toContain(secret); expect(fetch).toHaveBeenCalledOnce();
});
it("keeps operator and merchant unauthorized events separate in both directions", async () => {
  const tenantListener = vi.fn(), platformListener = vi.fn(), offTenant = onUnauthorized(tenantListener), offPlatform = onUnauthorized(platformListener, "platform"); reply(null, 401);
  try {
    await expect(platformApi.me()).rejects.toMatchObject({ status: 401 }); await expect(platformApi.login(user.email, "wrong")).rejects.toMatchObject({ status: 401 }); await expect(platformApi.completeTwoFactor("ticket", "wrong")).rejects.toMatchObject({ status: 401 });
    expect(tenantListener).not.toHaveBeenCalled(); expect(platformListener).not.toHaveBeenCalled();
    await expect(platformApi.tenants()).rejects.toMatchObject({ status: 401 }); expect(platformListener).toHaveBeenCalledOnce(); expect(tenantListener).not.toHaveBeenCalled();
    await expect(requestJson("/api/admin/stores")).rejects.toMatchObject({ status: 401 }); expect(tenantListener).toHaveBeenCalledOnce(); expect(platformListener).toHaveBeenCalledOnce();
  } finally { offTenant(); offPlatform(); }
});
it("does not expire either session for a departed read's late 401", async () => {
  const listener = vi.fn(), off = onUnauthorized(listener, "platform"), controller = new AbortController();
  vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => { controller.abort(); return new Response(null, { status: 401 }); }));
  try { await expect(platformApi.tenants(controller.signal)).rejects.toMatchObject({ name: "AbortError" }); expect(listener).not.toHaveBeenCalled(); } finally { off(); }
});
