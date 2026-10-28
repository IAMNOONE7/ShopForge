import type { AdminStore } from "../api";
import type { AuditRecord } from "../api/tenantAudit";
import type { AuditQuery } from "../utils/auditQuery";

export const auditStaff = { id: "owner-a", email: "owner@example.test", role: "Owner", tenantId: "company-a" };
export const auditStore: AdminStore = { id: "aaaaaaaa-1111-2222-3333-444444444444", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "draft", company: null, logoUrl: null, primaryHostName: null, returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 } };
export const defaultAuditQuery: AuditQuery = { storeId: "", action: "", from: "", to: "", page: 1, pageSize: 50 };
export const auditRows: AuditRecord[] = [
  { id: "11111111-1111-2222-3333-444444444444", recordedAt: "2026-10-28T08:30:00.1234567Z", actorKind: "TenantUser", actorId: "bbbbbbbb-1111-2222-3333-444444444444", actorName: "former-name@example.test", storeId: auditStore.id, action: "order.refunded", subject: "202600042", details: '{"credited":777.00,"reason":"[redacted]"}', ipAddress: "192.0.2.10" },
  { id: "22222222-1111-2222-3333-444444444444", recordedAt: "2026-10-27T09:00:00+01:00", actorKind: "PlatformUser", actorId: null, actorName: "operator@example.test", storeId: null, action: "tenant.resumed", subject: "Company A", details: null, ipAddress: null },
  { id: "33333333-1111-2222-3333-444444444444", recordedAt: "2026-10-26T08:00:00Z", actorKind: "System", actorId: null, actorName: null, storeId: null, action: "future.action", subject: "<literal subject>", details: '{"partial":"<script>literal</script>', ipAddress: null },
];
