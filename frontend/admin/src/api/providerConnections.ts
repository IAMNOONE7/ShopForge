import { HttpError, isHttpError, requestJson } from "./http";

export type ProviderConnection = {
  provider: string; merchantId: string; environment: string; isActive: boolean;
  hasSecret: boolean; publishableKey: string | null; changedAt: string;
};
export type ConnectionInput = { merchantId: string; environment: "Test" | "Live"; isActive: boolean; publishableKey: string | null };
export type ProviderChoices = { payment: string[]; shipping: string[] };
const fields = ["provider", "merchantId", "environment", "isActive", "publishableKey", "secret"];

// Provider failures must never carry an echoed credential, response body or trace value into the UI.
export function connectionFailure(error: unknown) {
  if (!isHttpError(error)) return new HttpError("network", null);
  const errors = Object.fromEntries(fields.filter(field => Object.keys(error.problem?.errors ?? {}).some(key => key.toLowerCase() === field.toLowerCase())).map(field => [field, [""]]));
  return new HttpError(error.kind, error.status, Object.keys(errors).length ? { errors } : null);
}
const invalid = () => new HttpError("invalid-response", null);
const keyValid = (value: unknown): value is string => typeof value === "string" && value.length > 0 && value.length <= 50 && value === value.trim().toLowerCase();
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/provider-connections`;
const itemPath = (storeId: string, provider: string) => `${root(storeId)}/${encodeURIComponent(provider)}`;

function checked(value: unknown): ProviderConnection {
  if (!value || typeof value !== "object") throw invalid();
  const row = value as ProviderConnection;
  if (!keyValid(row.provider) || typeof row.merchantId !== "string" || !row.merchantId.trim() || row.merchantId.length > 100
    || typeof row.environment !== "string" || !row.environment.trim() || typeof row.isActive !== "boolean" || typeof row.hasSecret !== "boolean"
    || row.publishableKey !== null && (typeof row.publishableKey !== "string" || row.publishableKey.length > 200)
    || typeof row.changedAt !== "string" || !/^\d{4}-\d{2}-\d{2}T/.test(row.changedAt) || !Number.isFinite(Date.parse(row.changedAt))) throw invalid();
  return { provider: row.provider, merchantId: row.merchantId, environment: row.environment, isActive: row.isActive,
    hasSecret: row.hasSecret, publishableKey: row.publishableKey, changedAt: row.changedAt };
}

export async function getProviderConnections(storeId: string, signal?: AbortSignal) {
  const result = await requestJson<unknown>(root(storeId), { signal });
  signal?.throwIfAborted();
  if (!Array.isArray(result)) throw invalid();
  const items = result.map(checked);
  if (new Set(items.map(item => item.provider)).size !== items.length) throw invalid();
  return items;
}

export async function getProviderChoices(storeId: string, signal?: AbortSignal): Promise<ProviderChoices> {
  const path = `/api/admin/stores/${encodeURIComponent(storeId)}`;
  const results = await Promise.all([requestJson<unknown>(`${path}/payment-providers`, { signal }), requestJson<unknown>(`${path}/shipping-providers`, { signal })]);
  signal?.throwIfAborted();
  for (const list of results) if (!Array.isArray(list) || !list.every(keyValid) || new Set(list).size !== list.length) throw invalid();
  return { payment: results[0] as string[], shipping: results[1] as string[] };
}

export async function saveProviderConnection(storeId: string, provider: string, input: ConnectionInput) {
  if (!keyValid(provider) || !input.merchantId.trim() || input.merchantId.trim().length > 100 || !["Test", "Live"].includes(input.environment)
    || typeof input.isActive !== "boolean" || input.publishableKey !== null && input.publishableKey.trim().length > 200) throw new RangeError("Invalid connection settings.");
  const body = { ...input, merchantId: input.merchantId.trim(), publishableKey: input.publishableKey?.trim() || null };
  try {
    const saved = checked(await requestJson<unknown>(itemPath(storeId, provider), { method: "PUT", body }));
    if (saved.provider !== provider || saved.merchantId !== body.merchantId || saved.environment !== body.environment || saved.isActive !== body.isActive || saved.publishableKey !== body.publishableKey) throw invalid();
    return saved;
  } catch (error) { throw connectionFailure(error); }
}

export async function setProviderSecret(storeId: string, provider: string, secret: string) {
  if (!keyValid(provider) || !secret.trim()) throw new RangeError("A credential is required.");
  try {
    const result = await requestJson<unknown>(`${itemPath(storeId, provider)}/secret`, { method: "PUT", body: { secret } });
    if (result !== undefined) throw invalid();
  } catch (error) { throw connectionFailure(error); }
}

export async function disconnectProvider(storeId: string, provider: string) {
  if (!keyValid(provider)) throw new RangeError("Invalid provider.");
  try {
    const result = await requestJson<unknown>(itemPath(storeId, provider), { method: "DELETE" });
    if (result !== undefined) throw invalid();
  } catch (error) { throw connectionFailure(error); }
}
