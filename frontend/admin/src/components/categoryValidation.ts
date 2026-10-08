import type { Category, CategoryInput } from "../api";
import { generatedCode, validCode } from "./attributeValidation";
import { canParentCategory } from "./categoryTree";

export type CategoryDraft = { name: string; slug: string; sortOrder: string; parentId: string };
export type CategoryIssue = { field: keyof CategoryDraft; key: "nameInvalid" | "slugInvalid" | "sortInvalid" | "parentInvalid" };

export function categoryDraft(category?: Category): CategoryDraft {
  return { name: category?.name ?? "", slug: category?.slug ?? "", sortOrder: String(category?.sortOrder ?? 0), parentId: category?.parentId ?? "" };
}

export function validateCategory(draft: CategoryDraft, categories: Category[], categoryId?: string): CategoryIssue[] {
  const issues: CategoryIssue[] = [];
  if (!draft.name.trim() || draft.name.trim().length > 200) issues.push({ field: "name", key: "nameInvalid" });
  const slug = draft.slug.trim();
  if (!validCode(slug || (categoryId ? "" : generatedCode(draft.name)))) issues.push({ field: "slug", key: "slugInvalid" });
  if (!/^-?[0-9]+$/.test(draft.sortOrder.trim()) || Number(draft.sortOrder) < -2147483648 || Number(draft.sortOrder) > 2147483647) {
    issues.push({ field: "sortOrder", key: "sortInvalid" });
  }
  if (!canParentCategory(categories, draft.parentId || null, categoryId)) issues.push({ field: "parentId", key: "parentInvalid" });
  return issues;
}

export function categoryInput(draft: CategoryDraft): CategoryInput {
  return { name: draft.name.trim(), slug: draft.slug.trim() || null, sortOrder: Number(draft.sortOrder), parentId: draft.parentId || null };
}
