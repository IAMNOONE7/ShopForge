import { HttpError, requestJson } from "./http";
import { isSetupId } from "./setupProgress";

export type EmailDnsRecord = { purpose: string; type: string; name: string | null; value: string; priority: string | null; valid: string | null };
export type SendingDomain = {
  domain: string; state: string; senderAddress: string; replyTo: string | null; region: string;
  operationId: string | null; verifiedAt: string | null; records: EmailDnsRecord[]; recommended: EmailDnsRecord[];
};
export type StoreEmail = { sendsFromOwnDomain: boolean; platformAddress: string; domain: SendingDomain | null };
export type ConnectEmailDomain = { domain: string; senderAddress: string; replyTo?: string };
export const emailStates = ["connecting", "awaitingDns", "active", "removing"] as const;
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/email`;
const invalid = (status = 200) => new HttpError("invalid-response", status);
const text = (value: unknown): value is string => typeof value === "string";
const nonempty = (value: unknown): value is string => text(value) && value.trim().length > 0;
const nullableText = (value: unknown): value is string | null => value === null || text(value);
function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw invalid();
  return value as Record<string, unknown>;
}
function dns(value: unknown): EmailDnsRecord {
  const row = record(value);
  if (!nonempty(row.purpose) || !text(row.type) || !nullableText(row.name) || !text(row.value) || !nullableText(row.priority) || !nullableText(row.valid)) throw invalid();
  return { purpose: row.purpose, type: row.type, name: row.name, value: row.value, priority: row.priority, valid: row.valid };
}
function domain(value: unknown): SendingDomain {
  const row = record(value);
  if (!nonempty(row.domain) || !nonempty(row.state) || !nonempty(row.senderAddress) || !nullableText(row.replyTo) || !nonempty(row.region)
    || !(row.operationId === null || text(row.operationId) && isSetupId(row.operationId))
    || !(row.verifiedAt === null || text(row.verifiedAt) && /T.*(?:Z|[+-]\d{2}:\d{2})$/.test(row.verifiedAt) && Number.isFinite(Date.parse(row.verifiedAt)))
    || !Array.isArray(row.records) || !Array.isArray(row.recommended)) throw invalid();
  return { domain: row.domain, state: row.state, senderAddress: row.senderAddress, replyTo: row.replyTo, region: row.region,
    operationId: row.operationId, verifiedAt: row.verifiedAt, records: row.records.map(dns), recommended: row.recommended.map(dns) };
}
function started(value: unknown) {
  if (!value || typeof value !== "object" || !("operationId" in value) || !text(value.operationId) || !isSetupId(value.operationId)) throw invalid(202);
  return { operationId: value.operationId };
}
export const storeEmailApi = {
  get: async (storeId: string, signal?: AbortSignal): Promise<StoreEmail> => {
    const row = record(await requestJson<unknown>(root(storeId), { signal })); signal?.throwIfAborted();
    if (typeof row.sendsFromOwnDomain !== "boolean" || !text(row.platformAddress) || row.domain === undefined) throw invalid();
    const item = row.domain === null ? null : domain(row.domain);
    if (row.sendsFromOwnDomain && item === null) throw invalid();
    return { sendsFromOwnDomain: row.sendsFromOwnDomain, platformAddress: row.platformAddress, domain: item };
  },
  connect: async (storeId: string, body: ConnectEmailDomain, signal?: AbortSignal) => {
    const value = await requestJson<unknown>(`${root(storeId)}/domain`, { method: "POST", body, signal }); signal?.throwIfAborted(); return started(value);
  },
  remove: async (storeId: string, signal?: AbortSignal) => {
    const value = await requestJson<unknown>(`${root(storeId)}/domain`, { method: "DELETE", signal }); signal?.throwIfAborted(); return started(value);
  },
};
