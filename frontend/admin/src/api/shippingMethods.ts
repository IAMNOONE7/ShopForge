import type { ShippingMethod } from "../api";
import { HttpError, requestJson } from "./http";

export type ShippingMethodInput = Pick<ShippingMethod, "name" | "price" | "vatRate" | "isActive" | "requiresPickupPoint" | "maxWeightGrams" | "countries" | "taxClass" | "pricing" | "carrierService" | "parcelMaxWeightGrams" | "parcelLengthMm" | "parcelWidthMm" | "parcelHeightMm">;
export type NewShippingMethodInput = Omit<ShippingMethodInput, "isActive"> & { providerKey: string };

export function checkedShippingMethod(value: unknown): ShippingMethod {
  if (!value || typeof value !== "object") throw new HttpError("invalid-response", null);
  const row = value as Record<string, unknown>;
  if (typeof row.code !== "string" || !row.code.trim() || typeof row.name !== "string" || !row.name.trim()
    || typeof row.providerKey !== "string" || !row.providerKey.trim()
    || typeof row.price !== "number" || !Number.isFinite(row.price) || row.price < 0
    || typeof row.vatRate !== "number" || !Number.isFinite(row.vatRate) || row.vatRate < 0 || row.vatRate > 100
    || typeof row.isActive !== "boolean" || typeof row.requiresPickupPoint !== "boolean"
    || row.maxWeightGrams !== null && (typeof row.maxWeightGrams !== "number" || !Number.isInteger(row.maxWeightGrams) || row.maxWeightGrams <= 0 || row.maxWeightGrams > 2147483647)
    || !Array.isArray(row.countries) || !row.countries.every(country => typeof country === "string" && country.trim().length === 2)
    || row.taxClass !== null && (typeof row.taxClass !== "string" || !row.taxClass.trim())) throw new HttpError("invalid-response", null);
  const terms = ["carrierService", "parcelMaxWeightGrams", "parcelLengthMm", "parcelWidthMm", "parcelHeightMm", "ratesFrom", "ratesPricedOn", "rateBands"];
  if (row.pricing === undefined && terms.some(field => row[field] !== undefined)) throw new HttpError("invalid-response", null);
  if (row.pricing !== undefined) {
    const nullableInteger = (value: unknown, max: number) => value === null || typeof value === "number" && Number.isInteger(value) && value > 0 && value <= max;
    const sizes = [row.parcelLengthMm, row.parcelWidthMm, row.parcelHeightMm];
    if (!["flat", "perParcel"].includes(String(row.pricing)) || row.carrierService !== null && (typeof row.carrierService !== "string" || !row.carrierService)
      || row.pricing === "perParcel" && (!row.carrierService || row.maxWeightGrams !== null)
      || !nullableInteger(row.parcelMaxWeightGrams, 2147483647) || !sizes.every(size => nullableInteger(size, 10000))
      || sizes.some(size => size !== null) && sizes.some(size => size === null)
      || row.ratesFrom !== null && !["carrier", "own", "placeholder"].includes(String(row.ratesFrom))
      || row.ratesPricedOn !== null && (typeof row.ratesPricedOn !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(row.ratesPricedOn))
      || row.ratesFrom === "carrier" && row.ratesPricedOn === null
      || row.pricing === "flat" && (row.ratesFrom !== null || sizes.some(size => size !== null) || row.parcelMaxWeightGrams !== null)
      || !Array.isArray(row.rateBands) || row.rateBands.length > 50
      || !row.rateBands.every((band, index, bands) => band && Number.isInteger(band.upToGrams) && band.upToGrams > 0 && band.upToGrams <= 2147483647 && typeof band.price === "number" && Number.isFinite(band.price) && band.price >= 0 && (index === 0 || band.upToGrams > bands[index - 1].upToGrams))
      || row.ratesFrom === null && row.rateBands.length !== 0 || row.ratesFrom !== null && row.rateBands.length === 0) throw new HttpError("invalid-response", null);
  }
  return value as ShippingMethod;
}
const path = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/shipping-methods`;
function matches(item: ShippingMethod, input: Omit<ShippingMethodInput, "isActive">) {
  const countries = [...new Set(input.countries.map(country => country.trim().toUpperCase()))].sort();
  return item.name === input.name.trim() && item.price === input.price && item.vatRate === input.vatRate
    && item.requiresPickupPoint === input.requiresPickupPoint && item.maxWeightGrams === input.maxWeightGrams
    && (input.pricing === undefined || item.pricing === input.pricing && item.carrierService === (input.carrierService ?? null)
      && item.parcelMaxWeightGrams === (input.parcelMaxWeightGrams ?? null)
      && JSON.stringify([item.parcelLengthMm, item.parcelWidthMm, item.parcelHeightMm]) === JSON.stringify([input.parcelLengthMm ?? null, input.parcelWidthMm ?? null, input.parcelHeightMm ?? null].sort((a,b) => (b ?? 0) - (a ?? 0))))
    && item.taxClass === (input.taxClass?.trim() || null) && JSON.stringify([...item.countries].sort()) === JSON.stringify(countries);
}

export async function getShippingMethods(storeId: string, signal?: AbortSignal) {
  const result = await requestJson<unknown>(path(storeId), { signal });
  signal?.throwIfAborted();
  if (!Array.isArray(result)) throw new HttpError("invalid-response", null);
  const items = result.map(checkedShippingMethod);
  if (new Set(items.map(item => item.code)).size !== items.length) throw new HttpError("invalid-response", null);
  return items;
}

export async function createShippingMethod(storeId: string, input: NewShippingMethodInput, signal?: AbortSignal) {
  const item = checkedShippingMethod(await requestJson<unknown>(path(storeId), { method: "POST", signal, body: { ...input, isActive: true } }));
  signal?.throwIfAborted();
  if (!matches(item, input) || item.providerKey !== input.providerKey || !item.isActive) throw new HttpError("invalid-response", null);
  return item;
}

export async function updateShippingMethod(storeId: string, code: string, input: ShippingMethodInput, signal?: AbortSignal) {
  const item = checkedShippingMethod(await requestJson<unknown>(`${path(storeId)}/${encodeURIComponent(code)}`, { method: "PUT", signal, body: input }));
  signal?.throwIfAborted();
  if (item.code !== code || item.isActive !== input.isActive || !matches(item, input)) throw new HttpError("invalid-response", null);
  return item;
}
