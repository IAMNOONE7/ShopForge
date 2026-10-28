import { afterEach, expect, it, vi } from "vitest";
import { operatorRecoveryApi } from "./operatorRecovery";
import { onUnauthorized } from "./http";
const signal = () => new AbortController().signal;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it.each([
  { id: "00000000-0000-0000-0000-000000000001", email: "operator@example.test" },
  { id: "00000000-0000-0000-0000-000000000001", email: "operator@example.test", isTwoFactorEnabled: true, tenantId: "company-a" },
  { id: "00000000-0000-0000-0000-000000000001", email: "operator@example.test", isTwoFactorEnabled: false, role: "Owner" },
])("rejects missing operator fields or staff identity contamination: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(operatorRecoveryApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it("sends only the documented same-origin bodies, preserving raw password/token bytes", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response(null, { status: 202 })).mockResolvedValueOnce(new Response(null, { status: 204 })).mockResolvedValueOnce(new Response(null, { status: 200 })); vi.stubGlobal("fetch", fetch);
  const request = signal(); await operatorRecoveryApi.forgot(" Operator@example.test ", request); await operatorRecoveryApi.reset("fixture+proof/=", " raw password ", request); await operatorRecoveryApi.signOut(request);
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body, options.signal])).toEqual([
    ["/api/platform/auth/password/forgot", "POST", '{"email":"Operator@example.test"}', request],
    ["/api/platform/auth/password/reset", "POST", '{"token":"fixture+proof/=","password":" raw password "}', request],
    ["/api/platform/auth/logout", "POST", undefined, request],
  ]);
});
it.each([200, 204])("does not infer request acceptance from empty %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status }))); await expect(operatorRecoveryApi.forgot("operator@example.test", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it("rejects unexpected data attached to an accepted request", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ private: true }, 202))); await expect(operatorRecoveryApi.forgot("operator@example.test", signal())).rejects.toMatchObject({ kind: "invalid-response", status: 202 });
});
it.each([200, 202, 205])("does not infer a reset from undocumented empty %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status }))); await expect(operatorRecoveryApi.reset("fixture-proof", "password-value", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it("reads checked operator sign-in with no-store and treats only 401 as observed guest", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json({ id: "00000000-0000-0000-0000-000000000001", email: "operator@example.test", isTwoFactorEnabled: true })).mockResolvedValueOnce(json({}, 401)); vi.stubGlobal("fetch", fetch);
  expect(await operatorRecoveryApi.session(signal())).toEqual({ email: "operator@example.test" }); expect(await operatorRecoveryApi.session(signal())).toBeNull(); expect(fetch.mock.calls[0][1].cache).toBe("no-store");
});
it.each([null, {}, { email: "operator@example.test" }, { id: "staff", email: "operator@example.test", role: "Admin", tenantId: null }])("does not infer guest from malformed session %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(operatorRecoveryApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([400, 401, 403, 404, 409, 429, 503])("keeps public status %s without session events, private diagnostics or replay", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ detail: "private-proof", traceId: "private-reference", errors: { password: ["private-password"] } }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "20" } })); vi.stubGlobal("fetch", fetch);
  const admin = vi.fn(), platform = vi.fn(), offAdmin = onUnauthorized(admin), offPlatform = onUnauthorized(platform, "platform");
  try {
    for (const action of [() => operatorRecoveryApi.forgot("operator@example.test", signal()), () => operatorRecoveryApi.reset("fixture-proof", "password-value", signal())]) {
      const error = await action().catch((value: unknown) => value); expect(error).toMatchObject({ status, problem: null, traceId: null, retryAfter: "20" }); expect(JSON.stringify(error)).not.toContain("private-");
    }
    expect(fetch).toHaveBeenCalledTimes(2); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled();
  } finally { offAdmin(); offPlatform(); }
});
it("discards an aborted response without invalidating either session", async () => {
  const request = new AbortController(); request.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
  await expect(operatorRecoveryApi.reset("fixture-proof", "password-value", request.signal)).rejects.toMatchObject({ name: "AbortError" });
});
