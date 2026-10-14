import type { StoreReadiness, StoreReadinessCheck } from "../api";
import { HttpError, isHttpError, requestJson } from "./http";

function isCheck(value: unknown): value is StoreReadinessCheck {
  return typeof value === "object" && value !== null
    && "check" in value && typeof value.check === "string" && value.check.trim().length > 0
    && "isReady" in value && typeof value.isReady === "boolean"
    && "problem" in value && (value.problem === null || typeof value.problem === "string")
    && "weight" in value && (value.weight === "required" || value.weight === "advised");
}

function checksOf(value: unknown): StoreReadinessCheck[] | null {
  return Array.isArray(value) && value.every(isCheck) ? value : null;
}

export async function getStoreReadiness(storeId: string, signal?: AbortSignal): Promise<StoreReadiness> {
  const value = await requestJson<unknown>(`/api/admin/stores/${storeId}/readiness`, { signal });
  if (typeof value !== "object" || value === null || !("checks" in value)
    || !("isReady" in value) || typeof value.isReady !== "boolean") {
    throw new HttpError("invalid-response", 200);
  }
  const checks = checksOf(value.checks);
  if (checks === null) throw new HttpError("invalid-response", 200);
  return { isReady: value.isReady, checks };
}

export function publicationRefusal(error: unknown) {
  if (!isHttpError(error) || error.status !== 409) return null;
  const checks = checksOf(error.problem?.readiness);
  const problems = Array.isArray(error.problem?.problems)
    ? error.problem.problems.filter((problem): problem is string => typeof problem === "string" && problem.trim().length > 0)
    : [];
  return checks !== null || problems.length > 0 ? { checks, problems } : null;
}
