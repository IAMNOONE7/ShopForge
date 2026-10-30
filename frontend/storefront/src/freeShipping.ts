export type FreeShipping = { threshold: number; remaining: number };

export function isFreeShippingThreshold(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value > 0;
}

export function validFreeShipping(value: unknown): value is FreeShipping | null | undefined {
  if (value === null || value === undefined) return true;
  if (typeof value !== "object" || Array.isArray(value)) return false;
  const { threshold, remaining } = value as Record<string, unknown>;
  return isFreeShippingThreshold(threshold) &&
    typeof remaining === "number" && Number.isFinite(remaining) && remaining >= 0 && remaining <= threshold;
}
