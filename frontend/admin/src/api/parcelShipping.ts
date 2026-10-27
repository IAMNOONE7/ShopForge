import type { ShippingMethod } from "../api";
import { checkedShippingMethod } from "./shippingMethods";
import { HttpError, requestJson } from "./http";
import { bulkPrice } from "../utils/bulkListings";

export type RateSource = "carrier" | "own" | "placeholder";
export type RateCard = { source: RateSource; pricedOn: string | null; bands: { upToGrams: number; price: number }[] };
export type Size = { lengthMm: number; widthMm: number; heightMm: number };
export type CarrierService = { key: string; providerKey: string; name: string; kind: "locker" | "pickupPoint" | "home"; country: string; isActive: boolean; maxWeightGrams: number; largest: Size; smallest: Size; maxSumOfSidesMm: number | null; largestVolumeMm3: number; maxParcels: number | null; source: string; sourceDate: string };
export type QuoteLine = { storeProductId: string; variantId: string; quantity: number };
export type QuoteInput = { country: string | null; lines: QuoteLine[] };
export const quoteReasons = ["notServed", "tooHeavy", "missingWeight", "missingSize", "itemTooLarge", "itemTooHeavy", "tooManyParcels", "aboveHighestBand", "noRates", "serviceUnavailable", "carrierRefused"] as const;
export type Quote = { isAvailable: boolean; currency: string; total: number; parcels: { contents: { variantId: string; sku: string; quantity: number }[]; grossWeightGrams: number; bandUpToGrams: number; price: number; alone: boolean }[]; unavailable: { reason: typeof quoteReasons[number]; detail: string; variantId: string | null } | null };
export type QuoteForm = { id: string; sku: string; optionValues: string[] };
export const uuid = (value: unknown): value is string => typeof value === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === "object" && !Array.isArray(value);
const positive = (value: unknown): value is number => typeof value === "number" && Number.isSafeInteger(value) && value > 0;
const text = (value: unknown): value is string => typeof value === "string" && !!value.trim();
const nullablePositive = (value: unknown) => value === null || positive(value);
export function calendarDate(value: string) { return /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value; }
const invalid = () => new HttpError("invalid-response", 200);
const root = (storeId: string) => "/api/admin/stores/" + encodeURIComponent(storeId);
const methodPath = (storeId: string, code: string) => root(storeId) + "/shipping-methods/" + encodeURIComponent(code);
export function checkedQuote(value: unknown, currency: string, input: QuoteInput, perParcel: boolean): Quote {
  const amount = (value: unknown): value is number => typeof value === "number" && bulkPrice(String(value), currency) === value;
  if (!record(value) || typeof value.isAvailable !== "boolean" || value.currency !== currency || !amount(value.total) || !Array.isArray(value.parcels)) throw invalid();
  if (!value.isAvailable) {
    const refusedVariant = record(value.unavailable) ? value.unavailable.variantId : undefined;
    if (!record(value.unavailable) || !quoteReasons.includes(value.unavailable.reason as typeof quoteReasons[number]) || !text(value.unavailable.detail)
      || value.unavailable.variantId !== null && !input.lines.some(line => line.variantId === refusedVariant)
      || value.total !== 0 || value.parcels.length) throw invalid();
  } else {
    if (value.unavailable !== null || perParcel && !value.parcels.length || !perParcel && value.parcels.length) throw invalid();
    const wanted = new Map(input.lines.map(line => [line.variantId, line.quantity])), actual = new Map<string, number>();
    let total = 0;
    for (const parcel of value.parcels) {
      if (!record(parcel) || !positive(parcel.grossWeightGrams) || !positive(parcel.bandUpToGrams) || parcel.grossWeightGrams > parcel.bandUpToGrams || !amount(parcel.price) || typeof parcel.alone !== "boolean" || !Array.isArray(parcel.contents) || !parcel.contents.length) throw invalid();
      total += parcel.price;
      for (const item of parcel.contents) {
        if (!record(item) || !uuid(item.variantId) || !wanted.has(item.variantId) || !text(item.sku) || !positive(item.quantity)) throw invalid();
        actual.set(item.variantId, (actual.get(item.variantId) ?? 0) + item.quantity);
      }
    }
    if (perParcel && ([...wanted].some(([id, count]) => actual.get(id) !== count) || Math.abs(total - value.total) > 0.000001)) throw invalid();
  }
  return value as unknown as Quote;
}
export const parcelShippingApi = {
  services: async (storeId: string, signal?: AbortSignal): Promise<CarrierService[]> => {
    const value = await requestJson<unknown>(root(storeId) + "/carrier-services", { signal }); signal?.throwIfAborted();
    const size = (value: unknown) => record(value) && [value.lengthMm, value.widthMm, value.heightMm].every(positive);
    if (!Array.isArray(value) || !value.every(item => record(item) && [item.key, item.providerKey, item.name, item.source].every(text) && ["locker", "pickupPoint", "home"].includes(String(item.kind)) && typeof item.country === "string" && /^[A-Z]{2}$/.test(item.country) && typeof item.isActive === "boolean" && positive(item.maxWeightGrams) && size(item.largest) && size(item.smallest) && nullablePositive(item.maxSumOfSidesMm) && positive(item.largestVolumeMm3) && nullablePositive(item.maxParcels) && typeof item.sourceDate === "string" && calendarDate(item.sourceDate)) || new Set(value.map(item => item.key)).size !== value.length) throw invalid();
    return value;
  },
  rates: async (storeId: string, method: ShippingMethod, input: RateCard, signal?: AbortSignal) => {
    const value = checkedShippingMethod(await requestJson<unknown>(methodPath(storeId, method.code) + "/rates", { method: "PUT", body: input, signal })); signal?.throwIfAborted();
    const { ratesFrom: _source, ratesPricedOn: _date, rateBands: _bands, ...rest } = value;
    const { ratesFrom: _oldSource, ratesPricedOn: _oldDate, rateBands: _oldBands, ...original } = method;
    if (value.pricing !== "perParcel" || value.ratesFrom !== input.source || value.ratesPricedOn !== input.pricedOn || JSON.stringify(value.rateBands) !== JSON.stringify(input.bands)
      || Object.keys(original).some(key => JSON.stringify(rest[key as keyof typeof rest]) !== JSON.stringify(original[key as keyof typeof original]))) throw invalid();
    return value;
  },
  quote: async (storeId: string, method: ShippingMethod, currency: string, input: QuoteInput, signal?: AbortSignal) => {
    const value = await requestJson<unknown>(methodPath(storeId, method.code) + "/quote", { method: "POST", body: input, signal }); signal?.throwIfAborted();
    return checkedQuote(value, currency, input, method.pricing === "perParcel");
  },
  forms: async (signal?: AbortSignal): Promise<{ id: string; variants: QuoteForm[] }[]> => {
    const value = await requestJson<unknown>("/api/admin/products", { signal }); signal?.throwIfAborted();
    if (!Array.isArray(value) || !value.every(item => record(item) && uuid(item.id) && Array.isArray(item.variants) && item.variants.every((form: unknown) => record(form) && uuid(form.id) && text(form.sku) && Array.isArray(form.optionValues) && form.optionValues.every(value => typeof value === "string")))) throw invalid();
    return value.map(item => ({ id: item.id, variants: item.variants.map((form: QuoteForm) => ({ id: form.id, sku: form.sku, optionValues: form.optionValues })) }));
  },
};
