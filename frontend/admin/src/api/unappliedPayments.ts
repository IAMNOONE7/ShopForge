import type { AdminList, AdminUnappliedPayment } from "../api";
import { maxUnappliedPage, unappliedPageSize, unappliedStatuses, type UnappliedQuery } from "../utils/unappliedQuery";
import { HttpError, requestJson } from "./http";

export function isUnappliedPayment(value: unknown): value is AdminUnappliedPayment {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
  const item = value as Record<string, unknown>;
  const text = (value: unknown) => typeof value === "string" && value.trim().length > 0;
  const nullable = (value: unknown) => value === null || typeof value === "string";
  const date = (value: unknown) => typeof value === "string" && Number.isFinite(Date.parse(value));
  return text(item.id) && text(item.orderNumber) && text(item.provider) && nullable(item.reference)
    && typeof item.amount === "number" && Number.isFinite(item.amount)
    && typeof item.currency === "string" && /^[A-Z]{3}$/.test(item.currency)
    && typeof item.reason === "string" && typeof item.status === "string"
    && date(item.receivedAt) && date(item.changedAt) && nullable(item.note) && typeof item.canRefund === "boolean";
}
const path = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/unapplied-payments`;

export async function getUnappliedPayments(storeId: string, query: UnappliedQuery, signal?: AbortSignal): Promise<AdminList<AdminUnappliedPayment>> {
  if (!Number.isSafeInteger(query.page) || query.page < 1 || query.page > maxUnappliedPage
    || (query.status !== "" && !unappliedStatuses.includes(query.status))) throw new RangeError("Invalid payment query.");
  const parameters = new URLSearchParams({ page: String(query.page), pageSize: String(unappliedPageSize) });
  if (query.status) parameters.set("status", query.status);
  const value = await requestJson<unknown>(`${path(storeId)}?${parameters}`, { signal });
  if (typeof value !== "object" || value === null) throw new HttpError("invalid-response", 200);
  const result = value as AdminList<unknown>;
  if (!Array.isArray(result.items) || !result.items.every(isUnappliedPayment) || result.items.length > unappliedPageSize
    || new Set(result.items.map((item) => item.id)).size !== result.items.length
    || (query.status !== "" && result.items.some((item) => item.status !== query.status))
    || !Number.isSafeInteger(result.totalCount) || result.totalCount < result.items.length
    || result.page !== query.page || result.pageSize !== unappliedPageSize
    || result.hasMore !== (result.page * result.pageSize < result.totalCount)) throw new HttpError("invalid-response", 200);
  return result as AdminList<AdminUnappliedPayment>;
}

export type UnappliedAction = "refund" | "close";
export async function changeUnappliedPayment(storeId: string, id: string, action: UnappliedAction, key: string, note?: string) {
  if (!key || key.length > 128) throw new RangeError("Invalid request key.");
  const value = await requestJson<unknown>(`${path(storeId)}/${encodeURIComponent(id)}/${action}`, {
    method: "POST", idempotencyKey: key, body: action === "close" ? { note } : undefined,
  });
  if (!isUnappliedPayment(value) || value.id !== id || (action === "close" && value.status !== "ClosedByHand")
    || (action === "refund" && value.status !== "Refunded" && value.status !== "ClosedByHand")) throw new HttpError("invalid-response", 200);
  return value;
}
