import type { PlanUsage } from "../api/planUsage";

export const planStaff = { id: "staff-a", email: "staff@example.test", role: "Support", tenantId: "company-a" };
export const planUsageFixture: PlanUsage = {
  code: "starter", name: "Starter", maxStores: 2, maxProducts: 100,
  usage: [{ name: "listings", value: 156 }, { name: "products", value: 42 }, { name: "stores", value: 2 }, { name: "users", value: 6 }],
};
