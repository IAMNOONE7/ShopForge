import { HttpError, requestJson } from "./http";

export const shippingSettingLimits = {
  packagingWeightGrams: { min: 0, max: 10000 },
  fillPercent: { min: 1, max: 100 },
  largestParcelOrder: { min: 1, max: 100 },
} as const;
export type ShippingSettingField = keyof typeof shippingSettingLimits;
export type ShippingSettings = Record<ShippingSettingField, number>;
export const shippingSettingFields = Object.keys(shippingSettingLimits) as ShippingSettingField[];
export function validShippingSetting(field: ShippingSettingField, value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= shippingSettingLimits[field].min && value <= shippingSettingLimits[field].max;
}
function checked(value: unknown): ShippingSettings {
  if (typeof value !== "object" || value === null || Array.isArray(value) || !shippingSettingFields.every(field => validShippingSetting(field, (value as Record<string, unknown>)[field]))) throw new HttpError("invalid-response", 200);
  const fields = value as ShippingSettings;
  return { packagingWeightGrams: fields.packagingWeightGrams, fillPercent: fields.fillPercent, largestParcelOrder: fields.largestParcelOrder };
}
const path = (storeId: string) => "/api/admin/stores/" + encodeURIComponent(storeId) + "/shipping-settings";
export const shippingSettingsApi = {
  read: async (storeId: string, signal?: AbortSignal) => {
    const value = await requestJson<unknown>(path(storeId), { signal }); signal?.throwIfAborted(); return checked(value);
  },
  save: async (storeId: string, values: ShippingSettings, signal?: AbortSignal) => {
    const input = checked(values);
    const value = await requestJson<unknown>(path(storeId), { method: "PUT", body: input, signal }); signal?.throwIfAborted();
    const result = checked(value);
    if (shippingSettingFields.some(field => input[field] !== result[field])) throw new HttpError("invalid-response", 200);
    return result;
  },
};
export type ShippingSettingsDraft = Record<ShippingSettingField, string>;
export function shippingSettingsDraft(values: ShippingSettings): ShippingSettingsDraft {
  return { packagingWeightGrams: String(values.packagingWeightGrams), fillPercent: String(values.fillPercent), largestParcelOrder: String(values.largestParcelOrder) };
}
export function parseShippingSetting(field: ShippingSettingField, raw: string): number | null {
  const value = raw.trim(); if (!/^\d+$/.test(value)) return null;
  const parsed = Number(value); return validShippingSetting(field, parsed) ? parsed : null;
}
