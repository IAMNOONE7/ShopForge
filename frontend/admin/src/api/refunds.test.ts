import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { orderDetail, returnedOrder } from "../test/orderFixtures";
import { returnRecord } from "../test/returnFixtures";
function response(value: unknown, status = 200, headers = {}) { return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json", ...headers } }); }
afterEach(() => vi.unstubAllGlobals());
describe("keyed financial contracts", () => {
  it.each(["Failed", "Pending", "Refunded", "ByStore", "ClosedByHand", "Future"])("treats 200 receipt with refund %s as received, preserving its distinct money outcome", async (refund) => {
    const row = { ...returnRecord, status: "Received", refundedAmount: 50, refund };
    const fetchMock = vi.fn().mockResolvedValue(response(row)); vi.stubGlobal("fetch", fetchMock);
    expect(await api.changeReturnMoney("s/a", row.id, "receive", "sf-receipt")).toEqual(row);
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(`/api/admin/stores/s%2Fa/returns/${row.id}/receive`, expect.objectContaining({ method: "POST", headers: { "Idempotency-Key": "sf-receipt" }, body: undefined }));
  });
  it("sends exact keyed retry and note bodies to encoded routes and accepts replay/concurrent manual settlement", async () => {
    const row = { ...returnRecord, id: "return/a &", status: "Received", refundedAmount: 50, refund: "ClosedByHand", refundNote: "Paid\nexternally" };
    const fetchMock = vi.fn().mockImplementation(async () => response(row, 200, { "Idempotent-Replay": "true" })); vi.stubGlobal("fetch", fetchMock);
    await api.changeReturnMoney("s/a", row.id, "retry", "sf-retry"); await api.changeReturnMoney("s/a", row.id, "close", "sf-close", row.refundNote);
    expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/admin/stores/s%2Fa/returns/return%2Fa%20%26/refund", expect.objectContaining({ body: undefined, headers: { "Idempotency-Key": "sf-retry" } }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, "/api/admin/stores/s%2Fa/returns/return%2Fa%20%26/refund/close", expect.objectContaining({ body: JSON.stringify({ note: row.refundNote }), headers: { "Content-Type": "application/json", "Idempotency-Key": "sf-close" } }));
  });
  it.each([
    ["receive", "Accepted", null], ["receive", "Received", null], ["retry", "Received", "Failed"], ["retry", "Received", "Pending"], ["close", "Received", "Refunded"],
  ] as const)("rejects an unconfirmed %s response with %s/%s", async (action, status, refund) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response({ ...returnRecord, status, refund })));
    await expect(api.changeReturnMoney("s", returnRecord.id, action, "sf-key", "Checked")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("requires a checked related return response and bounded keys before sending", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response({ ...returnRecord, id: "other", status: "Received", refund: "Refunded" })); vi.stubGlobal("fetch", fetchMock);
    await expect(api.changeReturnMoney("s", returnRecord.id, "receive", "sf-key")).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(api.changeReturnMoney("s", returnRecord.id, "receive", "")).rejects.toThrow(RangeError);
    await expect(api.refundOrder("s", orderDetail.number, "x".repeat(129))).rejects.toThrow(RangeError); expect(fetchMock).toHaveBeenCalledTimes(1);
  });
  it("preserves a partial-order response with its new credit and own currency instead of requiring order Refunded", async () => {
    const order = { ...orderDetail, status: "Paid", returns: [{ ...returnedOrder, refund: "Pending" }] };
    const fetchMock = vi.fn().mockResolvedValue(response(order)); vi.stubGlobal("fetch", fetchMock);
    expect(await api.refundOrder("s/a", order.number, "sf-order")).toEqual(order);
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(`/api/admin/stores/s%2Fa/orders/${order.number}/refund`, expect.objectContaining({ method: "POST", body: undefined, headers: { "Idempotency-Key": "sf-order" } }));
  });
  it.each([409, 422, 502])("preserves financial failure %s without retrying", async (status) => {
    const fetchMock = vi.fn().mockResolvedValue(response({ title: "Financial action refused", traceId: "trace" }, status)); vi.stubGlobal("fetch", fetchMock);
    await expect(api.changeReturnMoney("s", returnRecord.id, "retry", "sf-key")).rejects.toMatchObject({ status, traceId: "trace" }); expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
