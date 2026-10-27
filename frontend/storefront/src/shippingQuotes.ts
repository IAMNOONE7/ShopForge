import { HttpError, isHttpError } from "./api/http";
import type { Cart, ShippingMethod } from "./cart";

export type MapParcel = {
  weightKg: number;
  lengthCm: number | null;
  widthCm: number | null;
  depthCm: number | null;
};

export type ShippingRefusal = "notToCountry" | "tooHeavy" | "tooLarge" | "missingData" | "tooManyParcels";
export type UnavailableShippingMethod = { code: string; name: string; reason: ShippingRefusal; variantId: string | null };
export type QuotedParcel = {
  contents: { variantId: string; sku: string; quantity: number }[];
  grossWeightGrams: number;
  bandUpToGrams: number;
  price: number;
  alone: boolean;
};
export type ShippingQuote = { currency: string; total: number; parcels: QuotedParcel[]; isAvailable: true; unavailable: null };

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const money = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value) && value >= 0;
const positiveInteger = (value: unknown): value is number => typeof value === "number" && Number.isSafeInteger(value) && value > 0;

export function validMapParcel(value: unknown): value is MapParcel {
  if (!record(value) || typeof value.weightKg !== "number" || !Number.isFinite(value.weightKg) || value.weightKg <= 0) return false;
  const sides = [value.lengthCm, value.widthCm, value.depthCm];
  return sides.every(side => side === null) || sides.every(positiveInteger);
}

export function validShippingTerms(value: unknown) {
  if (!record(value)) return false;
  // Older flat offers have none of the additive terms; a partial new offer is not a usable quote.
  if (value.pricing === undefined && value.parcels === undefined && value.mapParcel === undefined) return true;
  if (value.pricing === "flat") return value.parcels === null && value.mapParcel === null;
  return value.pricing === "perParcel" && positiveInteger(value.parcels) && validMapParcel(value.mapParcel);
}

export function validUnavailableMethod(value: unknown): value is UnavailableShippingMethod {
  return record(value) && text(value.code) && text(value.name) &&
    ["notToCountry", "tooHeavy", "tooLarge", "missingData", "tooManyParcels"].includes(value.reason as string) &&
    (value.variantId === null || text(value.variantId));
}

function validParcel(value: unknown): value is QuotedParcel {
  return record(value) && positiveInteger(value.grossWeightGrams) && positiveInteger(value.bandUpToGrams) &&
    value.grossWeightGrams <= value.bandUpToGrams && money(value.price) && typeof value.alone === "boolean" &&
    Array.isArray(value.contents) && value.contents.length > 0 && value.contents.every(item =>
      record(item) && text(item.variantId) && text(item.sku) && positiveInteger(item.quantity)) &&
    new Set(value.contents.map(item => item.variantId)).size === value.contents.length;
}

export function deliveryPriceChanged(error: unknown) {
  return isHttpError(error) && error.status === 409 && error.problem?.title === "The delivery price has changed";
}

export function readChangedShippingQuote(error: unknown, currency: string, cart: Cart, method: ShippingMethod): ShippingQuote | null {
  if (!deliveryPriceChanged(error) || !isHttpError(error)) return null;
  const value = error.problem?.shippingQuote;
  if (!record(value) || value.currency !== currency || !money(value.total) || value.isAvailable !== true ||
    value.unavailable !== null || !Array.isArray(value.parcels) || !value.parcels.every(validParcel)) invalid();
  const parcels = value.parcels as QuotedParcel[];
  if (method.pricing === "perParcel") {
    if (parcels.length === 0 || Math.abs(parcels.reduce((sum, parcel) => sum + parcel.price, 0) - value.total) > 0.000001) invalid();
    const expected = new Map<string, number>();
    const actual = new Map<string, number>();
    for (const item of cart.items) expected.set(item.variantId, (expected.get(item.variantId) ?? 0) + item.quantity);
    for (const parcel of parcels) for (const item of parcel.contents) actual.set(item.variantId, (actual.get(item.variantId) ?? 0) + item.quantity);
    if (expected.size !== actual.size || [...expected].some(([id, count]) => actual.get(id) !== count)) invalid();
  } else if (parcels.length !== 0) invalid();
  return { currency, total: value.total, parcels, isAvailable: true, unavailable: null };
}

function invalid(): never { throw new HttpError("invalid-response", 409); }

export function shippingMethodErrors(error: unknown): string[] {
  if (!isHttpError(error) || error.status !== 400) return [];
  const messages = error.problem?.errors?.shippingMethodCode;
  return Array.isArray(messages) ? messages.filter(text) : [];
}
