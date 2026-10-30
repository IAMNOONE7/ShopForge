export type FreeShipping = { threshold: number; remaining: number };

export function validFreeShipping(value: unknown): value is FreeShipping | null | undefined {
  if (value === null || value === undefined) return true;
  if (typeof value !== "object" || Array.isArray(value)) return false;
  const { threshold, remaining } = value as Record<string, unknown>;
  return typeof threshold === "number" && Number.isFinite(threshold) && threshold > 0 &&
    typeof remaining === "number" && Number.isFinite(remaining) && remaining >= 0 && remaining <= threshold;
}
