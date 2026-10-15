import type { AdminUnappliedPayment } from "../api";
import type { UnappliedAction } from "../api/unappliedPayments";
import { isHttpError } from "../api/http";

export type UnappliedAttempt = { id: string; orderNumber: string; action: UnappliedAction; key: string; note: string | null };
export const attemptStorageKey = (userId: string, storeId: string) => `sf-unapplied:${encodeURIComponent(userId)}:${encodeURIComponent(storeId)}`;
export function readUnappliedAttempt(storageKey: string): UnappliedAttempt | null {
  const saved = sessionStorage.getItem(storageKey);
  if (saved === null) return null;
  const value = JSON.parse(saved) as Partial<UnappliedAttempt> | null;
  if (!value || typeof value.id !== "string" || !value.id.trim() || typeof value.orderNumber !== "string" || !value.orderNumber.trim()
    || typeof value.key !== "string" || !/^[a-zA-Z0-9-]{1,128}$/.test(value.key)
    || (value.action !== "refund" && value.action !== "close")
    || (value.action === "refund" ? value.note !== null : typeof value.note !== "string" || !value.note.trim() || value.note.length > 500)) throw new Error("Invalid saved payment request.");
  return value as UnappliedAttempt;
}
export function newUnappliedAttempt(payment: AdminUnappliedPayment, action: UnappliedAction, note: string): UnappliedAttempt {
  return { id: payment.id, orderNumber: payment.orderNumber, action, key: `sf-${crypto.randomUUID()}`, note: action === "close" ? note.trim() : null };
}
export function canChangePayment(payment: AdminUnappliedPayment, action: UnappliedAction) {
  return action === "refund" ? payment.status === "Open" && payment.canRefund && Boolean(payment.reference?.trim())
    : payment.status === "Open" || payment.status === "Refunding";
}
export function uncertainPaymentWrite(error: unknown) {
  return !isHttpError(error) || error.kind !== "http" || error.status === null || error.status >= 500
    || (error.status === 409 && error.problem?.title === "A request with this key is still in progress");
}
export function paymentProblem(error: unknown) {
  if (!isHttpError(error)) return null;
  const titles = {
    "This payment cannot be refunded from here": "cannotRefund",
    "The refund was refused": "refused",
    "The refund was not confirmed": "unconfirmed",
    "This payment has already been settled": "settled",
    "This payment changed while it was being closed": "changed",
    "A request with this key is still in progress": "inFlight",
    "This idempotency key was used for a different request": "keyMismatch",
  } as const;
  return Object.entries(titles).find(([title]) => title === error.problem?.title)?.[1] ?? null;
}
