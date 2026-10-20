import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api";

const storeId = "aaaaaaaa-1111-2222-3333-444444444444";
const id = "11111111-1111-1111-1111-111111111111";
const message = { id, type: "email.requested", attempts: 6, createdAt: "2026-10-20T07:00:00Z", error: "Delivery failed" };
const address = { id, email: "shopper@example.test", reason: "Bounced", detail: null, suppressedAt: "2026-10-20T07:00:00Z" };
function answer(value: unknown, status = 200) {
  const fetch = vi.fn().mockResolvedValue(status === 204 ? new Response(null, { status }) : Response.json(value, { status }));
  vi.stubGlobal("fetch", fetch); return fetch;
}
afterEach(() => vi.unstubAllGlobals());

it("reads each store's independent bounded list with its own cancellation signal", async () => {
  const fetch = answer([message]); const signal = new AbortController().signal;
  expect(await api.failedMessages(storeId, signal)).toEqual([message]);
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/${storeId}/failed-messages`);
  expect(fetch.mock.calls[0][1].signal).toBe(signal);
  fetch.mockResolvedValueOnce(Response.json([address]));
  expect(await api.suppressedAddresses(storeId, signal)).toEqual([address]);
  expect(fetch.mock.calls[1][0]).toBe(`/api/admin/stores/${storeId}/suppressed-addresses`);
  expect(fetch).toHaveBeenCalledTimes(2);
});
it.each(["messages", "addresses"] as const)("keeps an empty %s list distinct from a failed read", async kind => {
  const fetch = answer([]); const read = kind === "messages" ? api.failedMessages : api.suppressedAddresses;
  expect(await read(storeId)).toEqual([]);
  fetch.mockResolvedValueOnce(Response.json({ title: "Unavailable" }, { status: 503 }));
  await expect(read(storeId)).rejects.toMatchObject({ kind: "http", status: 503 });
});
it.each([
  null, {}, [message, { ...message, id: id.toUpperCase() }], [{ ...message, id: "wrong" }],
  [{ ...message, type: " " }], [{ ...message, attempts: -1 }], [{ ...message, attempts: 1.5 }],
  [{ ...message, createdAt: "not-a-date" }], [{ ...message, createdAt: "2026-10-20T07:00:00" }],
  [{ ...message, error: undefined }], [{ ...message, error: {} }],
].map(value => ({ value })))("rejects malformed failed-message rows: %j", async ({ value }) => {
  answer(value); await expect(api.failedMessages(storeId)).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([
  null, {}, [address, address], [{ ...address, id: "wrong" }], [{ ...address, email: "" }],
  [{ ...address, reason: null }], [{ ...address, detail: undefined }], [{ ...address, detail: {} }],
  [{ ...address, suppressedAt: "not-a-date" }],
].map(value => ({ value })))("rejects malformed suppression rows: %j", async ({ value }) => {
  answer(value); await expect(api.suppressedAddresses(storeId)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("accepts the actual 100/200 bounds and does not pretend extra pages exist", async () => {
  const records = (row: typeof message | typeof address, count: number) => Array.from({ length: count }, (_, index) => ({ ...row, id: `11111111-1111-1111-1111-${String(index).padStart(12, "0")}` }));
  const fetch = answer(records(message, 100)); expect(await api.failedMessages(storeId)).toHaveLength(100);
  fetch.mockResolvedValueOnce(Response.json(records(message, 101))); await expect(api.failedMessages(storeId)).rejects.toMatchObject({ kind: "invalid-response" });
  fetch.mockResolvedValueOnce(Response.json(records(address, 200))); expect(await api.suppressedAddresses(storeId)).toHaveLength(200);
  fetch.mockResolvedValueOnce(Response.json(records(address, 201))); await expect(api.suppressedAddresses(storeId)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("preserves unknown future suppression reasons as plain response data", async () => {
  answer([{ ...address, reason: "FutureReason" }]); expect((await api.suppressedAddresses(storeId))[0].reason).toBe("FutureReason");
});
it.each(["messages", "addresses"] as const)("discards a late %s read after cancellation", async kind => {
  const controller = new AbortController();
  answer(kind === "messages" ? [message] : [address]); controller.abort();
  await expect((kind === "messages" ? api.failedMessages : api.suppressedAddresses)(storeId, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it.each(["requeue", "allow"] as const)("sends one encoded, bodyless %s action and checks empty success", async kind => {
  const fetch = answer(undefined, 204);
  const change = kind === "requeue" ? api.requeueMessage : api.allowSuppressedAddress;
  await change("store / one", "record / one");
  expect(fetch).toHaveBeenCalledOnce();
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/store%20%2F%20one/${kind === "requeue" ? "failed-messages/record%20%2F%20one/requeue" : "suppressed-addresses/record%20%2F%20one"}`);
  expect(fetch.mock.calls[0][1].method).toBe(kind === "requeue" ? "POST" : "DELETE");
  expect(fetch.mock.calls[0][1].body).toBeUndefined();
  fetch.mockResolvedValueOnce(Response.json({ sent: true }));
  await expect(change(storeId, id)).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([404, 403, 429, 503])("does not replay refused or uncertain writes (%i)", async status => {
  const fetch = answer({ title: "Failed" }, status);
  await expect(api.requeueMessage(storeId, id)).rejects.toMatchObject({ status }); expect(fetch).toHaveBeenCalledOnce();
});
