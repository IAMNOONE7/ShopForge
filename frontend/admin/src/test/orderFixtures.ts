import type { AdminList, AdminOrder, AdminOrderDetail, AdminOrderReturn, AdminUnappliedPayment } from "../api";

export const order: AdminOrder = {
  number: "ORD-1001", placedAt: "2026-09-25T12:00:00Z", status: "AwaitingPayment",
  email: "customer@example.com", hasAccount: false, grandTotal: 149, items: 2,
};
export const orderPage: AdminList<AdminOrder> = { items: [order], totalCount: 1, page: 1, pageSize: 25, hasMore: false };
const address = { fullName: "Alex Buyer", line1: "Wilsonova 8", line2: null, city: "Praha", postalCode: "110 00", country: "CZ" };
export const orderDetail: AdminOrderDetail = {
  number: order.number, placedAt: order.placedAt, status: order.status, email: order.email,
  carrier: "packeta", phone: "+420 777 123 456", currency: "EUR", paymentMethod: "Bank transfer", shippingMethod: "Z-BOX",
  shippingPrice: 59, itemsTotal: 90, vatTotal: 25.86, grandTotal: 149, billingAddress: address, shippingAddress: address,
  discount: null, pickupPoint: "Z-BOX Hlavní nádraží, Wilsonova 8, Praha", shipment: null, documents: [],
  paymentReference: null, paidAt: null, unappliedPayments: [], returns: [],
  paymentAttempts: [{ provider: "manual", reference: null, status: "Started", environment: null, amount: 149, currency: "EUR", startedAt: order.placedAt, changedAt: order.placedAt }],
  lines: [{ productName: "Chair", unitPrice: 45, quantity: 2, vatRate: 21, lineTotal: 90 }],
};

export const unappliedPayment: AdminUnappliedPayment = {
  id: "money-a", orderNumber: order.number, provider: "comgate", reference: "extra-payment", amount: 175, currency: "USD",
  reason: "OrderAlreadyPaid", status: "Refunding", receivedAt: order.placedAt, changedAt: order.placedAt, note: null, canRefund: false,
};
export const returnedOrder: AdminOrderReturn = {
  id: "return-a", number: "RET-1", status: "Received", refundedAmount: 90, refund: "Failed", refundProblem: "Provider refused the refund.",
};

export const orderParcels = [
  { number: 1, grossWeightGrams: 7100, bandUpToGrams: 10000, price: 12, alone: false,
    contents: [{ variantId: "11111111-1111-1111-1111-111111111111", sku: "CHAIR-OAK", quantity: 8 }] },
  { number: 2, grossWeightGrams: 2000, bandUpToGrams: 3000, price: 8, alone: true,
    contents: [{ variantId: "22222222-2222-2222-2222-222222222222", sku: "TABLE-OAK", quantity: 1 }] },
];
