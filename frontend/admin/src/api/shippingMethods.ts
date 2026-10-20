import type { ShippingMethod } from "../api";
import { HttpError, requestJson } from "./http";

export type ShippingMethodInput = Omit<ShippingMethod, "code" | "providerKey">;
export type NewShippingMethodInput = Omit<ShippingMethodInput, "isActive"> & { providerKey: string };

function checked(value: unknown): ShippingMethod {
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
  return value as ShippingMethod;
}
const path = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/shipping-methods`;
function matches(item: ShippingMethod, input: Omit<ShippingMethodInput, "isActive">) {
  const countries = [...new Set(input.countries.map(country => country.trim().toUpperCase()))].sort();
  return item.name === input.name.trim() && item.price === input.price && item.vatRate === input.vatRate
    && item.requiresPickupPoint === input.requiresPickupPoint && item.maxWeightGrams === input.maxWeightGrams
    && item.taxClass === (input.taxClass?.trim() || null) && JSON.stringify([...item.countries].sort()) === JSON.stringify(countries);
}

export async function getShippingMethods(storeId: string, signal?: AbortSignal) {
  const result = await requestJson<unknown>(path(storeId), { signal });
  signal?.throwIfAborted();
  if (!Array.isArray(result)) throw new HttpError("invalid-response", null);
  const items = result.map(checked);
  if (new Set(items.map(item => item.code)).size !== items.length) throw new HttpError("invalid-response", null);
  return items;
}

export async function createShippingMethod(storeId: string, input: NewShippingMethodInput) {
  const item = checked(await requestJson<unknown>(path(storeId), { method: "POST", body: { ...input, isActive: true } }));
  if (!matches(item, input) || item.providerKey !== input.providerKey || !item.isActive) throw new HttpError("invalid-response", null);
  return item;
}

export async function updateShippingMethod(storeId: string, code: string, input: ShippingMethodInput) {
  const item = checked(await requestJson<unknown>(`${path(storeId)}/${encodeURIComponent(code)}`, { method: "PUT", body: input }));
  if (item.code !== code || item.isActive !== input.isActive || !matches(item, input)) throw new HttpError("invalid-response", null);
  return item;
}
