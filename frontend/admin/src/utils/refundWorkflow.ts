import type { AdminOrderDetail, AdminOrderReturn, OrderReturn } from "../api";
import { isHttpError } from "../api/http";

export type RefundAction = "order-refund" | "receive" | "retry" | "close";
export type RefundAttempt = {
  action: RefundAction; key: string; orderNumber: string; currency: string;
  returnId: string | null; returnNumber: string | null; note: string | null;
  previousReturnIds: string[]; reviewOnly?: true;
};
export type RefundOutcome = { attempt: RefundAttempt; records: AdminOrderReturn[] };
export const refundStorageKey = (userId: string, storeId: string) => `sf-refund:${encodeURIComponent(userId)}:${encodeURIComponent(storeId)}`;

export function readRefundAttempt(storageKey: string): RefundAttempt | null {
  const saved = sessionStorage.getItem(storageKey);
  if (saved === null) return null;
  const item = JSON.parse(saved) as Partial<RefundAttempt> | null;
  const text = (value: unknown) => typeof value === "string" && value.trim().length > 0;
  if (!item || !["order-refund", "receive", "retry", "close"].includes(item.action ?? "") || !text(item.orderNumber)
    || typeof item.currency !== "string" || !/^[A-Z]{3}$/.test(item.currency)
    || typeof item.key !== "string" || !/^[a-zA-Z0-9-]{1,128}$/.test(item.key)
    || !Array.isArray(item.previousReturnIds) || !item.previousReturnIds.every(text) || new Set(item.previousReturnIds).size !== item.previousReturnIds.length
    || (item.action === "order-refund" ? item.returnId !== null || item.returnNumber !== null : !text(item.returnId) || !text(item.returnNumber) || item.previousReturnIds.length > 0)
    || (item.action === "close" ? !text(item.note) || String(item.note).trim() !== item.note || String(item.note).length > 500 : item.note !== null)
    || (item.reviewOnly !== undefined && item.reviewOnly !== true)) throw new Error("Invalid saved refund request.");
  return item as RefundAttempt;
}

export function makeRefundAttempt(action: RefundAction, order: AdminOrderDetail, record?: OrderReturn, note = ""): RefundAttempt {
  if (action !== "order-refund" && !record) throw new Error("A return is required.");
  return { action, orderNumber: order.number, currency: order.currency, key: `sf-${crypto.randomUUID()}`,
    returnId: action === "order-refund" ? null : record!.id, returnNumber: action === "order-refund" ? null : record!.number,
    note: action === "close" ? note.trim() : null, previousReturnIds: action === "order-refund" ? order.returns.map((item) => item.id) : [] };
}

export function canChangeRefund(action: RefundAction, order: AdminOrderDetail, record?: OrderReturn | null) {
  if (action === "order-refund") return ["Paid", "Shipped"].includes(order.status);
  if (!record || record.orderNumber !== order.number) return false;
  if (action === "receive") return record.status === "Accepted" && ["Paid", "Shipped"].includes(order.status) && record.refund === null;
  if (record.status !== "Received") return false;
  return action === "retry" ? record.refund === "Failed" : record.refund === "Failed" || record.refund === "Pending";
}

export function canRecoverRefund(attempt: RefundAttempt, order: AdminOrderDetail, record?: OrderReturn | null) {
  if (attempt.reviewOnly || order.number !== attempt.orderNumber || order.currency !== attempt.currency) return false;
  // The order can remain Paid after a partial credit. A new return is evidence that receipt may already be booked.
  if (attempt.action === "order-refund" && order.returns.some((item) => !attempt.previousReturnIds.includes(item.id))) return false;
  if (attempt.action !== "order-refund" && (record?.id !== attempt.returnId || record?.number !== attempt.returnNumber)) return false;
  return canChangeRefund(attempt.action, order, record);
}

export function refundNeedsRecovery(error: unknown) {
  return !isHttpError(error) || error.kind !== "http" || error.status === null || error.status >= 500
    || (error.status === 409 && error.problem?.title === "A request with this key is still in progress")
    || (error.status === 422 && error.problem?.title === "This idempotency key was used for a different request");
}

export function refundProblem(error: unknown) {
  if (!isHttpError(error)) return null;
  const titles = {
    "Only an accepted return of a paid order can be received": "cannotReceive",
    "Only a refund the provider refused can be tried again": "cannotRetry",
    "Only a refund that was refused or never confirmed can be closed by hand": "cannotClose",
    "There is nothing left to refund on this order": "nothingLeft",
    "Only a paid order can be refunded": "cannotRefundOrder",
    "The refund was refused": "refused", "The refund was not confirmed": "unconfirmed",
    "A request with this key is still in progress": "inFlight",
    "This idempotency key was used for a different request": "keyMismatch",
  } as const;
  return Object.entries(titles).find(([title]) => error.problem?.title === title)?.[1] ?? null;
}
