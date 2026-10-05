export type ProductField =
  | "sku"
  | "ean"
  | "weightGrams"
  | "brand"
  | "partNumber"
  | "condition"
  | "file"
  | "altText";
export type ProductIssueKey =
  | "skuInvalid"
  | "eanInvalid"
  | "weightInvalid"
  | "brandInvalid"
  | "partNumberInvalid"
  | "fileRequired"
  | "fileTypeInvalid"
  | "fileSizeInvalid"
  | "altTextInvalid";
export type ProductIssue = { field: ProductField; key: ProductIssueKey };
export type ProductDraft = {
  sku: string;
  ean: string;
  weightGrams: string;
  brand: string;
  partNumber: string;
  condition: string;
};

export const conditions = ["", "New", "Refurbished", "Used"] as const;

// The same rule the server keeps: a barcode is a number that checks itself, and a feed rejects the whole
// product for one that does not. Catching it here saves a round trip, not the check.
export function isGtin(barcode: string) {
  if (!/^[0-9]+$/.test(barcode) || ![8, 12, 13, 14].includes(barcode.length)) {
    return false;
  }
  const body = barcode.slice(0, -1);
  let total = 0;
  for (let position = 0; position < body.length; position++) {
    total += Number(body[position]) * ((body.length - position) % 2 === 1 ? 3 : 1);
  }
  return (10 - (total % 10)) % 10 === Number(barcode[barcode.length - 1]);
}

export function validateProduct(
  draft: ProductDraft,
  creating: boolean,
): ProductIssue[] {
  const issues: ProductIssue[] = [];
  if (creating && (!draft.sku.trim() || draft.sku.trim().length > 64)) {
    issues.push({ field: "sku", key: "skuInvalid" });
  }
  const ean = draft.ean.trim();
  if (ean && !isGtin(ean)) {
    issues.push({ field: "ean", key: "eanInvalid" });
  }
  if (draft.brand.trim().length > 70) {
    issues.push({ field: "brand", key: "brandInvalid" });
  }
  if (draft.partNumber.trim().length > 70) {
    issues.push({ field: "partNumber", key: "partNumberInvalid" });
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
    brand: draft.brand.trim() || null,
    partNumber: draft.partNumber.trim() || null,
    condition: draft.condition || null,
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
