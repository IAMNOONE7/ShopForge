export type ProductField = "sku" | "ean" | "weightGrams" | "file" | "altText";
export type ProductIssueKey =
  | "skuInvalid"
  | "eanInvalid"
  | "weightInvalid"
  | "fileRequired"
  | "fileTypeInvalid"
  | "fileSizeInvalid"
  | "altTextInvalid";
export type ProductIssue = { field: ProductField; key: ProductIssueKey };
export type ProductDraft = {
  sku: string;
  ean: string;
  weightGrams: string;
};

export function validateProduct(
  draft: ProductDraft,
  creating: boolean,
): ProductIssue[] {
  const issues: ProductIssue[] = [];
  if (creating && (!draft.sku.trim() || draft.sku.trim().length > 64)) {
    issues.push({ field: "sku", key: "skuInvalid" });
  }
  const ean = draft.ean.trim();
  if (ean && !/^[0-9]{8,14}$/.test(ean)) {
    issues.push({ field: "ean", key: "eanInvalid" });
  }
  const weight = draft.weightGrams.trim();
  if (
    weight &&
    (!/^[0-9]+$/.test(weight) || Number(weight) > 2147483647)
  ) {
    issues.push({ field: "weightGrams", key: "weightInvalid" });
  }
  return issues;
}

export function physicalInput(draft: ProductDraft) {
  return {
    ean: draft.ean.trim() || null,
    weightGrams: draft.weightGrams.trim()
      ? Number(draft.weightGrams.trim())
      : null,
  };
}

export function validateProductImage(
  file: File | null,
  altText: string,
): ProductIssue[] {
  const issues: ProductIssue[] = [];
  if (!file || file.size === 0) {
    issues.push({ field: "file", key: "fileRequired" });
  } else {
    if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) {
      issues.push({ field: "file", key: "fileTypeInvalid" });
    }
    if (file.size > 5 * 1024 * 1024) {
      issues.push({ field: "file", key: "fileSizeInvalid" });
    }
  }
  if (!altText.trim() || altText.trim().length > 300) {
    issues.push({ field: "altText", key: "altTextInvalid" });
  }
  return issues;
}
