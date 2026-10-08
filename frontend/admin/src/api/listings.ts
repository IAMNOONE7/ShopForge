import type { AdminList, StoreProduct } from "../api";
import { requestJson } from "./http";

export type ListingQuery = { page?: number; pageSize?: number; sort?: string; q?: string };

export function getStoreProducts(storeId: string, signal?: AbortSignal, query: ListingQuery = {}) {
  const parameters = new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 25) });
  if (query.sort) parameters.set("sort", query.sort);
  if (query.q) parameters.set("q", query.q);
  return requestJson<AdminList<StoreProduct>>(`/api/admin/stores/${storeId}/products?${parameters}`, { signal });
}

// There is no single-listing read. Use bounded server pages, match canonical IDs, and stop at the read's total.
async function findListing(storeId: string, matches: (listing: StoreProduct) => boolean, signal?: AbortSignal) {
  signal?.throwIfAborted();
  const first = await getStoreProducts(storeId, signal, { pageSize: 200 });
  signal?.throwIfAborted();
  const found = first.items.find(matches);
  if (found) return found;
  const pages = Math.ceil(first.totalCount / first.pageSize);
  for (let page = 2; first.hasMore && page <= pages; page++) {
    signal?.throwIfAborted();
    const next = await getStoreProducts(storeId, signal, { page, pageSize: 200 });
    signal?.throwIfAborted();
    const item = next.items.find(matches);
    if (item) return item;
    if (!next.hasMore) break;
  }
  return null;
}

export const findStoreProduct = (storeId: string, id: string, signal?: AbortSignal) => findListing(storeId, (item) => item.id === id, signal);
export const hasVisibleStoreProduct = async (storeId: string, signal?: AbortSignal) => (await findListing(storeId, (item) => item.isVisible, signal)) !== null;
