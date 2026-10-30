import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import * as http from "./http";
import { orderDetail, returnedOrder, unappliedPayment } from "../test/orderFixtures";
import { safeTrackingUrl } from "../components/orderDetailFacts";

function responses(...values: unknown[]) {
  const fetch = vi.fn(); values.forEach((value) => fetch.mockResolvedValueOnce(Response.json(value))); vi.stubGlobal("fetch", fetch); return fetch;
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("order detail contracts", () => {
  it.each([undefined, null, 123.456])("preserves the order's recorded threshold on reads (%s)", async freeShippingThreshold => {
    const value = { ...orderDetail, shippingPrice: 0, freeShippingThreshold };
    responses(value); expect((await api.order("store-a", value.number)).freeShippingThreshold).toBe(freeShippingThreshold);
  });

  it.each([{ freeShippingThreshold: 0 }, { freeShippingThreshold: -1 }, { freeShippingThreshold: "200" },
    { freeShippingThreshold: 200, shippingPrice: 4.9 }])("rejects invalid or inconsistent threshold facts %j", async facts => {
    responses({ ...orderDetail, ...facts }); await expect(api.order("store-a", orderDetail.number)).rejects.toMatchObject({ kind: "invalid-response" });
  });

  it.each(["payment", "cancel", "shipment", "refund"] as const)("retains the recorded threshold after %s", async action => {
    const status = { payment: "Paid", cancel: "Cancelled", shipment: "Shipped", refund: "Refunded" }[action];
    const shipment = { carrier: "manual", trackingNumber: "TRACK", trackingUrl: null, shippedAt: orderDetail.placedAt };
    responses({ ...orderDetail, shippingPrice: 0, freeShippingThreshold: 123.456, status, shipment: action === "shipment" ? shipment : null });
    const result = action === "payment" ? await api.confirmOrderPayment("store-a", orderDetail.number)
      : action === "cancel" ? await api.cancelOrder("store-a", orderDetail.number)
        : action === "shipment" ? await api.createShipment("store-a", orderDetail.number, "TRACK")
          : await api.refundOrder("store-a", orderDetail.number, "delivery-review");
    expect(result.freeShippingThreshold).toBe(123.456);
  });

  it("keeps recorded payment facts and every attempt/related record in its returned currency", async () => {
    const value = { ...orderDetail, paymentReference: "recorded-reference", paidAt: "2026-10-15T12:00:00Z",
      unappliedPayments: [unappliedPayment], returns: [returnedOrder], paymentAttempts: [orderDetail.paymentAttempts[0], orderDetail.paymentAttempts[0]] };
    const fetch = responses(value); const signal = new AbortController().signal;
    expect(await api.order("store/a", value.number, signal)).toEqual(value);
    expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/store%2Fa/orders/${value.number}`);
    expect(fetch.mock.calls[0][1].signal).toBe(signal);
  });

  it.each([
    {}, { ...orderDetail, number: "OTHER" }, { ...orderDetail, paymentAttempts: undefined }, { ...orderDetail, paymentAttempts: null },
    { ...orderDetail, paymentAttempts: [{ ...orderDetail.paymentAttempts[0], environment: true }] },
    { ...orderDetail, paymentAttempts: [{ ...orderDetail.paymentAttempts[0], currency: "euro" }] },
    { ...orderDetail, paidAt: "not a date" }, { ...orderDetail, currency: "" },
    { ...orderDetail, unappliedPayments: [{ ...unappliedPayment, orderNumber: "OTHER" }] },
    { ...orderDetail, unappliedPayments: [unappliedPayment, unappliedPayment] },
    { ...orderDetail, returns: [{ ...returnedOrder, refundedAmount: "90" }] }, { ...orderDetail, returns: [returnedOrder, returnedOrder] },
    { ...orderDetail, shipment: { carrier: "manual" } }, { ...orderDetail, billingAddress: null },
    { ...orderDetail, documents: [{ number: "INV-1", kind: "Invoice", issuedAt: null }] },
  ])("refuses incomplete or mismatched detail data: %j", async (value) => {
    responses(value); await expect(api.order("store-a", orderDetail.number)).rejects.toMatchObject({ kind: "invalid-response" });
  });

  it("retains unknown states without inventing paid/live outcomes or dropping duplicate-name lines", async () => {
    const value = { ...orderDetail, status: "FutureOrder", returns: [{ ...returnedOrder, refund: "FutureRefund" }],
      paymentAttempts: [{ ...orderDetail.paymentAttempts[0], status: "FutureAttempt", environment: "future" }], lines: [orderDetail.lines[0], orderDetail.lines[0]] };
    responses(value); expect(await api.order("store-a", value.number)).toEqual(value);
  });

  it("uses existing encoded action paths and exact empty/tracking bodies", async () => {
    const number = "2026/1 #"; const value = { ...orderDetail, number };
    const shipment = { carrier: "manual", trackingNumber: "TRACK", trackingUrl: null, shippedAt: orderDetail.placedAt };
    const fetch = responses({ ...value, status: "Paid" }, { ...value, status: "Cancelled" }, { ...value, status: "Shipped", shipment }, { ...value, status: "Refunded" });
    await api.confirmOrderPayment("store/a", number); await api.cancelOrder("store/a", number);
    await api.createShipment("store/a", number, "TRACK"); await api.refundOrder("store/a", number, "sf-order-key");
    expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body])).toEqual([
      ["/api/admin/stores/store%2Fa/orders/2026%2F1%20%23/payment", "POST", undefined],
      ["/api/admin/stores/store%2Fa/orders/2026%2F1%20%23/cancel", "POST", undefined],
      ["/api/admin/stores/store%2Fa/orders/2026%2F1%20%23/shipment", "POST", JSON.stringify({ trackingNumber: "TRACK" })],
      ["/api/admin/stores/store%2Fa/orders/2026%2F1%20%23/refund", "POST", undefined],
    ]);
  });

  it("does not acknowledge empty, unapplied or mismatched mutation responses", async () => {
    responses(orderDetail, { ...orderDetail, status: "Cancelled", number: "OTHER" }, { ...orderDetail, status: "Shipped" });
    await expect(api.confirmOrderPayment("store-a", orderDetail.number)).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(api.cancelOrder("store-a", orderDetail.number)).rejects.toMatchObject({ kind: "invalid-response" });
    await expect(api.createShipment("store-a", orderDetail.number, "TRACK")).rejects.toMatchObject({ kind: "invalid-response" });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    await expect(api.confirmOrderPayment("store-a", orderDetail.number)).rejects.toMatchObject({ kind: "invalid-response" });
  });

  it("checks PDF content before saving and encodes document identity", async () => {
    const save = vi.spyOn(http, "saveDownload").mockImplementation(() => undefined);
    const fetch = vi.fn().mockResolvedValueOnce(new Response("<html>Sign in</html>", { headers: { "Content-Type": "text/html" } }))
      .mockResolvedValueOnce(new Response("%PDF-document", { headers: { "Content-Type": "application/pdf", "Content-Disposition": 'attachment; filename="../../invoice.pdf"' } }));
    vi.stubGlobal("fetch", fetch);
    await expect(api.downloadDocument("store/a", "2026/1", "INV/1")).rejects.toMatchObject({ kind: "invalid-response" });
    expect(save).not.toHaveBeenCalled(); await api.downloadDocument("store/a", "2026/1", "INV/1");
    expect(fetch.mock.calls[1][0]).toBe("/api/admin/stores/store%2Fa/orders/2026%2F1/documents/INV%2F1");
    expect(save).toHaveBeenCalledWith(expect.objectContaining({ filename: "invoice.pdf" }));
  });

  it("offers only absolute HTTP(S) tracking links without embedded credentials", () => {
    expect(safeTrackingUrl("https://carrier.example/track?id=123")).toBe("https://carrier.example/track?id=123");
    for (const value of [null, "", "/track/123", "//carrier.example", "javascript:alert(1)", "data:text/html,hello", "https://user:secret@carrier.example"]) expect(safeTrackingUrl(value)).toBeNull();
  });
});
