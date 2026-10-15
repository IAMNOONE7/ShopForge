import type { AdminList, AdminOrder, AdminOrderDetail } from "../api";

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
  paymentAttempts: [{ reference: null, status: "Started" }],
  lines: [{ productName: "Chair", unitPrice: 45, quantity: 2, vatRate: 21, lineTotal: 90 }],
};
