import { afterEach, expect, it, vi } from "vitest";
import { customerEmailApi } from "./customerEmail";
import { onUnauthorized } from "./http";
const profile = { email: "ada@example.test", firstName: "Ada", lastName: "Lovelace", phone: null };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it("uses exact token/password bodies on same-origin paths with cancellation and no automatic session invalidation", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(new Response(null, { status: 202 })).mockResolvedValueOnce(new Response(null, { status: 202 }))
    .mockResolvedValueOnce(json(profile)).mockResolvedValueOnce(json(profile)).mockResolvedValueOnce(new Response(null, { status: 200 })); vi.stubGlobal("fetch", fetch);
  const signal = new AbortController().signal;
  await customerEmailApi.resend("ada@example.test", signal); await customerEmailApi.request("new@example.test", " raw password ", signal);
  expect(await customerEmailApi.confirm("fixture-proof-token", signal)).toEqual(profile); await customerEmailApi.profile(signal); await customerEmailApi.logout(signal);
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body, options.signal])).toEqual([
    ["/api/storefront/account/verification/resend", "POST", '{"email":"ada@example.test"}', signal],
    ["/api/storefront/account/email", "POST", '{"newEmail":"new@example.test","password":" raw password "}', signal],
    ["/api/storefront/account/email/confirm", "POST", '{"token":"fixture-proof-token"}', signal],
    ["/api/storefront/account/me", "GET", undefined, signal], ["/api/storefront/account/logout", "POST", undefined, signal],
  ]);
  expect(fetch.mock.calls[3][1].cache).toBe("no-store");
});
it.each(["resend", "request"] as const)("does not infer acceptance from an unexpected %s response body", async kind => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ queued: true })));
  await expect(kind === "resend" ? customerEmailApi.resend("a@example.test", new AbortController().signal) : customerEmailApi.request("a@example.test", "password", new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([null, {}, { ...profile, email: "" }, { ...profile, firstName: undefined }, { ...profile, lastName: 12 }, { ...profile, phone: 17 }])("rejects malformed confirmation/profile answers instead of signing a customer in: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => json(value)));
  await expect(customerEmailApi.confirm("fixture-proof-token", new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
  await expect(customerEmailApi.profile(new AbortController().signal)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("normalizes a confirmed customer and keeps credentials out of server diagnostics while retaining field targets", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json({ ...profile, token: "not-a-profile-field" })).mockResolvedValueOnce(json({ detail: "private-email-and-token", traceId: "private-reference", errors: { newEmail: ["private-address"], token: ["private-token"] } }, 400)); vi.stubGlobal("fetch", fetch);
  expect(await customerEmailApi.confirm("fixture-proof-token", new AbortController().signal)).toEqual(profile);
  try { await customerEmailApi.request("a@example.test", "password", new AbortController().signal); throw new Error("Expected refusal"); }
  catch (error) { expect(error).toMatchObject({ status: 400, problem: { errors: { newEmail: [] } }, traceId: null }); expect(JSON.stringify(error)).not.toContain("private-"); }
});
it.each([400, 401, 403, 409, 429, 500])("retains status %s without mutating the global session or replaying a confirmation", async status => {
  const fetch = vi.fn().mockResolvedValue(json({ detail: "private" }, status)); vi.stubGlobal("fetch", fetch);
  const listener = vi.fn(), unsubscribe = onUnauthorized(listener);
  try { await expect(customerEmailApi.confirm("fixture-proof-token", new AbortController().signal)).rejects.toMatchObject({ status }); expect(fetch).toHaveBeenCalledTimes(1); expect(listener).not.toHaveBeenCalled(); }
  finally { unsubscribe(); }
});
