import { afterEach, describe, expect, it, vi } from "vitest";
import { unappliedPayment } from "../test/orderFixtures";
import { maxUnappliedPage } from "../utils/unappliedQuery";
import { changeUnappliedPayment, getUnappliedPayments } from "./unappliedPayments";
const payment = { ...unappliedPayment, status: "Open", canRefund: true };
const list = { items: [payment], page: 1, pageSize: 25, totalCount: 1, hasMore: false };
function response(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } }); }
afterEach(() => vi.unstubAllGlobals());
describe("unapplied payment contracts", () => {
  it("gets a checked server page with only supported filters and abort signal", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response({ ...list, page: 2 })); vi.stubGlobal("fetch", fetchMock);
    const signal = new AbortController().signal;
    await expect(getUnappliedPayments("store/ &", { page: 2, status: "Open" }, signal)).resolves.toEqual({ ...list, page: 2 });
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith("/api/admin/stores/store%2F%20%26/unapplied-payments?page=2&pageSize=25&status=Open", expect.objectContaining({ signal, method: "GET" }));
  });
  it("omits status for all records and preserves unknown facts and currency", async () => {
    const item = { ...payment, status: "Future", reason: "FutureReason", reference: null, note: "Checked", currency: "USD" };
    const fetchMock = vi.fn().mockResolvedValue(response({ ...list, items: [item] })); vi.stubGlobal("fetch", fetchMock);
    expect((await getUnappliedPayments("a", { page: 1, status: "" })).items).toEqual([item]);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/admin/stores/a/unapplied-payments?page=1&pageSize=25");
  });
  it.each([
    { items: null }, { items: [payment, payment] }, { items: [{ ...payment, currency: undefined }] },
    { items: [{ ...payment, reference: undefined }] }, { items: [{ ...payment, note: undefined }] },
    { items: [{ ...payment, canRefund: "true" }] }, { items: [{ ...payment, receivedAt: "bad" }] },
    { items: [{ ...payment, status: "Refunding" }] }, { page: 2 }, { pageSize: 200 }, { hasMore: true }, { totalCount: 0 },
  ])("rejects malformed or mismatched collection %j", async (patch) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response({ ...list, ...patch })));
    await expect(getUnappliedPayments("a", { page: 1, status: "Open" })).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each([0, maxUnappliedPage + 1, 1.5, Infinity])("rejects unsafe page %s before sending", async (page) => {
    const fetchMock = vi.fn(); vi.stubGlobal("fetch", fetchMock);
    await expect(getUnappliedPayments("a", { page, status: "Open" })).rejects.toThrow(RangeError); expect(fetchMock).not.toHaveBeenCalled();
  });
  it("sends keyed refund and exact close note to encoded identities", async () => {
    const id = "id/ &";
    const fetchMock = vi.fn().mockResolvedValueOnce(response({ ...payment, id, status: "Refunded" }))
      .mockResolvedValueOnce(response({ ...payment, id, status: "ClosedByHand", note: "Checked\nwith provider" })); vi.stubGlobal("fetch", fetchMock);
    await changeUnappliedPayment("s/1", id, "refund", "sf-refund");
    await changeUnappliedPayment("s/1", id, "close", "sf-close", "Checked\nwith provider");
    expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/admin/stores/s%2F1/unapplied-payments/id%2F%20%26/refund", expect.objectContaining({ method: "POST", body: undefined, headers: { "Idempotency-Key": "sf-refund" } }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, "/api/admin/stores/s%2F1/unapplied-payments/id%2F%20%26/close", expect.objectContaining({ method: "POST", body: JSON.stringify({ note: "Checked\nwith provider" }), headers: { "Idempotency-Key": "sf-close", "Content-Type": "application/json" } }));
  });
  it.each(["", "x".repeat(129)])("requires a valid bounded key", async (key) => {
    const fetchMock = vi.fn(); vi.stubGlobal("fetch", fetchMock);
    await expect(changeUnappliedPayment("s", payment.id, "refund", key)).rejects.toThrow(RangeError); expect(fetchMock).not.toHaveBeenCalled();
  });
  it.each([{ id: "other", status: "Refunded" }, { status: "Open" }, { status: "Refunding" }])("rejects unrelated or unconfirmed refund answer %j", async (patch) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response({ ...payment, ...patch })));
    await expect(changeUnappliedPayment("s", payment.id, "refund", "key")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("preserves concurrent manual closure and authoritative refusal metadata", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(response({ ...payment, status: "ClosedByHand" }))
      .mockResolvedValueOnce(response({ title: "The refund was refused", traceId: "trace" }, 409)));
    expect((await changeUnappliedPayment("s", payment.id, "refund", "key1")).status).toBe("ClosedByHand");
    await expect(changeUnappliedPayment("s", payment.id, "refund", "key2")).rejects.toMatchObject({ status: 409, traceId: "trace", problem: { title: "The refund was refused" } });
  });
});
