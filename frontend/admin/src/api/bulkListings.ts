import { HttpError, requestJson } from "./http";

export const maxBulkListings = 500;
export type BulkListingChange =
  | { action: "visibility"; isVisible: boolean }
  | { action: "price"; set: number; byPercent?: never }
  | { action: "price"; byPercent: number; set?: never }
  | { action: "categories"; addCategoryIds: string[]; removeCategoryIds: string[] }
  | { action: "tax-class"; taxClass: string | null }
  | { action: "archive"; archived: boolean };
export type BulkListingResult = { asked: number; changed: number };

export async function changeBulkListings(storeId: string, ids: string[], change: BulkListingChange) {
  const storeProductIds = [...new Set(ids)];
  if (!storeProductIds.length || storeProductIds.length > maxBulkListings) throw new RangeError("Invalid listing selection.");
  const { action, ...details } = change;
  const result = await requestJson<BulkListingResult>(`/api/admin/stores/${encodeURIComponent(storeId)}/products/bulk/${action}`, {
    method: "POST", body: { storeProductIds, ...details },
  });
  if (!result || result.asked !== storeProductIds.length || !Number.isInteger(result.changed) || result.changed < 0 || result.changed > result.asked) {
    throw new HttpError("invalid-response", null);
  }
  return result;
}
