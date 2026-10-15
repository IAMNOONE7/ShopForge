import type { AdminOrderDetail } from "../api";
import { HttpError, requestJson } from "./http";
import { isUnappliedPayment } from "./unappliedPayments";

const object = (value: unknown): Record<string, unknown> | null => typeof value === "object" && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : null;
const string = (value: unknown): value is string => typeof value === "string";
const text = (value: unknown) => string(value) && value.trim().length > 0;
const nullableString = (value: unknown) => value === null || string(value);
const amount = (value: unknown) => typeof value === "number" && Number.isFinite(value);
const date = (value: unknown) => string(value) && Number.isFinite(Date.parse(value));
const nullableDate = (value: unknown) => value === null || date(value);
const currency = (value: unknown) => string(value) && /^[A-Z]{3}$/.test(value);
function list(value: unknown, check: (item: Record<string, unknown>) => boolean, unique?: string) {
  if (!Array.isArray(value) || !value.every((item) => { const row = object(item); return row !== null && check(row); })) return false;
  return unique === undefined || new Set(value.map((item) => item[unique])).size === value.length;
}
function address(value: unknown) {
  const item = object(value);
  return item !== null && ["fullName", "line1", "city", "postalCode", "country"].every((key) => string(item[key])) && nullableString(item.line2);
}
function validOrder(value: unknown): value is AdminOrderDetail {
  const item = object(value);
  if (!item) return false;
  const discount = item.discount === null ? null : object(item.discount);
  const shipment = item.shipment === null ? null : object(item.shipment);
  return text(item.number) && date(item.placedAt) && string(item.status) && string(item.email) && nullableString(item.phone) && currency(item.currency)
    && string(item.paymentMethod) && string(item.shippingMethod) && nullableString(item.carrier)
    && ["shippingPrice", "itemsTotal", "vatTotal", "grandTotal"].every((key) => amount(item[key]))
    && (item.discount === null || (discount !== null && string(discount.code) && string(discount.name) && amount(discount.amount)))
    && address(item.billingAddress) && address(item.shippingAddress) && nullableString(item.pickupPoint)
    && (item.shipment === null || (shipment !== null && string(shipment.carrier) && string(shipment.trackingNumber) && nullableString(shipment.trackingUrl) && date(shipment.shippedAt)))
    && nullableString(item.paymentReference) && nullableDate(item.paidAt)
    && list(item.paymentAttempts, (attempt) => text(attempt.provider) && nullableString(attempt.reference) && string(attempt.status)
      && nullableString(attempt.environment) && amount(attempt.amount) && currency(attempt.currency) && date(attempt.startedAt) && date(attempt.changedAt))
    && list(item.unappliedPayments, (payment) => isUnappliedPayment(payment) && payment.orderNumber === item.number, "id")
    && list(item.returns, (returned) => text(returned.id) && text(returned.number) && string(returned.status) && amount(returned.refundedAmount)
      && nullableString(returned.refund) && nullableString(returned.refundProblem), "id")
    && list(item.documents, (document) => text(document.number) && string(document.kind) && date(document.issuedAt), "number")
    && list(item.lines, (line) => string(line.productName) && amount(line.unitPrice) && amount(line.vatRate) && amount(line.lineTotal)
      && Number.isSafeInteger(line.quantity) && Number(line.quantity) >= 0);
}
function checked(value: unknown, number: string) {
  if (!validOrder(value) || value.number !== number) throw new HttpError("invalid-response", 200);
  return value;
}
const path = (storeId: string, number: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/orders/${encodeURIComponent(number)}`;

export async function getOrderDetails(storeId: string, number: string, signal?: AbortSignal) {
  return checked(await requestJson<unknown>(path(storeId, number), { signal }), number);
}
export async function changeOrder(storeId: string, number: string, action: "payment" | "cancel" | "shipment" | "refund", body?: { trackingNumber: string }) {
  const value = checked(await requestJson<unknown>(`${path(storeId, number)}/${action}`, { method: "POST", body }), number);
  const status = { payment: "Paid", cancel: "Cancelled", shipment: "Shipped", refund: null }[action];
  if ((status !== null && value.status !== status) || (action === "shipment" && value.shipment === null)) throw new HttpError("invalid-response", 200);
  return value;
}
