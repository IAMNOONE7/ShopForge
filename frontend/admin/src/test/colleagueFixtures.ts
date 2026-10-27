import type { CurrentUser } from "../api";
import type { Colleagues } from "../api/colleagues";
export const colleagueOwner: CurrentUser = { id: "00000000-0000-0000-0000-000000000001", tenantId: "00000000-0000-0000-0000-000000000010", role: "Owner", email: "owner@example.test" };
export const colleagueAdmin: CurrentUser = { ...colleagueOwner, id: "00000000-0000-0000-0000-000000000002", role: "Admin", email: "admin@example.test" };
export const colleagueTeam: Colleagues = {
  users: [
    { id: colleagueOwner.id, email: colleagueOwner.email, role: "Owner", isActive: true },
    { id: colleagueAdmin.id, email: colleagueAdmin.email, role: "Admin", isActive: true },
    { id: "00000000-0000-0000-0000-000000000003", email: "support@example.test", role: "Support", isActive: true },
    { id: "00000000-0000-0000-0000-000000000004", email: "warehouse@example.test", role: "Warehouse", isActive: false },
    { id: "00000000-0000-0000-0000-000000000005", email: "other-owner@example.test", role: "Owner", isActive: true },
  ],
  invitations: [
    { id: "00000000-0000-0000-0000-000000000006", email: "pending@example.test", role: "Support", expiresAt: "2026-11-03T12:00:00Z" },
    { id: "00000000-0000-0000-0000-000000000007", email: "pending-owner@example.test", role: "Owner", expiresAt: "2026-11-03T13:00:00Z" },
  ],
};
