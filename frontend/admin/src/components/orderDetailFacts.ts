import type { AdminOrderDetail } from "../api";
import { isHttpError } from "../api/http";

export type OrderAction = "payment" | "cancel" | "shipment";
export function hasOpenGatewayPayment(order: AdminOrderDetail) {
  return !Array.isArray(order.paymentAttempts) || order.paymentAttempts.some((attempt) => attempt.reference !== null && !["Paid", "Failed"].includes(attempt.status));
}
export function canChangeOrder(order: AdminOrderDetail, action: OrderAction) {
  if (action === "payment") return order.status === "AwaitingPayment" && !hasOpenGatewayPayment(order);
  if (action === "cancel") return order.status === "AwaitingPayment";
  return order.status === "Paid" && order.shipment === null;
}
export function safeTrackingUrl(value: string | null) {
  if (!value) return null;
  try { const url = new URL(value); return ["https:", "http:"].includes(url.protocol) && !url.username && !url.password ? url.href : null; }
  catch { return null; }
}
export function orderActionProblem(error: unknown, action: OrderAction) {
  if (!isHttpError(error) || error.status !== 409) return null;
  if (action === "payment" && error.problem?.title === "This order's payment is confirmed by its gateway") return "gatewayRefusal";
  if (action === "shipment" && error.problem?.title === "The shipping provider of this order is not available") return "carrierRefusal";
  return null;
}
