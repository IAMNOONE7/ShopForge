import type { AdminList, AdminOrder } from "../api";
import { maxOrderPage, orderPageSize, type OrderListQuery } from "../utils/orderListQuery";
import { HttpError, requestJson } from "./http";

function order(value: unknown): value is AdminOrder {
  if (typeof value !== "object" || value === null) return false;
  const item = value as Record<string, unknown>;
  return typeof item.number === "string" && item.number.trim().length > 0
    && typeof item.placedAt === "string" && Number.isFinite(Date.parse(item.placedAt))
    && typeof item.status === "string" && typeof item.email === "string" && typeof item.hasAccount === "boolean"
    && typeof item.grandTotal === "number" && Number.isFinite(item.grandTotal)
    && Number.isSafeInteger(item.items) && Number(item.items) >= 0;
}

export async function getOrders(storeId: string, signal?: AbortSignal, query: OrderListQuery = { page: 1, q: "", sort: "-placed" }): Promise<AdminList<AdminOrder>> {
  if (!Number.isSafeInteger(query.page) || query.page < 1 || query.page > maxOrderPage) throw new RangeError("Invalid order page.");
  const parameters = new URLSearchParams({ page: String(query.page), pageSize: String(orderPageSize), sort: query.sort });
  if (query.q) parameters.set("q", query.q);
  const value = await requestJson<unknown>(`/api/admin/stores/${encodeURIComponent(storeId)}/orders?${parameters}`, { signal });
  if (typeof value !== "object" || value === null) throw new HttpError("invalid-response", 200);
  const result = value as AdminList<unknown>;
  if (!Array.isArray(result.items) || !result.items.every(order) || result.items.length > orderPageSize
    || new Set(result.items.map((item) => item.number)).size !== result.items.length
    || !Number.isSafeInteger(result.totalCount) || result.totalCount < 0
    || result.page !== query.page || result.pageSize !== orderPageSize
    || result.hasMore !== (result.page * result.pageSize < result.totalCount)) throw new HttpError("invalid-response", 200);
  return result as AdminList<AdminOrder>;
}
