import { afterEach, expect, it, vi } from "vitest";
import { onUnauthorized } from "./http";
import { operatorTwoFactorApi } from "./operatorTwoFactor";
import { recoveryCodes, securityOperator, twoFactorSetup } from "../test/operatorTwoFactorFixtures";

const signal = () => new AbortController().signal;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
it("uses the actual begin/confirm/off contracts, raw proof bodies and uncached checked operator status", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json(securityOperator)).mockResolvedValueOnce(json(twoFactorSetup)).mockResolvedValueOnce(json({ recoveryCodes })).mockResolvedValueOnce(new Response(null, { status: 204 })); vi.stubGlobal("fetch", fetch);
  const request = signal(); expect(await operatorTwoFactorApi.session(request)).toEqual(securityOperator); expect(await operatorTwoFactorApi.begin(securityOperator.email, request)).toEqual(twoFactorSetup); expect(await operatorTwoFactorApi.confirm("012345", request)).toEqual(recoveryCodes); await operatorTwoFactorApi.turnOff(" raw password ", request);
  expect(fetch.mock.calls.map(([path, options]) => [path, options.method, options.body, options.signal])).toEqual([
    ["/api/platform/auth/me", "GET", undefined, request], ["/api/platform/account/two-factor", "POST", undefined, request],
    ["/api/platform/account/two-factor/confirm", "POST", '{"code":"012345"}', request], ["/api/platform/account/two-factor/off", "POST", '{"password":" raw password "}', request],
  ]); expect(fetch.mock.calls[0][1].cache).toBe("no-store"); expect(fetch.mock.calls.every(([, options]) => !options.headers?.["Idempotency-Key"])).toBe(true);
});
it.each([null, [], {}, { ...securityOperator, isTwoFactorEnabled: undefined }, { ...securityOperator, isTwoFactorEnabled: "false" }, { ...securityOperator, id: "" }, { ...securityOperator, tenantId: "company" }, { ...securityOperator, role: "Admin" }])("rejects malformed security status %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(operatorTwoFactorApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([
  {}, { ...twoFactorSetup, secret: "invalid" }, { ...twoFactorSetup, enrolmentUri: "https://outside.test/?secret=private" },
  { ...twoFactorSetup, enrolmentUri: twoFactorSetup.enrolmentUri.replace("totp/", "hotp/") },
  { ...twoFactorSetup, enrolmentUri: twoFactorSetup.enrolmentUri.replace("operator%40example.test", "other%40example.test") },
  { ...twoFactorSetup, enrolmentUri: twoFactorSetup.enrolmentUri.replace("period=30", "period=60") },
  { ...twoFactorSetup, enrolmentUri: twoFactorSetup.enrolmentUri + "&issuer=ShopForge%20Platform" },
  { ...twoFactorSetup, enrolmentUri: twoFactorSetup.enrolmentUri + "#private" },
])("rejects untrusted or inconsistent enrolment data %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(operatorTwoFactorApi.begin(securityOperator.email, signal())).rejects.toMatchObject({ kind: "invalid-response", problem: null, traceId: null });
});
it.each([{}, { recoveryCodes: [] }, { recoveryCodes: recoveryCodes.slice(1) }, { recoveryCodes: Array(10).fill(recoveryCodes[0]) }, { recoveryCodes: [...recoveryCodes.slice(1), "wrong-format"] }])("rejects missing, duplicate or malformed recovery data %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(operatorTwoFactorApi.confirm("012345", signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([202, 204, 205])("rejects undocumented setup acknowledgment %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => new Response(null, { status })));
  await expect(operatorTwoFactorApi.begin(securityOperator.email, signal())).rejects.toMatchObject({ kind: "invalid-response", status }); await expect(operatorTwoFactorApi.confirm("012345", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it("requires strict 204 to acknowledge disabling", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({}))); await expect(operatorTwoFactorApi.turnOff("raw password", signal())).rejects.toMatchObject({ kind: "invalid-response", status: 200 });
});
it.each([400, 401, 403, 409, 429, 503])("sanitizes status %s without replay or either global auth event", async status => {
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ detail: "private-material", errors: { code: ["private-material"] }, traceId: "private-material" }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "10" } })); vi.stubGlobal("fetch", fetch);
  const admin = vi.fn(), platform = vi.fn(), offAdmin = onUnauthorized(admin), offPlatform = onUnauthorized(platform, "platform");
  try {
    for (const action of [() => operatorTwoFactorApi.session(signal()), () => operatorTwoFactorApi.begin(securityOperator.email, signal()), () => operatorTwoFactorApi.confirm("012345", signal()), () => operatorTwoFactorApi.turnOff("raw password", signal())]) expect(await action().catch((error: unknown) => error)).toMatchObject({ status, problem: null, traceId: null, retryAfter: "10" });
    expect(fetch).toHaveBeenCalledTimes(4); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled();
  } finally { offAdmin(); offPlatform(); }
});
it("drops successful secret responses after cancellation", async () => {
  const request = new AbortController(); request.abort(); vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json(securityOperator)).mockResolvedValueOnce(json(twoFactorSetup)).mockResolvedValueOnce(json({ recoveryCodes })).mockResolvedValueOnce(new Response(null, { status: 204 })));
  for (const action of [() => operatorTwoFactorApi.session(request.signal), () => operatorTwoFactorApi.begin(securityOperator.email, request.signal), () => operatorTwoFactorApi.confirm("012345", request.signal), () => operatorTwoFactorApi.turnOff("raw password", request.signal)]) await expect(action()).rejects.toMatchObject({ name: "AbortError" });
});

it.each([
  twoFactorSetup.enrolmentUri.replaceAll("ShopForge%20Platform", "ShopForge"),
  twoFactorSetup.enrolmentUri.replace("issuer=ShopForge%20Platform", "issuer=ShopForge"),
  twoFactorSetup.enrolmentUri.replace("ShopForge%20Platform:", "ShopForge:"),
])("refuses a company issuer for an operator even when the email and key match", async enrolmentUri => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ ...twoFactorSetup, enrolmentUri }))); await expect(operatorTwoFactorApi.begin(securityOperator.email, signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([202, 203, 206])("refuses plausible data with an undocumented %s response", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json(twoFactorSetup, status)).mockResolvedValueOnce(json({ recoveryCodes }, status)));
  await expect(operatorTwoFactorApi.begin(securityOperator.email, signal())).rejects.toMatchObject({ kind: "invalid-response", status }); await expect(operatorTwoFactorApi.confirm("012345", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
