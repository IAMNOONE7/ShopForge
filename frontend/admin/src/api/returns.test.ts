import { afterEach, describe, expect, it, vi } from "vitest";
import { returnRecord } from "../test/returnFixtures";
import { decideReturn, getReturns } from "./returns";
function response(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } }); }
afterEach(() => vi.unstubAllGlobals());
describe("admin returns contracts", () => {
  it("requests only supported filters and keeps the existing bounded array contract", async () => {
    const item = { ...returnRecord, status: "Received", refund: "Failed", refundedAmount: 12.5, refundProblem: "Provider refused" };
    const fetchMock = vi.fn().mockResolvedValue(response([item])); vi.stubGlobal("fetch", fetchMock); const signal = new AbortController().signal;
    await expect(getReturns("store/ &", signal, { status: "Received", refund: "Failed" })).resolves.toEqual([item]);
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith("/api/admin/stores/store%2F%20%26/returns?status=Received&refund=Failed", expect.objectContaining({ signal, method: "GET" }));
  });
  it("omits All filters and preserves duplicate-name lines, unknown states and nullable facts", async () => {
    const item = { ...returnRecord, status: "Future", refund: "FutureMoney", reason: null, refundNote: "Checked manually" };
    const fetchMock = vi.fn().mockResolvedValue(response([item])); vi.stubGlobal("fetch", fetchMock);
    expect((await getReturns("a"))[0]).toEqual(item); expect(fetchMock.mock.calls[0][0]).toBe("/api/admin/stores/a/returns");
  });
  it.each([
    [undefined], [{ ...returnRecord, refund: undefined }], [{ ...returnRecord, refundNote: undefined }],
    [{ ...returnRecord, refundProblem: undefined }], [{ ...returnRecord, refundedAmount: "12" }],
    [{ ...returnRecord, requestedAt: "bad" }], [{ ...returnRecord, lines: [{ productName: "x", quantity: 0 }] }],
    [{ ...returnRecord, lines: [{ productName: "x", quantity: 1.5 }] }], [returnRecord, returnRecord],
    [returnRecord, { ...returnRecord, id: "different" }],
  ])("rejects malformed or duplicated records %j", async (...items) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(items)));
    await expect(getReturns("s")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("accepts exactly 200 records and rejects a made-up page envelope or more than the server bound", async () => {
    const rows = Array.from({ length: 200 }, (_, index) => ({ ...returnRecord, id: `id-${index}`, number: `RET-${index}` }));
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(response(rows)).mockResolvedValueOnce(response({ items: rows, totalCount: 200 })).mockResolvedValueOnce(response([...rows, { ...returnRecord, id: "over", number: "over" }])));
    expect(await getReturns("s")).toHaveLength(200);
    await expect(getReturns("s")).rejects.toMatchObject({ kind: "invalid-response" }); await expect(getReturns("s")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("rejects records inconsistent with the requested server filters", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response([returnRecord])));
    await expect(getReturns("s", undefined, { status: "Received", refund: "Failed" })).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each(["accept", "refuse"] as const)("posts %s without inventing a note, body or idempotency contract", async (decision) => {
    const item = { ...returnRecord, id: "return/ &", status: decision === "accept" ? "Accepted" : "Refused" };
    const fetchMock = vi.fn().mockResolvedValue(response(item)); vi.stubGlobal("fetch", fetchMock);
    expect(await decideReturn("s/1", item.id, decision)).toEqual(item);
    expect(fetchMock).toHaveBeenCalledExactlyOnceWith(`/api/admin/stores/s%2F1/returns/return%2F%20%26/${decision}`, expect.objectContaining({ method: "POST", headers: undefined, body: undefined }));
  });
  it.each([{ id: "other", status: "Accepted" }, { status: "Requested" }, { status: "Received" }, { status: "Accepted", refund: undefined }])("rejects unconfirmed or unrelated decision responses %j", async (patch) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response({ ...returnRecord, ...patch })));
    await expect(decideReturn("s", returnRecord.id, "accept")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("preserves server conflicts and never retries a write", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response({ title: "This return has already been dealt with", traceId: "trace" }, 409)); vi.stubGlobal("fetch", fetchMock);
    await expect(decideReturn("s", returnRecord.id, "refuse")).rejects.toMatchObject({ status: 409, traceId: "trace" }); expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
