import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api";
import { connectionFailure, type ConnectionInput } from "./providerConnections";
import { HttpError } from "./http";
const row = { provider: "comgate", merchantId: "merchant-a", environment: "Test", isActive: false, hasSecret: true, publishableKey: null, changedAt: "2026-10-20T12:00:00Z" };
const input: ConnectionInput = { merchantId: "merchant-a", environment: "Test", isActive: false, publishableKey: null };
const credential = "credential-canary-for-contract-tests";
function fetchReply(body: unknown, status = 200) { const fetch = vi.fn().mockImplementation(async () => status === 204 ? new Response(null, { status }) : Response.json(body, { status })); vi.stubGlobal("fetch", fetch); return fetch; }
afterEach(() => vi.unstubAllGlobals());
it("reads complete saved and unknown environments without adding readiness or credential values", async () => {
  const fetch = fetchReply([{ ...row, environment: "Future", secret: credential, secretName: "private-reference" }]); const controller = new AbortController();
  expect(await api.providerConnections("a/b", controller.signal)).toEqual([{ ...row, environment: "Future" }]); expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/provider-connections"); expect(fetch.mock.calls[0][1].signal).toBe(controller.signal);
});
it.each([undefined, null, {}, [null], [{ ...row, provider: "" }], [{ ...row, provider: "COMGATE" }], [{ ...row, merchantId: null }], [{ ...row, merchantId: " " }],
  [{ ...row, environment: undefined }], [{ ...row, hasSecret: undefined }], [{ ...row, isActive: "false" }], [{ ...row, publishableKey: undefined }], [{ ...row, publishableKey: 1 }],
  [{ ...row, publishableKey: "x".repeat(201) }], [{ ...row, changedAt: "not-a-date" }], [{ ...row, changedAt: "2026-99-99T12:00:00Z" }], [row, row]])("refuses malformed connection reads rather than defaulting saved values: %j", async body => {
  if (body === undefined) fetchReply(null, 204); else fetchReply(body); await expect(api.providerConnections("a")).rejects.toMatchObject({ kind: "invalid-response" });
});
it("distinguishes an empty collection and ignores a late canceled read", async () => {
  fetchReply([]); expect(await api.providerConnections("a")).toEqual([]);
  const controller = new AbortController(); vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => { controller.abort(); return Response.json([row]); })); await expect(api.providerConnections("a", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it("discovers actual payment and shipping keys, including future providers, without inventing capabilities", async () => {
  const fetch = vi.fn().mockImplementation(async url => Response.json(String(url).endsWith("/payment-providers") ? ["comgate", "manual", "future"] : ["manual", "packeta"])); vi.stubGlobal("fetch", fetch);
  expect(await api.providerChoices("a/b")).toEqual({ payment: ["comgate", "manual", "future"], shipping: ["manual", "packeta"] }); expect(fetch.mock.calls.map(([url]) => url)).toEqual(["/api/admin/stores/a%2Fb/payment-providers", "/api/admin/stores/a%2Fb/shipping-providers"]);
});
it.each([null, {}, [""], ["Comgate"], ["comgate", "comgate"], [5]])("rejects malformed discovery %j", async body => {
  fetchReply(body); await expect(api.providerChoices("a")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([null, "public-widget-key"])("saves metadata only, preserving false flags and explicit public key %s", async publishableKey => {
  const fetch = fetchReply({ ...row, publishableKey }); expect(await api.saveProviderConnection("a/b", "comgate", { ...input, merchantId: " merchant-a ", publishableKey: publishableKey ? ` ${publishableKey} ` : " " })).toEqual({ ...row, publishableKey });
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/provider-connections/comgate"); expect(fetch.mock.calls[0][1].method).toBe("PUT"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ ...input, publishableKey }); expect(fetch).toHaveBeenCalledOnce();
});
it.each([{ ...row, provider: "packeta" }, { ...row, merchantId: "other" }, { ...row, isActive: true }, { ...row, environment: "Live" }, { ...row, publishableKey: "different" }, undefined])("treats mismatched metadata acknowledgments as unconfirmed: %j", async body => {
  if (body === undefined) fetchReply(null, 204); else fetchReply(body); await expect(api.saveProviderConnection("a", "comgate", input)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("sends the secret only in its own body, with exact whitespace and encoded identifiers", async () => {
  const fetch = fetchReply(null, 204); await api.setProviderSecret("a/b", "provider/one", ` ${credential} `);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/provider-connections/provider%2Fone/secret"); expect(fetch.mock.calls[0][1].method).toBe("PUT"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ secret: ` ${credential} ` }); expect(fetch).toHaveBeenCalledOnce();
});
it.each([400, 403, 404, 409, 503])("drops echoed secrets, diagnostics, traces and retry headers on credential failure %s", async status => {
  const fetch = fetchReply({ detail: credential, traceId: credential, errors: { secret: [credential], Unknown: [credential] } }, status);
  const error = await api.setProviderSecret("a", "comgate", credential).catch(error => error);
  expect(error).toMatchObject({ kind: "http", status, traceId: null, retryAfter: null, problem: { errors: { secret: [""] } } }); expect(JSON.stringify(error)).not.toContain(credential); expect(String(error)).not.toContain(credential); expect(fetch).toHaveBeenCalledOnce();
});
it.each([null, {}, { secret: credential }])("requires an empty credential acknowledgment without keeping an unexpected body", async result => {
  fetchReply(result); const error = await api.setProviderSecret("a", "comgate", credential).catch(error => error); expect(error).toMatchObject({ kind: "invalid-response", problem: null }); expect(JSON.stringify(error)).not.toContain(credential);
});
it("disconnects through a single bodyless DELETE and checks the empty acknowledgment", async () => {
  const fetch = fetchReply(null, 204); await api.disconnectProvider("a/b", "legacy/one"); expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/provider-connections/legacy%2Fone"); expect(fetch.mock.calls[0][1]).toMatchObject({ method: "DELETE" }); expect(fetch.mock.calls[0][1].body).toBeUndefined();
  fetchReply({}); await expect(api.disconnectProvider("a", "comgate")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([{ ...input, merchantId: " " }, { ...input, merchantId: "x".repeat(101) }, { ...input, publishableKey: "x".repeat(201) }, { ...input, environment: "" }])("rejects invalid metadata before sending: %j", async value => {
  const fetch = fetchReply(row); await expect(api.saveProviderConnection("a", "comgate", value as ConnectionInput)).rejects.toBeInstanceOf(RangeError); expect(fetch).not.toHaveBeenCalled();
});
it("rejects blank credentials before sending and sanitizes arbitrary thrown messages", async () => {
  const fetch = fetchReply(null, 204); await expect(api.setProviderSecret("a", "comgate", " ")).rejects.toBeInstanceOf(RangeError); expect(fetch).not.toHaveBeenCalled();
  expect(JSON.stringify(connectionFailure(new Error(credential)))).not.toContain(credential);
  expect(connectionFailure(new HttpError("http", 400, { errors: { MerchantId: [credential] }, detail: credential }, credential))).toMatchObject({ traceId: null, problem: { errors: { merchantId: [""] } } });
});
