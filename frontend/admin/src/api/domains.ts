import { HttpError, requestJson } from "./http";

export type StoreDomain = {
  id: string;
  hostName: string;
  isPrimary: boolean;
  isVerified: boolean;
  verifiedAt: string | null;
  challengeName: string | null;
  challengeValue: string | null;
  claimExpiresAt: string | null;
};

function text(value: unknown): value is string { return typeof value === "string" && value.trim().length > 0; }
function date(value: unknown) { return value === null || (typeof value === "string" && Number.isFinite(Date.parse(value))); }
function domain(value: unknown): value is StoreDomain {
  if (typeof value !== "object" || value === null) return false;
  const item = value as Record<string, unknown>;
  return text(item.id) && text(item.hostName) && typeof item.isPrimary === "boolean" && typeof item.isVerified === "boolean"
    && date(item.verifiedAt) && date(item.claimExpiresAt)
    && (item.challengeName === null || text(item.challengeName)) && (item.challengeValue === null || text(item.challengeValue))
    && (item.isVerified || (text(item.challengeName) && text(item.challengeValue)));
}
const root = (storeId: string) => `/api/admin/stores/${encodeURIComponent(storeId)}/domains`;
function checked(value: unknown, status: number): StoreDomain {
  if (!domain(value)) throw new HttpError("invalid-response", status);
  return value;
}

export async function getStoreDomains(storeId: string, signal?: AbortSignal): Promise<StoreDomain[]> {
  const value = await requestJson<unknown>(root(storeId), { signal });
  if (!Array.isArray(value) || !value.every(domain) || new Set(value.map((item) => item.id)).size !== value.length) {
    throw new HttpError("invalid-response", 200);
  }
  return value;
}
export async function addStoreDomain(storeId: string, hostName: string) {
  const value = checked(await requestJson<unknown>(root(storeId), { method: "POST", body: { hostName } }), 201);
  if (value.hostName !== hostName) throw new HttpError("invalid-response", 201);
  return value;
}
export async function verifyStoreDomain(storeId: string, domainId: string) {
  const value = checked(await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(domainId)}/verify`, { method: "POST" }), 200);
  if (value.id !== domainId || !value.isVerified) throw new HttpError("invalid-response", 200);
  return value;
}
export async function makeStoreDomainPrimary(storeId: string, domainId: string) {
  const value = checked(await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(domainId)}/primary`, { method: "POST" }), 200);
  if (value.id !== domainId || !value.isVerified || !value.isPrimary) throw new HttpError("invalid-response", 200);
  return value;
}
export async function removeStoreDomain(storeId: string, domainId: string) {
  const value = await requestJson<unknown>(`${root(storeId)}/${encodeURIComponent(domainId)}`, { method: "DELETE" });
  if (value !== undefined) throw new HttpError("invalid-response", 200);
}
