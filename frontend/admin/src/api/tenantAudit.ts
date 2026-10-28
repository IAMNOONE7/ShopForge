import { HttpError, isHttpError, requestJson } from "./http";
import { staffAccountApi } from "./staffAccount";
import { auditGuid, auditInstant, auditParameters, checkedAuditDraft, type AuditQuery } from "../utils/auditQuery";

export type AuditRecord = { id: string; recordedAt: string; actorKind: string; actorId: string | null; actorName: string | null; storeId: string | null; action: string; subject: string; details: string | null; ipAddress: string | null };
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const nullable = (value: unknown): value is string | null => value === null || typeof value === "string";
const guid = (value: unknown): value is string => typeof value === "string" && auditGuid.test(value);
const invalid = () => new HttpError("invalid-response", 200);

export const tenantAuditApi = {
  session: staffAccountApi.session,
  async read(input: AuditQuery, signal: AbortSignal): Promise<AuditRecord[]> {
    const query = checkedAuditDraft({ ...input, pageSize: String(input.pageSize) }, String(input.page)).query;
    if (!query) throw new TypeError("Invalid audit filters.");
    const parameters = auditParameters(query);
    let value: unknown;
    try { value = await requestJson<unknown>(`/api/admin/audit${parameters.size ? `?${parameters}` : ""}`, { signal, cache: "no-store", expectedStatus: 200, notifyUnauthorized: false }); }
    catch (error) { if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter); throw error; }
    signal.throwIfAborted();
    if (!Array.isArray(value) || value.length > query.pageSize) throw invalid();
    const rows = value.map((row: unknown): AuditRecord => {
      if (!object(row) || !guid(row.id) || !text(row.recordedAt) || auditInstant(row.recordedAt) === null || !text(row.actorKind) || !nullable(row.actorId) || row.actorId !== null && !guid(row.actorId) || !nullable(row.actorName) || !nullable(row.storeId) || row.storeId !== null && !guid(row.storeId) || !text(row.action) || !text(row.subject) || !nullable(row.details) || !nullable(row.ipAddress)) throw invalid();
      if (query.storeId && row.storeId?.toLowerCase() !== query.storeId.toLowerCase() || query.action && row.action !== query.action || query.from && auditInstant(row.recordedAt)! < auditInstant(query.from)! || query.to && auditInstant(row.recordedAt)! >= auditInstant(query.to)!) throw invalid();
      return { id: row.id, recordedAt: row.recordedAt, actorKind: row.actorKind, actorId: row.actorId, actorName: row.actorName, storeId: row.storeId, action: row.action, subject: row.subject, details: row.details, ipAddress: row.ipAddress };
    });
    if (new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length) throw invalid();
    return rows;
  },
};
