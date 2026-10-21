import { HttpError, isHttpError, requestJson } from "./http";

export type PlatformUser = { id: string; email: string; isTwoFactorEnabled: boolean };
export type PlatformSignIn = { twoFactorRequired: true; ticket: string; user: null } | { twoFactorRequired: false; ticket: null; user: PlatformUser };
export type PlatformTenant = { id: string; name: string; status: string; planCode: string | null; usage: { name: string; value: number }[] };
export type TenantAction = "suspend" | "resume";
const root = "/api/platform";
const record = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const word = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const uuid = (value: unknown): value is string => typeof value === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
const invalid = () => new HttpError("invalid-response", null);
function user(value: unknown): PlatformUser {
  if (!record(value) || !uuid(value.id) || !word(value.email) || typeof value.isTwoFactorEnabled !== "boolean" || "tenantId" in value || "role" in value) throw invalid();
  return { id: value.id, email: value.email, isTwoFactorEnabled: value.isTwoFactorEnabled };
}
function signIn(value: unknown): PlatformSignIn {
  if (!record(value)) throw invalid();
  if (value.twoFactorRequired === true && word(value.ticket) && value.user === null) return { twoFactorRequired: true, ticket: value.ticket, user: null };
  if (value.twoFactorRequired === false && value.ticket === null) return { twoFactorRequired: false, ticket: null, user: user(value.user) };
  throw invalid();
}
function tenant(value: unknown): PlatformTenant {
  if (!record(value) || !uuid(value.id) || !word(value.name) || !word(value.status) || value.planCode !== null && !word(value.planCode)
    || !Array.isArray(value.usage) || !value.usage.every(item => record(item) && word(item.name) && Number.isSafeInteger(item.value) && Number(item.value) >= 0)) throw invalid();
  const usage = value.usage as PlatformTenant["usage"];
  if (new Set(usage.map(item => item.name)).size !== usage.length) throw invalid();
  return { id: value.id, name: value.name, status: value.status, planCode: value.planCode as string | null, usage: usage.map(item => ({ name: item.name, value: item.value })) };
}
// Authentication errors must never retain an echoed password, challenge ticket, code or response diagnostic.
function failure(error: unknown) {
  if (!isHttpError(error)) return new HttpError("network", null);
  return new HttpError(error.kind, error.status);
}
async function call(path: string, options: Parameters<typeof requestJson>[1] = {}) {
  try { return await requestJson<unknown>(root + path, { ...options, unauthorizedScope: "platform" }); }
  catch (error) { if (error instanceof DOMException && error.name === "AbortError") throw error; throw failure(error); }
}
export const platformApi = {
  me: async (signal?: AbortSignal) => { const value = await call("/auth/me", { signal, notifyUnauthorized: false }); signal?.throwIfAborted(); return user(value); },
  login: async (email: string, password: string) => signIn(await call("/auth/login", { method: "POST", body: { email, password }, notifyUnauthorized: false })),
  completeTwoFactor: async (ticket: string, code: string) => {
    const result = signIn(await call("/auth/two-factor", { method: "POST", body: { ticket, code }, notifyUnauthorized: false }));
    if (result.twoFactorRequired) throw invalid(); return result;
  },
  logout: async () => { if (await call("/auth/logout", { method: "POST", notifyUnauthorized: false }) !== undefined) throw invalid(); },
  tenants: async (signal?: AbortSignal) => {
    const value = await call("/tenants", { signal }); signal?.throwIfAborted(); if (!Array.isArray(value)) throw invalid();
    const items = value.map(tenant); if (new Set(items.map(item => item.id.toLowerCase())).size !== items.length) throw invalid(); return items;
  },
  changeTenant: async (id: string, action: TenantAction, signal?: AbortSignal) => {
    if (!uuid(id) || !["suspend", "resume"].includes(action)) throw new TypeError("A known tenant and action are required.");
    const value = tenant(await call(`/tenants/${encodeURIComponent(id)}/${action}`, { method: "POST", signal })); signal?.throwIfAborted();
    if (value.id.toLowerCase() !== id.toLowerCase() || value.status !== (action === "suspend" ? "Suspended" : "Active")) throw invalid(); return value;
  },
};
