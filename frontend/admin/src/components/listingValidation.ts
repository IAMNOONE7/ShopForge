import type { StoreProduct, StoreProductInput } from "../api";
import { generatedCode, validCode } from "./attributeValidation";

export type ListingDraft = { name: string; slug: string; description: string; price: string; vatRate: string; sortOrder: string; isVisible: boolean };
export type ListingIssue = { field: Exclude<keyof ListingDraft, "isVisible">; key: "nameInvalid" | "slugInvalid" | "priceInvalid" | "vatInvalid" | "sortInvalid" };

export function listingDraft(item?: StoreProduct): ListingDraft {
  return { name: item?.name ?? "", slug: item?.slug ?? "", description: item?.description ?? "", price: String(item?.price ?? ""),
    vatRate: String(item?.vatRate ?? 21), sortOrder: String(item?.sortOrder ?? 0), isVisible: item?.isVisible ?? false };
}

export function decimalNumber(text: string) { return Number(text.trim().replace(",", ".")); }
function moneyValid(text: string) { return /^\d{1,13}(?:[.,]\d{1,2})?$/.test(text.trim()); }

export function validateListing(draft: ListingDraft, editing: boolean): ListingIssue[] {
  const issues: ListingIssue[] = [];
  if (!draft.name.trim() || draft.name.trim().length > 200) issues.push({ field: "name", key: "nameInvalid" });
  if (!validCode(draft.slug.trim() || (editing ? "" : generatedCode(draft.name)))) issues.push({ field: "slug", key: "slugInvalid" });
  if (!moneyValid(draft.price)) issues.push({ field: "price", key: "priceInvalid" });
  if (!moneyValid(draft.vatRate) || decimalNumber(draft.vatRate) > 100) issues.push({ field: "vatRate", key: "vatInvalid" });
  if (!/^-?\d+$/.test(draft.sortOrder.trim()) || Number(draft.sortOrder) < -2147483648 || Number(draft.sortOrder) > 2147483647) issues.push({ field: "sortOrder", key: "sortInvalid" });
  return issues;
}

export function listingInput(draft: ListingDraft): StoreProductInput {
  return { name: draft.name.trim(), slug: draft.slug.trim() || null, description: draft.description.trim() || null,
    price: decimalNumber(draft.price), vatRate: decimalNumber(draft.vatRate), sortOrder: Number(draft.sortOrder), isVisible: draft.isVisible };
}
