import { afterEach, expect, it, vi } from "vitest";
import { staffRecoveryApi } from "./staffRecovery";
import { onUnauthorized } from "./http";
const signal = () => new AbortController().signal;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it("sends only the documented same-origin bodies, preserving raw password/token bytes", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response(null, { status: 202 })).mockResolvedValueOnce(new Response(null, { status: 204 })).mockResolvedValueOnce(new Response(null, { status: 200 })); vi.stubGlobal("fetch", fetch);
  const request = signal(); await staffRecoveryApi.forgot(" Staff@example.test ", request); await staffRecoveryApi.reset("fixture+proof/=", " raw password ", request); await staffRecoveryApi.signOut(request);
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body, options.signal])).toEqual([
    ["/api/admin/auth/password/forgot", "POST", '{"email":"Staff@example.test"}', request],
    ["/api/admin/auth/password/reset", "POST", '{"token":"fixture+proof/=","password":" raw password "}', request],
    ["/api/admin/auth/logout", "POST", undefined, request],
  ]);
});
it.each([200, 204])("does not infer request acceptance from empty %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status }))); await expect(staffRecoveryApi.forgot("staff@example.test", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it("rejects unexpected data attached to an accepted request", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ private: true }, 202))); await expect(staffRecoveryApi.forgot("staff@example.test", signal())).rejects.toMatchObject({ kind: "invalid-response", status: 202 });
});
it.each([200, 202, 205])("does not infer a reset from undocumented empty %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status }))); await expect(staffRecoveryApi.reset("fixture-proof", "password-value", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it("reads checked staff sign-in with no-store and treats only 401 as observed guest", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json({ id: "staff", email: "staff@example.test", role: "Admin", tenantId: "company", isTwoFactorEnabled: true })).mockResolvedValueOnce(json({}, 401)); vi.stubGlobal("fetch", fetch);
  expect(await staffRecoveryApi.session(signal())).toEqual({ email: "staff@example.test" }); expect(await staffRecoveryApi.session(signal())).toBeNull(); expect(fetch.mock.calls[0][1].cache).toBe("no-store");
});
it.each([null, {}, { email: "staff@example.test" }, { id: "staff", email: "staff@example.test", role: "Admin", tenantId: null }])("does not infer guest from malformed session %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(staffRecoveryApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([400, 401, 403, 404, 409, 429, 503])("keeps public status %s without session events, private diagnostics or replay", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ detail: "private-proof", traceId: "private-reference", errors: { password: ["private-password"] } }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "20" } })); vi.stubGlobal("fetch", fetch);
  const admin = vi.fn(), platform = vi.fn(), offAdmin = onUnauthorized(admin), offPlatform = onUnauthorized(platform, "platform");
  try {
    for (const action of [() => staffRecoveryApi.forgot("staff@example.test", signal()), () => staffRecoveryApi.reset("fixture-proof", "password-value", signal())]) {
      const error = await action().catch((value: unknown) => value); expect(error).toMatchObject({ status, problem: null, traceId: null, retryAfter: "20" }); expect(JSON.stringify(error)).not.toContain("private-");
    }
    expect(fetch).toHaveBeenCalledTimes(2); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled();
  } finally { offAdmin(); offPlatform(); }
});
it("discards an aborted response without invalidating either session", async () => {
  const request = new AbortController(); request.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
  await expect(staffRecoveryApi.reset("fixture-proof", "password-value", request.signal)).rejects.toMatchObject({ name: "AbortError" });
});
