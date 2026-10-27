import type { AdminOrderParcel } from "../api";

const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const positiveInteger = (value: unknown): value is number => typeof value === "number" && Number.isInteger(value) && value > 0 && value <= 2_147_483_647;
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const variantId = (value: unknown): value is string => typeof value === "string" && /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(value) && !/^0{8}-(?:0{4}-){3}0{12}$/.test(value);

function parcel(value: unknown): value is AdminOrderParcel {
  return object(value) && positiveInteger(value.number) && positiveInteger(value.grossWeightGrams) &&
    positiveInteger(value.bandUpToGrams) && value.grossWeightGrams <= value.bandUpToGrams &&
    typeof value.price === "number" && Number.isFinite(value.price) && value.price >= 0 && typeof value.alone === "boolean" &&
    Array.isArray(value.contents) && value.contents.length > 0 && value.contents.every(item =>
      object(item) && variantId(item.variantId) && text(item.sku) && positiveInteger(item.quantity)) &&
    new Set(value.contents.map(item => item.variantId.toLowerCase())).size === value.contents.length;
}

export function checkedOrderParcels(value: unknown): AdminOrderParcel[] | null {
  if (!Array.isArray(value) || !value.every(parcel) || value.some((item, index) => index > 0 && item.number <= value[index - 1].number)) return null;
  return value.map(item => ({ number: item.number, grossWeightGrams: item.grossWeightGrams, bandUpToGrams: item.bandUpToGrams,
    price: item.price, alone: item.alone, contents: item.contents.map(content => ({ variantId: content.variantId, sku: content.sku, quantity: content.quantity })) }));
}
