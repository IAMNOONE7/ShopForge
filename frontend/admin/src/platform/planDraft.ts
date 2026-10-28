import type { PlanField, PlanValues, PlatformPlan } from "../api/platformPlans";

export type PlanDraft = Record<PlanField, string>;
export function planDraft(plan: PlatformPlan | null): PlanDraft {
  return plan ? { code: plan.code, name: plan.name, maxStores: plan.maxStores?.toString() ?? "", maxProducts: plan.maxProducts?.toString() ?? "" } : { code: "", name: "", maxStores: "", maxProducts: "" };
}
export function reviewPlan(draft: PlanDraft, original: PlatformPlan | null): { values: PlanValues; issues: PlanField[] } {
  const code = original?.code ?? draft.code.trim().toLowerCase(), name = draft.name.trim();
  const parse = (value: string) => !value.trim() ? null : /^[0-9]+$/.test(value.trim()) && Number(value) <= 2147483647 ? Number(value) : NaN;
  const maxStores = parse(draft.maxStores), maxProducts = parse(draft.maxProducts);
  const issues: PlanField[] = [];
  if (!code || [...code].length > 40) issues.push("code");
  if (!name || name.length > 100) issues.push("name");
  if (Number.isNaN(maxStores)) issues.push("maxStores");
  if (Number.isNaN(maxProducts)) issues.push("maxProducts");
  return { values: { code, name, maxStores, maxProducts }, issues };
}
