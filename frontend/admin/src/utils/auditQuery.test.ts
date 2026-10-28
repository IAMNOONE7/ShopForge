import { expect, it } from "vitest";
import { auditInstant, auditParameters, auditQuery, checkedAuditDraft, maxAuditPage } from "./auditQuery";
import { auditStore, defaultAuditQuery } from "../test/tenantAuditFixtures";

it("keeps exact actions, encoded offsets and 100-nanosecond bounds in shareable filters", () => {
  const query = { ...defaultAuditQuery, storeId: auditStore.id, action: "future.action <literal>", from: "2026-10-28T10:30:00.1234567+02:00", to: "2026-10-28T08:30:00.1234568Z", page: 2, pageSize: 1 };
  const parameters = auditParameters(query); expect(parameters.toString()).toContain("%2B02%3A00"); expect(auditQuery(parameters)).toMatchObject({ invalidUrl: false, query }); expect(auditInstant(query.to)! - auditInstant(query.from)!).toBe(1n);
});
it.each(["2026-02-30T12:00:00Z", "2025-02-29T00:00Z", "0000-01-01T00:00Z", "2026-13-01T00:00Z", "2026-01-01T24:00Z", "2026-01-01T12:60Z", "2026-01-01T12:00:60Z", "2026-01-01T12:00:00.12345678Z", "2026-01-01T12:00:00", "2026-01-01", "2026-01-01T12:00+14:01", "2026-01-01T12:00+15:00", "0001-01-01T00:00+01:00", "9999-12-31T23:59-01:00"])("rejects ambiguous, impossible or out-of-contract time %s", value => { expect(auditInstant(value)).toBeNull(); });
it.each(["2024-02-29T12:00Z", "0001-01-01T00:00:00Z", "9999-12-31T23:59:59.9999999Z", "2026-10-28t10:00:00.1z", "2026-10-28T12:00+14:00", "2026-10-28T12:00-14:00"])("accepts an explicit valid time %s", value => { expect(auditInstant(value)).not.toBeNull(); });
it.each(["page=0", "page=-1", "page=2147483648&pageSize=1", "page=99999999999", "page=1e2", "pageSize=0", "pageSize=101", "storeId=foreign-name", "from=not-a-date", "to=2026-02-30T00%3A00Z", "action=" + "a".repeat(61)])("does not broaden an invalid link: %s", input => { expect(auditQuery(new URLSearchParams(input)).query).toBeNull(); });
it.each(["action=a&action=b", "page=1&page=2", "pageSize=50&pageSize=50", "tenantId=other-company", "actor=other-person", "q=anything"])("requires review for unsupported or repeated parameters: %s", input => { expect(auditQuery(new URLSearchParams(input))).toMatchObject({ query: null, invalidUrl: true }); });
it("bounds offsets and page binders without claiming a total or silently clamping page size", () => {
  for (const size of [1, 2, 25, 50, 100]) { const max = maxAuditPage(size); expect((max - 1) * size).toBeLessThanOrEqual(2_147_483_647); expect(max).toBeLessThanOrEqual(2_147_483_647); expect(auditQuery(new URLSearchParams(`page=${max}&pageSize=${size}`)).query?.page).toBe(max); expect(auditQuery(new URLSearchParams(`page=${max + 1}&pageSize=${size}`)).query).toBeNull(); }
});
it("requires a strictly later end, preserving time precision and explicit empty/default filters", () => {
  const draft = { storeId: "", action: "", from: "2026-10-28T08:00:00Z", to: "2026-10-28T09:00:00+01:00", pageSize: "50" }; expect(checkedAuditDraft(draft).issues).toEqual(["range"]); expect(auditParameters(defaultAuditQuery).toString()).toBe(""); expect(auditQuery(new URLSearchParams()).query).toEqual(defaultAuditQuery);
});
