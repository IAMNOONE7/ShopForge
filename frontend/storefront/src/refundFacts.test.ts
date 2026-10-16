import { afterEach, describe, expect, it, vi } from "vitest";
import { getAccountOrder, getReturns, requestReturn } from "./account";
import { getOrder } from "./cart";

const paidAt = "2026-10-16T09:00:00Z", settledAt = "2026-10-16T10:00:00Z";
const refund = { returnNumber: "RET-1", credited: 14.5, state: "Settled", settledAt };
const order = { number: "2026-1", placedAt: "2026-10-15T09:00:00Z", status: "Refunded", email: "buyer@example.test",
  currency: "EUR", paymentMethod: "Comgate", shippingMethod: "Delivery", shippingPrice: 0, itemsTotal: 55, vatTotal: 5,
  grandTotal: 55, discount: null, pickupPoint: null, shipment: null, documents: [], lines: [], paidAt, refunds: [refund] };
const returned = { number: "RET-1", status: "Received", requestedAt: "2026-10-15T12:00:00Z", refundedAmount: 14.5,
  refundState: "Settled", refundSettledAt: settledAt,
  lines: [{ storeProductId: "listing", variantId: "variant-l", productName: "Shirt (L)", quantity: 1 }] };
const returns = { closesAt: null, returnable: [], returns: [returned] };

function serve(body: unknown) {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } }));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}
afterEach(() => vi.unstubAllGlobals());

describe("public payment and refund reads", () => {
  it("reads whole-order refunds through both scoped order routes, without exposing private refund fields", async () => {
    const fetch = serve({ ...order, refunds: [{ ...refund, refundProblem: "private-provider-reason", refundNote: "private-merchant-note" }] });
    const controller = new AbortController();
    expect(await getOrder("2026/1", "private token", controller.signal)).toEqual(order);
    expect(fetch.mock.calls[0][0]).toBe("/api/storefront/orders/2026%2F1?token=private%20token");
    expect(fetch.mock.calls[0][1].signal).toBe(controller.signal);
    serve(order);
    expect(await getAccountOrder("2026/1", controller.signal)).toEqual(order);
    expect(vi.mocked(globalThis.fetch).mock.calls[0][0]).toBe("/api/storefront/account/orders/2026%2F1");
  });

  it("keeps missing legacy facts unconfirmed and never substitutes the placement date", async () => {
    const legacy = { ...order };
    Reflect.deleteProperty(legacy, "paidAt"); Reflect.deleteProperty(legacy, "refunds");
    serve(legacy);
    expect(await getAccountOrder("2026-1")).toEqual({ ...order, paidAt: null, refunds: [] });
  });

  it.each(["InProgress", "ByStore", "FutureState"])("ignores a settlement timestamp for %s", async state => {
    serve({ ...order, paidAt: "invalid", refunds: [{ ...refund, state, credited: 0 }] });
    expect(await getAccountOrder("2026-1")).toMatchObject({ paidAt: null, refunds: [{ ...refund, state, credited: 0, settledAt: null }] });
  });

  it("retains a settled fact without inventing an invalid settlement time", async () => {
    serve({ ...order, refunds: [{ ...refund, settledAt: "not-a-time" }] });
    expect((await getAccountOrder("2026-1")).refunds).toEqual([{ ...refund, settledAt: null }]);
  });

  it.each([null, {}, [{ ...refund, credited: -1 }], [{ ...refund, credited: "14.50" }],
    [{ ...refund, state: null }], [{ ...refund, returnNumber: "" }], [refund, refund]])("refuses malformed or ambiguous refund money: %j", async refunds => {
    serve({ ...order, refunds });
    await expect(getAccountOrder("2026-1")).rejects.toMatchObject({ kind: "invalid-response" });
  });

  it("retains return line identities and uses the same checked facts after a keyed request", async () => {
    let fetch = serve(returns);
    expect(await getReturns("2026/1")).toEqual(returns);
    expect(fetch.mock.calls[0][0]).toBe("/api/storefront/account/orders/2026%2F1/returns");
    fetch = serve(returns);
    const body = { lines: [{ storeProductId: "listing", variantId: "variant-l", quantity: 1 }], reason: null };
    expect(await requestReturn("2026/1", body, "original-request-key")).toEqual(returns);
    expect(fetch.mock.calls[0][1]).toMatchObject({ method: "POST", headers: { "Idempotency-Key": "original-request-key" }, body: JSON.stringify(body) });
  });

  it.each([null, "InProgress", "ByStore", "FutureState"])("never exposes an unconfirmed return settlement time for %s", async refundState => {
    serve({ ...returns, returns: [{ ...returned, refundState }] });
    expect((await getReturns("2026-1")).returns[0]).toMatchObject({ refundState, refundSettledAt: null });
  });

  it.each([null, {}, { returns: [null] }, { ...returns, returns: [{ ...returned, refundedAmount: -1 }] }])("refuses invalid return money on reads and uncertain write answers: %j", async body => {
    serve(body);
    await expect(getReturns("2026-1")).rejects.toMatchObject({ kind: "invalid-response" });
    serve(body);
    await expect(requestReturn("2026-1", { lines: [], reason: null }, "same-key")).rejects.toMatchObject({ kind: "invalid-response" });
  });
});
