import { HttpError, isHttpError, requestJson } from "./http";

export type Consent = { purpose: string; isGranted: boolean; statement: string; decidedAt: string | null };
const invalid = () => new HttpError("invalid-response", 200);
function consent(value: unknown): Consent {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw invalid();
  const row = value as Record<string, unknown>;
  if (typeof row.purpose !== "string" || !row.purpose.trim() || typeof row.isGranted !== "boolean" ||
    typeof row.statement !== "string" || !row.statement.trim() ||
    !(row.decidedAt === null || typeof row.decidedAt === "string" && Number.isFinite(Date.parse(row.decidedAt))) ||
    row.isGranted && row.decidedAt === null) throw invalid();
  return { purpose: row.purpose, isGranted: row.isGranted, statement: row.statement, decidedAt: row.decidedAt };
}
async function read(path: string, signal: AbortSignal, body?: { isGranted: boolean }): Promise<unknown> {
  try {
    const value = await requestJson<unknown>(`/api/storefront/account/consents${path}`, { method: body === undefined ? "GET" : "PUT", body, signal, cache: "no-store", notifyUnauthorized: false });
    signal.throwIfAborted(); return value;
  } catch (error) {
    if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter);
    throw error;
  }
}
export const consentsApi = {
  async list(signal: AbortSignal): Promise<Consent[]> {
    const value = await read("", signal);
    if (!Array.isArray(value)) throw invalid();
    const rows = value.map(consent);
    if (new Set(rows.map(row => row.purpose)).size !== rows.length) throw invalid();
    return rows;
  },
  async setMarketing(isGranted: boolean, signal: AbortSignal): Promise<Consent> {
    const row = consent(await read("/marketing", signal, { isGranted }));
    if (row.purpose !== "Marketing" || row.isGranted !== isGranted || row.decidedAt === null) throw invalid();
    return row;
  },
};
