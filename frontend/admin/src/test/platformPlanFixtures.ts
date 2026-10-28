import type { PlatformUser } from "../api/platform";
import type { PlatformPlan } from "../api/platformPlans";

export const plansOperator: PlatformUser = { id: "00000000-0000-0000-0000-000000000001", email: "operator@example.test", isTwoFactorEnabled: true };
export const platformPlan: PlatformPlan = { id: "abc00000-0000-0000-0000-000000000002", code: "scale", name: "Scale", maxStores: null, maxProducts: 0, isDefault: true };
export const newPlatformPlan: PlatformPlan = { id: "abc00000-0000-0000-0000-000000000003", code: "growth", name: "Growth", maxStores: 5, maxProducts: null, isDefault: false };
