import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { orderDetail, orderParcels } from "../test/orderFixtures";

function response(parcels: unknown, rest: object = {}) {
  const value = { ...orderDetail, ...rest, parcels };
  const fetch = vi.fn().mockResolvedValue(Response.json(value));
  vi.stubGlobal("fetch", fetch);
  return fetch;
}
afterEach(() => vi.unstubAllGlobals());

describe("recorded order parcels", () => {
  it("retains recorded numbers, identities, contents, weights and prices without reading today's catalog or rate card", async () => {
    const fetch = response(orderParcels);
    expect((await api.order("store/a", orderDetail.number)).parcels).toEqual(orderParcels);
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/store%2Fa/orders/${orderDetail.number}`);
  });

  it("keeps a valid parcel group independent of a discounted delivery amount and money totals", async () => {
    response(orderParcels, { shippingPrice: 0, grandTotal: 90, discount: { code: "FREE", name: "Delivery", amount: 20 } });
    const order = await api.order("store-a", orderDetail.number);
    expect(order.parcels).toEqual(orderParcels);
    expect(order.shippingPrice).toBe(0);
    expect(order.grandTotal).toBe(90);
    expect(order.discount?.amount).toBe(20);
  });

  it("keeps empty current facts distinct from an absent legacy parcel field", async () => {
    response([]);
    expect((await api.order("store-a", orderDetail.number)).parcels).toEqual([]);
    response(undefined);
    expect(await api.order("store-a", orderDetail.number)).not.toHaveProperty("parcels");
  });

  it("accepts the same variant in separate parcels, repeated SKU text on different forms, zero prices and non-consecutive saved numbers", async () => {
    const parcels = [{ ...orderParcels[0], price: 0 }, { ...orderParcels[1], number: 3,
      contents: [orderParcels[0].contents[0], { ...orderParcels[1].contents[0], sku: orderParcels[0].contents[0].sku }] }];
    response(parcels);
    expect((await api.order("store-a", orderDetail.number)).parcels).toEqual(parcels);
  });

  const first = orderParcels[0];
  const item = first.contents[0];
  it.each([
    null, {}, "none", [null], [{ ...first, number: 0 }], [{ ...first, number: 1.5 }],
    [{ ...first, number: "1" }], [first, first], [orderParcels[1], first],
    [{ ...first, grossWeightGrams: 0 }], [{ ...first, grossWeightGrams: 1.5 }],
    [{ ...first, grossWeightGrams: 2147483648, bandUpToGrams: 2147483648 }],
    [{ ...first, bandUpToGrams: 1 }], [{ ...first, bandUpToGrams: null }],
    [{ ...first, price: -1 }], [{ ...first, price: "12" }], [{ ...first, price: Infinity }],
    [{ ...first, alone: "false" }], [{ ...first, alone: undefined }],
    [{ ...first, contents: [] }], [{ ...first, contents: null }], [{ ...first, contents: [null] }],
    [{ ...first, contents: [{ ...item, variantId: "other" }] }],
    [{ ...first, contents: [{ ...item, variantId: "00000000-0000-0000-0000-000000000000" }] }],
    [{ ...first, contents: [{ ...item, sku: " " }] }],
    [{ ...first, contents: [{ ...item, quantity: 0 }] }], [{ ...first, contents: [{ ...item, quantity: 2.5 }] }],
    [{ ...first, contents: [item, item] }],
    [first, { ...orderParcels[1], price: undefined }],
  ])("marks the whole malformed parcel group unavailable while preserving checked order/payment facts: %j", async parcels => {
    response(parcels);
    const order = await api.order("store-a", orderDetail.number);
    expect(order.parcels).toBeNull();
    expect(order.grandTotal).toBe(149);
    expect(order.paymentAttempts).toEqual(orderDetail.paymentAttempts);
    expect(order.status).toBe("AwaitingPayment");
  });

  it.each(["payment", "cancel", "shipment", "refund"] as const)("checks and retains parcels in the existing %s answer", async action => {
    const status = { payment: "Paid", cancel: "Cancelled", shipment: "Shipped", refund: "Refunded" }[action];
    const shipment = { carrier: "manual", trackingNumber: "TRACK", trackingUrl: null, shippedAt: orderDetail.placedAt };
    const fetch = response(orderParcels, { status, shipment: action === "shipment" ? shipment : null });
    const result = action === "payment" ? await api.confirmOrderPayment("store-a", orderDetail.number)
      : action === "cancel" ? await api.cancelOrder("store-a", orderDetail.number)
        : action === "shipment" ? await api.createShipment("store-a", orderDetail.number, "TRACK")
          : await api.refundOrder("store-a", orderDetail.number, "sf-parcel-refund");
    expect(result.parcels).toEqual(orderParcels);
    expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/store-a/orders/${orderDetail.number}/${action}`);
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("does not turn an acknowledged payment into an uncertain write because its additive parcel group is malformed", async () => {
    response([{ ...first, price: null }], { status: "Paid" });
    const paid = await api.confirmOrderPayment("store-a", orderDetail.number);
    expect(paid.status).toBe("Paid"); expect(paid.parcels).toBeNull();
  });

  it("retains literal SKU text and strips undeclared parcel/content properties", async () => {
    response([{ ...first, trackingUrl: "https://carrier.invalid", contents: [{ ...item, sku: "<b>Saved SKU</b>", productName: "Current name" }] }]);
    const result = await api.order("store-a", orderDetail.number);
    expect(result.parcels?.[0]).not.toHaveProperty("trackingUrl");
    expect(result.parcels?.[0].contents[0]).toEqual({ ...item, sku: "<b>Saved SKU</b>" });
  });
});
