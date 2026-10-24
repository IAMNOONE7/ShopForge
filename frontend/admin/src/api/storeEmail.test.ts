import { afterEach, expect, it, vi } from "vitest";
import { storeEmailApi } from "./storeEmail";

const id = "11111111-1111-1111-1111-111111111111";
const dns = { purpose: "receiving", type: "MX", name: null, value: "mxa.example.test", priority: "0", valid: "unknown" };
const domain = { domain: "mail.shop.cz", state: "awaitingDns", senderAddress: "orders@shop.cz", replyTo: null, region: "eu", operationId: id, verifiedAt: null, records: [dns], recommended: [{ ...dns, purpose: "dmarc", type: "TXT", name: "_dmarc.shop.cz", value: "v=DMARC1; p=none", priority: null, valid: null }] };
const view = { sendsFromOwnDomain: false, platformAddress: "platform@example.test", domain };
function answer(value: unknown, status = 200) { const fetch = vi.fn().mockImplementation(async () => Response.json(value, { status })); vi.stubGlobal("fetch", fetch); return fetch; }
afterEach(() => vi.unstubAllGlobals());
it("reads exact nullable/string DNS fields and discards undeclared secrets", async () => {
  const fetch = answer({ ...view, secret: "not-client-data", domain: { ...domain, keySecretName: "not-client-data", records: [{ ...dns, secret: "not-client-data" }] } }), signal = new AbortController().signal;
  expect(await storeEmailApi.get("store/a", signal)).toEqual(view);
  expect(fetch).toHaveBeenCalledExactlyOnceWith("/api/admin/stores/store%2Fa/email", { method: "GET", signal, body: undefined, headers: undefined });
});
it("keeps unknown states, regions, purposes, empty strings and provider values inert", async () => {
  const item = { ...view, platformAddress: "", domain: { ...domain, state: "future", region: "future", operationId: null, records: [{ ...dns, purpose: "future", name: "", type: "", value: "", priority: "0", valid: "false" }] } };
  answer(item); expect(await storeEmailApi.get("store")).toEqual(item);
});
it("distinguishes a null domain from a failed or malformed read", async () => {
  answer({ ...view, domain: null }); expect(await storeEmailApi.get("store")).toEqual({ ...view, domain: null });
});
it.each([null, [], {}, { ...view, sendsFromOwnDomain: null }, { ...view, platformAddress: null }, { ...view, sendsFromOwnDomain: true, domain: null }])("rejects malformed email truth %j", async value => {
  answer(value); await expect(storeEmailApi.get("store")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each(["domain", "state", "senderAddress", "replyTo", "region", "operationId", "verifiedAt", "records", "recommended"])("requires the declared domain field %s", async field => {
  const value: Record<string, unknown> = { ...domain }; delete value[field]; answer({ ...view, domain: value });
  await expect(storeEmailApi.get("store")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([{ operationId: "wrong" }, { verifiedAt: "2026-10-24" }, { records: {} }, { recommended: null }, { state: " " }])("rejects malformed domain facts %j", async change => {
  answer({ ...view, domain: { ...domain, ...change } }); await expect(storeEmailApi.get("store")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([{ purpose: "" }, { type: null }, { name: 0 }, { value: null }, { priority: 0 }, { valid: false }, { valid: undefined }])("does not coerce malformed DNS facts %j", async change => {
  answer({ ...view, domain: { ...domain, records: [{ ...dns, ...change }] } }); await expect(storeEmailApi.get("store")).rejects.toMatchObject({ kind: "invalid-response" });
});
it("starts connection once with the exact draft and removal once without a body", async () => {
  const fetch = answer({ operationId: id }, 202), signal = new AbortController().signal, body = { domain: domain.domain, senderAddress: domain.senderAddress };
  expect(await storeEmailApi.connect("store/a", body, signal)).toEqual({ operationId: id });
  expect(await storeEmailApi.remove("store/a", signal)).toEqual({ operationId: id });
  expect(fetch.mock.calls.map(call => [call[0], call[1].method, call[1].body, call[1].headers, call[1].signal])).toEqual([
    ["/api/admin/stores/store%2Fa/email/domain", "POST", JSON.stringify(body), { "Content-Type": "application/json" }, signal],
    ["/api/admin/stores/store%2Fa/email/domain", "DELETE", undefined, undefined, signal],
  ]);
});
it.each([null, {}, { operationId: null }, { operationId: "wrong" }])("keeps malformed starts unconfirmed (%j) and never repeats them", async value => {
  const fetch = answer(value, 202); await expect(storeEmailApi.remove("store")).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
});
it.each([400, 401, 403, 404, 409, 429, 503])("preserves a %i refusal including its busy operation link without replaying", async status => {
  const fetch = answer({ title: "Not now", detail: "Already running", operationId: id }, status);
  await expect(storeEmailApi.connect("store", { domain: domain.domain, senderAddress: domain.senderAddress })).rejects.toMatchObject({ kind: "http", status, problem: { operationId: id } }); expect(fetch).toHaveBeenCalledOnce();
});
it("rejects an aborted reply even when transport resolves", async () => {
  const controller = new AbortController(); controller.abort(); answer({ operationId: id }, 202);
  await expect(storeEmailApi.remove("store", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
