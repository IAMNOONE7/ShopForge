import type { BulkListingChange } from "../api/bulkListings";

export type BulkListingAction = "show" | "hide" | "setPrice" | "changePrice" | "categories" | "archive" | "restore";
const zeroDecimals = new Set(["BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"]);
const threeDecimals = new Set(["BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"]);

// Matches the supported currency units in the catalog contract, independently of UI locale data.
export function bulkPriceDecimals(currency: string) { return zeroDecimals.has(currency) ? 0 : threeDecimals.has(currency) ? 3 : 2; }

export function bulkPrice(text: string, currency: string): number | null {
  const normalized = text.trim().replace(",", ".");
  if (!/^\d+(?:\.\d+)?$/.test(normalized)) return null;
  const decimals = bulkPriceDecimals(currency);
  const [whole, fraction = ""] = normalized.split(".");
  if (fraction.replace(/0+$/, "").length > decimals) return null;
  const units = Number(whole + fraction.slice(0, decimals).padEnd(decimals, "0"));
  const value = Number(normalized);
  const [sentWhole, sentFraction = ""] = String(value).split(".");
  const sentUnits = Number(sentWhole + sentFraction.padEnd(decimals, "0"));
  return Number.isSafeInteger(units) && sentUnits === units ? value : null;
}

export function bulkPercent(text: string): number | null {
  const normalized = text.trim().replace(",", ".");
  if (!/^-?\d+(?:\.\d{1,6})?$/.test(normalized)) return null;
  const value = Number(normalized);
  return value > -100 && value <= 1000 ? value : null;
}

export function bulkChange(action: BulkListingAction, value: number, addCategoryIds: string[], removeCategoryIds: string[]): BulkListingChange {
  switch (action) {
    case "show": return { action: "visibility", isVisible: true };
    case "hide": return { action: "visibility", isVisible: false };
    case "setPrice": return { action: "price", set: value };
    case "changePrice": return { action: "price", byPercent: value };
    case "categories": return { action: "categories", addCategoryIds, removeCategoryIds };
    case "archive": return { action: "archive", archived: true };
    case "restore": return { action: "archive", archived: false };
  }
}
