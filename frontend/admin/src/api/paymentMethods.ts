import type { PaymentMethod } from "../api";
import { HttpError, requestJson } from "./http";

const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/payment-methods`;
function checked(value: unknown): PaymentMethod {
  if (!value || typeof value !== "object") throw new HttpError("invalid-response", null);
  const row = value as PaymentMethod;
  if (typeof row.code !== "string" || !row.code.trim() || typeof row.name !== "string" || !row.name.trim()
    || typeof row.providerKey !== "string" || !row.providerKey.trim() || typeof row.isActive !== "boolean") throw new HttpError("invalid-response", null);
  return { code: row.code, name: row.name, providerKey: row.providerKey, isActive: row.isActive };
}
export async function getPaymentMethods(storeId: string, signal?: AbortSignal) {
  const value = await requestJson<unknown>(root(storeId), { signal }); signal?.throwIfAborted();
  if (!Array.isArray(value)) throw new HttpError("invalid-response", null);
  const rows = value.map(checked);
  if (new Set(rows.map(row => row.code)).size !== rows.length) throw new HttpError("invalid-response", null);
  return rows;
}
export async function createPaymentMethod(storeId: string, name: string, providerKey: string) {
  const row = checked(await requestJson<unknown>(root(storeId), { method: "POST", body: { name, providerKey, isActive: true } }));
  if (row.name !== name.trim() || row.providerKey !== providerKey || !row.isActive) throw new HttpError("invalid-response", null);
  return row;
}
export async function updatePaymentMethod(storeId: string, code: string, input: { name: string; isActive: boolean }) {
  const row = checked(await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(code)}`, { method: "PUT", body: input }));
  if (row.code !== code || row.name !== input.name.trim() || row.isActive !== input.isActive) throw new HttpError("invalid-response", null);
  return row;
}
export async function getMethodProviders(storeId: string, kind: "payment" | "shipping", signal?: AbortSignal): Promise<string[]> {
  const value = await requestJson<unknown>(`/api/admin/stores/${encodeURIComponent(storeId)}/${kind}-providers`, { signal }); signal?.throwIfAborted();
  if (!Array.isArray(value) || !value.every(key => typeof key === "string" && key.trim().length > 0 && key === key.trim()) || new Set(value).size !== value.length) throw new HttpError("invalid-response", null);
  return value;
}
