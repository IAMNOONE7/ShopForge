import { HttpError, isHttpError, requestJson } from "./http";
import { operatorAccountApi } from "./operatorAccount";

export type PlatformPlan = { id: string; code: string; name: string; maxStores: number | null; maxProducts: number | null; isDefault: boolean };
export type PlanValues = Pick<PlatformPlan, "code" | "name" | "maxStores" | "maxProducts">;
export const planFields = ["code", "name", "maxStores", "maxProducts"] as const;
export type PlanField = typeof planFields[number];
const root = "/api/platform/plans";
const options = { cache: "no-store" as const, notifyUnauthorized: false, unauthorizedScope: "platform" as const };
const invalid = (status = 200) => new HttpError("invalid-response", status);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const guid = (value: unknown): value is string => typeof value === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
const cap = (value: unknown): value is number | null => value === null || Number.isInteger(value) && Number(value) >= 0 && Number(value) <= 2147483647;
function plan(value: unknown, status = 200): PlatformPlan {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw invalid(status);
  const row = value as Record<string, unknown>;
  if (!guid(row.id) || !text(row.code) || [...row.code].length > 40 || !text(row.name) || row.name.length > 100 || !cap(row.maxStores) || !cap(row.maxProducts) || typeof row.isDefault !== "boolean") throw invalid(status);
  return { id: row.id, code: row.code, name: row.name, maxStores: row.maxStores, maxProducts: row.maxProducts, isDefault: row.isDefault };
}
async function call(path: string, signal: AbortSignal, expectedStatus: number, method = "GET", body?: unknown) {
  try {
    const value = await requestJson<unknown>(path, { ...options, signal, expectedStatus, method, body });
    signal.throwIfAborted(); return value;
  } catch (error) {
    if (!isHttpError(error)) throw error;
    const keys = error.status === 400 ? planFields.filter(field => Object.hasOwn(error.problem?.errors ?? {}, field)) : [];
    throw new HttpError(error.kind, error.status, keys.length ? { errors: Object.fromEntries(keys.map(key => [key, []])) } : null, null, error.retryAfter);
  }
}
function matches(plan: PlatformPlan, values: PlanValues) {
  return plan.code === values.code && plan.name === values.name && plan.maxStores === values.maxStores && plan.maxProducts === values.maxProducts;
}
export function samePlan(expected: PlatformPlan, actual: PlatformPlan) {
  return expected.id.toLowerCase() === actual.id.toLowerCase() && expected.isDefault === actual.isDefault && matches(expected, actual);
}
export const platformPlansApi = {
  session: operatorAccountApi.session,
  async list(signal: AbortSignal) {
    const value = await call(root, signal, 200);
    if (!Array.isArray(value)) throw invalid();
    const rows = value.map(row => plan(row));
    if (new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length || new Set(rows.map(row => row.code)).size !== rows.length || rows.filter(row => row.isDefault).length > 1) throw invalid();
    return rows;
  },
  async create(values: PlanValues, signal: AbortSignal) {
    const { code, name, maxStores, maxProducts } = values;
    const row = plan(await call(root, signal, 201, "POST", { code, name, maxStores, maxProducts }), 201);
    if (!matches(row, values) || row.isDefault) throw invalid(201);
    return row;
  },
  async update(original: PlatformPlan, values: PlanValues, signal: AbortSignal) {
    if (!guid(original.id) || values.code !== original.code) throw new TypeError("A saved plan and its unchanged code are required.");
    const { name, maxStores, maxProducts } = values;
    const row = plan(await call(`${root}/${encodeURIComponent(original.id)}`, signal, 200, "PUT", { name, maxStores, maxProducts }));
    if (row.id.toLowerCase() !== original.id.toLowerCase() || row.isDefault !== original.isDefault || !matches(row, values)) throw invalid();
    return row;
  },
};
