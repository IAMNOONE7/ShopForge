import { HttpError, requestJson } from "./http";
import { isSetupId } from "./setupProgress";

export type DomainDnsRecord = { type: string; name: string; value: string; purpose: string };
export type DomainEdge = { state: string; operationId: string | null; recordsByPlatform: boolean; validation: string | null; records: DomainDnsRecord[] };
export type DomainZone = { name: string; state: string; nameServers: string[]; operationId: string | null };
export const edgeStates = ["unproved", "waiting", "binding", "bound", "unbinding"] as const;
export const zoneStates = ["waiting", "active"] as const;

export type StoreDomain = {
  id: string;
  hostName: string;
  isPrimary: boolean;
  isVerified: boolean;
  verifiedAt: string | null;
  challengeName: string | null;
  challengeValue: string | null;
  claimExpiresAt: string | null;
  edge: DomainEdge | null;
  zone: DomainZone | null;
};

function text(value: unknown): value is string { return typeof value === "string" && value.trim().length > 0; }
function date(value: unknown) { return value === null || (typeof value === "string" && Number.isFinite(Date.parse(value))); }
function record(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function operationId(value: unknown): value is string | null { return value === null || typeof value === "string" && isSetupId(value); }
function edge(value: unknown): value is DomainEdge | null {
  return value === null || record(value) && text(value.state) && operationId(value.operationId) && typeof value.recordsByPlatform === "boolean"
    && (value.validation === null || typeof value.validation === "string") && Array.isArray(value.records)
    && value.records.every(item => record(item) && typeof item.type === "string" && typeof item.name === "string" && typeof item.value === "string" && text(item.purpose));
}
function zone(value: unknown): value is DomainZone | null {
  return value === null || record(value) && text(value.name) && text(value.state) && operationId(value.operationId)
    && Array.isArray(value.nameServers) && value.nameServers.every(text);
}
function domain(value: unknown): value is StoreDomain {
  if (!record(value)) return false;
  const item = value;
  return text(item.id) && text(item.hostName) && typeof item.isPrimary === "boolean" && typeof item.isVerified === "boolean"
    && date(item.verifiedAt) && date(item.claimExpiresAt)
    && (item.challengeName === null || text(item.challengeName)) && (item.challengeValue === null || text(item.challengeValue))
    && (item.isVerified || (text(item.challengeName) && text(item.challengeValue))) && edge(item.edge) && zone(item.zone);
}
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/domains`;
function checked(value: unknown, status: number): StoreDomain {
  if (!domain(value)) throw new HttpError("invalid-response", status);
  return { id: value.id, hostName: value.hostName, isPrimary: value.isPrimary, isVerified: value.isVerified, verifiedAt: value.verifiedAt,
    challengeName: value.challengeName, challengeValue: value.challengeValue, claimExpiresAt: value.claimExpiresAt,
    edge: value.edge === null ? null : { state: value.edge.state, operationId: value.edge.operationId, recordsByPlatform: value.edge.recordsByPlatform, validation: value.edge.validation,
      records: value.edge.records.map(item => ({ type: item.type, name: item.name, value: item.value, purpose: item.purpose })) },
    zone: value.zone === null ? null : { name: value.zone.name, state: value.zone.state, nameServers: [...value.zone.nameServers], operationId: value.zone.operationId } };
}

export async function getStoreDomains(storeId: string, signal?: AbortSignal): Promise<StoreDomain[]> {
  const value = await requestJson<unknown>(root(storeId), { signal });
  signal?.throwIfAborted();
  if (!Array.isArray(value) || !value.every(domain) || new Set(value.map((item) => item.id)).size !== value.length) {
    throw new HttpError("invalid-response", 200);
  }
  return value.map(item => checked(item, 200));
}
export async function addStoreDomain(storeId: string, hostName: string, signal?: AbortSignal) {
  const raw = await requestJson<unknown>(root(storeId), { method: "POST", body: { hostName }, signal }); signal?.throwIfAborted();
  const value = checked(raw, 201);
  if (value.hostName !== hostName) throw new HttpError("invalid-response", 201);
  return value;
}
export async function verifyStoreDomain(storeId: string, domainId: string, signal?: AbortSignal) {
  const raw = await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(domainId)}/verify`, { method: "POST", signal }); signal?.throwIfAborted();
  const value = checked(raw, 200);
  if (value.id !== domainId || !value.isVerified) throw new HttpError("invalid-response", 200);
  return value;
}
export async function makeStoreDomainPrimary(storeId: string, domainId: string, signal?: AbortSignal) {
  const raw = await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(domainId)}/primary`, { method: "POST", signal }); signal?.throwIfAborted();
  const value = checked(raw, 200);
  if (value.id !== domainId || !value.isVerified || !value.isPrimary) throw new HttpError("invalid-response", 200);
  return value;
}
export async function removeStoreDomain(storeId: string, domainId: string, signal?: AbortSignal) {
  const value = await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(domainId)}`, { method: "DELETE", signal }); signal?.throwIfAborted();
  if (value !== undefined) throw new HttpError("invalid-response", 200);
}
export async function startStoreDomainZone(storeId: string, domainId: string, zone: string | null, signal?: AbortSignal) {
  const value = await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(domainId)}/zone`, { method: "POST", body: { zone }, signal });
  signal?.throwIfAborted();
  if (!record(value) || typeof value.operationId !== "string" || !isSetupId(value.operationId)) throw new HttpError("invalid-response", 202);
  return { operationId: value.operationId };
}
export function domainNeedsRefresh(item: StoreDomain) {
  return item.edge !== null && (["waiting", "binding", "unbinding"].includes(item.edge.state)
    || item.edge.state === "bound" && ["pendingRevalidation", "refreshingValidationToken", "submitting", "pending"].includes(item.edge.validation ?? ""))
    || item.zone?.state === "waiting";
}
