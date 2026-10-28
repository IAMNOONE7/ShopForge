import { staffAccountApi } from "./staffAccount";
import { HttpError, isHttpError, requestJson } from "./http";

export type PlanResource = "stores" | "products";
export type UsageCount = { name: string; value: number };
export type PlanUsage = { code: string | null; name: string | null; maxStores: number | null; maxProducts: number | null; usage: UsageCount[] };
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const count = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const invalid = () => new HttpError("invalid-response", 200);

export const planUsageApi = {
  session: staffAccountApi.session,
  async read(signal: AbortSignal): Promise<PlanUsage> {
    let value: unknown;
    try { value = await requestJson<unknown>("/api/admin/plan", { signal, cache: "no-store", expectedStatus: 200, notifyUnauthorized: false }); }
    catch (error) {
      if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter);
      throw error;
    }
    signal.throwIfAborted();
    if (!object(value) || value.code !== null && !text(value.code) || value.name !== null && !text(value.name) || value.maxStores !== null && !count(value.maxStores) || value.maxProducts !== null && !count(value.maxProducts) || !Array.isArray(value.usage)) throw invalid();
    const usage = value.usage.map((row: unknown): UsageCount => {
      if (!object(row) || !text(row.name) || !count(row.value)) throw invalid();
      return { name: row.name, value: row.value };
    });
    if (new Set(usage.map(row => row.name)).size !== usage.length) throw invalid();
    return { code: value.code, name: value.name, maxStores: value.maxStores, maxProducts: value.maxProducts, usage };
  },
};

export function planLimitRefusal(error: unknown, resource: PlanResource) {
  return isHttpError(error) && error.kind === "http" && error.status === 409 && error.problem?.title === `This plan allows no more ${resource}`;
}
