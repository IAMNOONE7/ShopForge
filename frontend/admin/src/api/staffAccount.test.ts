import { afterEach, expect, it, vi } from "vitest";
import { sameStaffAccount, staffAccountApi } from "./staffAccount";
import { onUnauthorized } from "./http";

const staff = { id: "staff-a", email: "Staff@example.test", role: "Support", tenantId: "company-a" };
const signal = () => new AbortController().signal;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());

it("sends raw passwords in the documented body and the session command without a body", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response(null, { status: 204 })).mockResolvedValueOnce(new Response(null, { status: 204 })); vi.stubGlobal("fetch", fetch);
  const request = signal(); await staffAccountApi.password(" old password ", " new password ", request); await staffAccountApi.endOtherSessions(request);
  expect(fetch.mock.calls.map(([path, options]) => [path, options.method, options.body, options.signal, options.headers])).toEqual([
    ["/api/admin/account/password", "POST", '{"currentPassword":" old password ","newPassword":" new password "}', request, { "Content-Type": "application/json" }],
    ["/api/admin/account/sign-out-everywhere", "POST", undefined, request, undefined],
  ]);
});
it("checks required staff identity fields with an uncached read", async () => {
  const fetch = vi.fn().mockResolvedValue(json({ ...staff, isTwoFactorEnabled: true })); vi.stubGlobal("fetch", fetch);
  expect(await staffAccountApi.session(signal())).toEqual(staff); expect(fetch.mock.calls[0][1].cache).toBe("no-store");
});
it.each([null, [], {}, { ...staff, id: " " }, { ...staff, tenantId: null }, { ...staff, email: 2 }, { ...staff, role: "" }])("rejects malformed session %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(staffAccountApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([200, 202, 205])("does not accept undocumented %s as a confirmed change", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(null, { status })); vi.stubGlobal("fetch", fetch);
  await expect(staffAccountApi.password("old", "new", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
  await expect(staffAccountApi.endOtherSessions(signal())).rejects.toMatchObject({ kind: "invalid-response", status }); expect(fetch).toHaveBeenCalledTimes(2);
});
it.each([400, 401, 403, 404, 409, 429, 503])("strips diagnostics for %s, isolates session events and never retries a command", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ detail: "private-secret", traceId: "private-trace", errors: { newPassword: ["private-password"] } }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "10" } })); vi.stubGlobal("fetch", fetch);
  const admin = vi.fn(), platform = vi.fn(), offAdmin = onUnauthorized(admin), offPlatform = onUnauthorized(platform, "platform");
  try {
    for (const action of [() => staffAccountApi.session(signal()), () => staffAccountApi.password("old", "new", signal()), () => staffAccountApi.endOtherSessions(signal())]) {
      const error = await action().catch((value: unknown) => value); expect(error).toMatchObject({ status, problem: null, traceId: null, retryAfter: "10" }); expect(JSON.stringify(error)).not.toContain("private-");
    }
    expect(fetch).toHaveBeenCalledTimes(3); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled();
  } finally { offAdmin(); offPlatform(); }
});
it("discards late successful reads and writes after departure", async () => {
  const request = new AbortController(); request.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json(staff)).mockResolvedValue(new Response(null, { status: 204 })));
  for (const action of [() => staffAccountApi.session(request.signal), () => staffAccountApi.password("old", "new", request.signal), () => staffAccountApi.endOtherSessions(request.signal)]) await expect(action()).rejects.toMatchObject({ name: "AbortError" });
});
it("compares caller and company access while allowing email case normalization", () => {
  expect(sameStaffAccount(staff, { ...staff, email: " staff@EXAMPLE.test " })).toBe(true);
  for (const field of ["id", "tenantId", "email", "role"] as const) expect(sameStaffAccount(staff, { ...staff, [field]: "different" })).toBe(false);
});
