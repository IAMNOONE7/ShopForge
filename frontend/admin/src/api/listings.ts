import type { AdminList, StoreProduct } from "../api";
import { HttpError, isHttpError, requestJson } from "./http";

export type ListingQuery = { page?: number; pageSize?: number; sort?: string; q?: string; archived?: boolean };

export function isListing(value: unknown): value is StoreProduct {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return false;
  const item = value as Record<string, unknown>;
  const id = (value: unknown) => typeof value === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
  const text = (value: unknown) => typeof value === "string" && value.trim().length > 0;
  const nullableText = (value: unknown) => value === null || typeof value === "string";
  return id(item.id) && id(item.productId) && text(item.sku) && text(item.name) && text(item.slug)
    && nullableText(item.description) && nullableText(item.taxClass)
    && typeof item.price === "number" && Number.isFinite(item.price) && item.price >= 0
    && typeof item.vatRate === "number" && Number.isFinite(item.vatRate) && item.vatRate >= 0 && item.vatRate <= 100
    && Number.isInteger(item.sortOrder) && Number(item.sortOrder) >= -2147483648 && Number(item.sortOrder) <= 2147483647
    && typeof item.isVisible === "boolean" && Array.isArray(item.categoryIds) && item.categoryIds.every(id)
    && nullableText(item.seoTitle) && nullableText(item.seoDescription) && nullableText(item.seoSocialImageUrl) && typeof item.seoNoIndex === "boolean"
    && (item.archivedAt === null || typeof item.archivedAt === "string" && /^\d{4}-\d{2}-\d{2}T/.test(item.archivedAt) && Number.isFinite(Date.parse(item.archivedAt)));
}

const path = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/products`;

export async function getStoreProducts(storeId: string, signal?: AbortSignal, query: ListingQuery = {}): Promise<AdminList<StoreProduct>> {
  const parameters = new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 25) });
  if (query.sort) parameters.set("sort", query.sort);
  if (query.q) parameters.set("q", query.q);
  if (query.archived) parameters.set("archived", "true");
  const value = await requestJson<AdminList<unknown>>(`${path(storeId)}?${parameters}`, { signal });
  if (!value || !Array.isArray(value.items) || !value.items.every(isListing)
    || value.items.some(item => (item.archivedAt !== null) !== (query.archived === true))
    || !Number.isSafeInteger(value.totalCount) || value.totalCount < 0
    || !Number.isInteger(value.page) || value.page < 1 || !Number.isInteger(value.pageSize) || value.pageSize < 1 || value.pageSize > 200
    || typeof value.hasMore !== "boolean") throw new HttpError("invalid-response", 200);
  return value as AdminList<StoreProduct>;
}

export async function findStoreProduct(storeId: string, id: string, signal?: AbortSignal): Promise<StoreProduct | null> {
  signal?.throwIfAborted();
  let value: unknown;
  try {
    value = await requestJson<unknown>(`${path(storeId)}/${encodeURIComponent(id)}`, { signal });
  } catch (error) {
    signal?.throwIfAborted();
    if (isHttpError(error) && error.status === 404) return null;
    throw error;
  }
  signal?.throwIfAborted();
  if (!isListing(value) || value.id.toLowerCase() !== id.toLowerCase()) throw new HttpError("invalid-response", 200);
  return value;
}

// Publication discovery still searches the actual collection with bounded pages.
export async function hasVisibleStoreProduct(storeId: string, signal?: AbortSignal) {
  signal?.throwIfAborted();
  const first = await getStoreProducts(storeId, signal, { pageSize: 200 });
  signal?.throwIfAborted();
  if (first.items.some(item => item.isVisible)) return true;
  const pages = Math.ceil(first.totalCount / first.pageSize);
  for (let page = 2; first.hasMore && page <= pages; page++) {
    signal?.throwIfAborted();
    const next = await getStoreProducts(storeId, signal, { page, pageSize: 200 });
    signal?.throwIfAborted();
    if (next.items.some(item => item.isVisible)) return true;
    if (!next.hasMore) break;
  }
  return false;
}
