import type { Product, ProductOptionsInput, ProductVariant, VariantInput } from "../api";
import { physicalInput, validateProduct, type ProductDraft, type ProductIssueKey } from "./productValidation";

export type VariantDraft = ProductDraft & { optionValues: string[] };
export type VariantIssue = { field: string; key: ProductIssueKey | "axisLimit" | "axisNameRequired" | "axisNameDuplicate" | "optionValueRequired" };

export function variantDraft(variant?: ProductVariant): VariantDraft {
  return {
    sku: variant?.sku ?? "", ean: variant?.ean ?? "",
    weightGrams: variant?.weightGrams?.toString() ?? "", brand: "",
    partNumber: variant?.partNumber ?? "", condition: variant?.condition ?? "",
    optionValues: [...(variant?.optionValues ?? [])],
  };
}

export function validateVariant(draft: VariantDraft, optionNames: string[]): VariantIssue[] {
  const issues: VariantIssue[] = validateProduct(draft, true);
  optionNames.forEach((_, index) => {
    if (!draft.optionValues[index]?.trim()) {
      issues.push({ field: `optionValues.${index}`, key: "optionValueRequired" });
    }
  });
  return issues;
}

export function variantInput(draft: VariantDraft): VariantInput {
  const { brand: _brand, ...physical } = physicalInput(draft);
  return { ...physical, sku: draft.sku.trim(), optionValues: draft.optionValues.map((value) => value.trim()) };
}

export function optionsDraft(product: Product): ProductOptionsInput {
  return {
    names: [...product.optionNames],
    values: Object.fromEntries(product.variants.map((variant) => [variant.id, [...variant.optionValues]])),
  };
}

export function validateOptions(draft: ProductOptionsInput, product: Product): VariantIssue[] {
  const issues: VariantIssue[] = [];
  if (draft.names.length > 3) issues.push({ field: "names", key: "axisLimit" });
  const seen = new Set<string>();
  draft.names.forEach((name, index) => {
    const normalized = name.trim().toLowerCase();
    if (!normalized) issues.push({ field: `names.${index}`, key: "axisNameRequired" });
    else if (seen.has(normalized)) issues.push({ field: `names.${index}`, key: "axisNameDuplicate" });
    seen.add(normalized);
  });
  product.variants.forEach((variant) => draft.names.forEach((_, index) => {
    if (!draft.values[variant.id]?.[index]?.trim()) {
      issues.push({ field: `values.${variant.id}.${index}`, key: "optionValueRequired" });
    }
  }));
  return issues;
}

export function productVariantRevision(product: Product) {
  return JSON.stringify([product.brand, product.optionNames, product.variants]);
}
