import { afterEach, expect, it, vi } from "vitest";
import { sameOperatorAccount, operatorAccountApi } from "./operatorAccount";
import { onUnauthorized } from "./http";

const operator = { id: "00000000-0000-0000-0000-000000000001", email: "Operator@example.test", isTwoFactorEnabled: true };
const signal = () => new AbortController().signal;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());

it("sends raw passwords in the documented body and the session command without a body", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response(null, { status: 204 })).mockResolvedValueOnce(new Response(null, { status: 204 })); vi.stubGlobal("fetch", fetch);
  const request = signal(); await operatorAccountApi.password(" old password ", " new password ", request); await operatorAccountApi.endOtherSessions(request);
  expect(fetch.mock.calls.map(([path, options]) => [path, options.method, options.body, options.signal, options.headers])).toEqual([
    ["/api/platform/account/password", "POST", '{"currentPassword":" old password ","newPassword":" new password "}', request, { "Content-Type": "application/json" }],
    ["/api/platform/account/sign-out-everywhere", "POST", undefined, request, undefined],
  ]);
});
it("checks required operator identity fields with an uncached read", async () => {
  const fetch = vi.fn().mockResolvedValue(json({ ...operator, isTwoFactorEnabled: true })); vi.stubGlobal("fetch", fetch);
  expect(await operatorAccountApi.session(signal())).toEqual(operator); expect(fetch.mock.calls[0][1].cache).toBe("no-store");
});
it.each([null, [], {}, { ...operator, id: " " }, { ...operator, tenantId: "company" }, { ...operator, email: 2 }, { ...operator, role: "Admin" }, { ...operator, isTwoFactorEnabled: null }])("rejects malformed session %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(operatorAccountApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([200, 202, 205])("does not accept undocumented %s as a confirmed change", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(null, { status })); vi.stubGlobal("fetch", fetch);
  await expect(operatorAccountApi.password("old", "new", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
  await expect(operatorAccountApi.endOtherSessions(signal())).rejects.toMatchObject({ kind: "invalid-response", status }); expect(fetch).toHaveBeenCalledTimes(2);
});
it.each([400, 401, 403, 404, 409, 429, 503])("strips diagnostics for %s, isolates session events and never retries a command", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ detail: "private-secret", traceId: "private-trace", errors: { newPassword: ["private-password"] } }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "10" } })); vi.stubGlobal("fetch", fetch);
  const admin = vi.fn(), platform = vi.fn(), offAdmin = onUnauthorized(admin), offPlatform = onUnauthorized(platform, "platform");
  try {
    for (const action of [() => operatorAccountApi.session(signal()), () => operatorAccountApi.password("old", "new", signal()), () => operatorAccountApi.endOtherSessions(signal())]) {
      const error = await action().catch((value: unknown) => value); expect(error).toMatchObject({ status, problem: null, traceId: null, retryAfter: "10" }); expect(JSON.stringify(error)).not.toContain("private-");
    }
    expect(fetch).toHaveBeenCalledTimes(3); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled();
  } finally { offAdmin(); offPlatform(); }
});
it("discards late successful reads and writes after departure", async () => {
  const request = new AbortController(); request.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json(operator)).mockResolvedValue(new Response(null, { status: 204 })));
  for (const action of [() => operatorAccountApi.session(request.signal), () => operatorAccountApi.password("old", "new", request.signal), () => operatorAccountApi.endOtherSessions(request.signal)]) await expect(action()).rejects.toMatchObject({ name: "AbortError" });
});
it("compares operator identity and two-factor state while allowing email case normalization", () => {
  expect(sameOperatorAccount(operator, { ...operator, email: " operator@EXAMPLE.test " })).toBe(true);
  for (const field of ["id", "email", "isTwoFactorEnabled"] as const) expect(sameOperatorAccount(operator, { ...operator, [field]: field === "isTwoFactorEnabled" ? false : "different" })).toBe(false);
});

it.each([202, 203, 206])("requires a checked 200 for the operator read, refusing %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(operator, status))); await expect(operatorAccountApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it("refuses a platform read without its two-factor flag", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ id: operator.id, email: operator.email }))); await expect(operatorAccountApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
