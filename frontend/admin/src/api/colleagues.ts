import type { CurrentUser } from "../api";
import { knownRole, type AdminRole } from "../session";
import { HttpError, isHttpError, requestJson, requestNoContent } from "./http";

export type ColleagueRole = Exclude<AdminRole, "unknown">;
export const colleagueRoles: readonly ColleagueRole[] = ["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support"];
export type Colleague = { id: string; email: string; role: string; isActive: boolean };
export type PendingInvitation = { id: string; email: string; role: string; expiresAt: string };
export type Colleagues = { users: Colleague[]; invitations: PendingInvitation[] };
export const normalizedEmail = (value: string) => value.trim().toLowerCase();
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const invalid = () => new HttpError("invalid-response", 200);
function person(value: unknown): Colleague {
  if (!object(value) || !text(value.id) || !text(value.email) || !text(value.role) || typeof value.isActive !== "boolean") throw invalid();
  return { id: value.id, email: value.email, role: value.role, isActive: value.isActive };
}
function invitation(value: unknown): PendingInvitation {
  if (!object(value) || !text(value.id) || !text(value.email) || !text(value.role) || !text(value.expiresAt) || !Number.isFinite(Date.parse(value.expiresAt))) throw invalid();
  return { id: value.id, email: value.email, role: value.role, expiresAt: value.expiresAt };
}
async function safe<T>(action: () => Promise<T>): Promise<T> {
  try { return await action(); }
  catch (error) {
    if (!isHttpError(error)) throw error;
    const errors = Object.fromEntries(Object.keys(error.problem?.errors ?? {}).filter(key => ["email", "role"].includes(key.toLowerCase())).map(key => [key.toLowerCase(), []]));
    throw new HttpError(error.kind, error.status, Object.keys(errors).length ? { errors } : null, null, error.retryAfter);
  }
}
async function read(path: string, signal: AbortSignal, method = "GET", body?: unknown) {
  return safe(async () => {
    const value = await requestJson<unknown>(path, { method, body, signal, cache: "no-store" }); signal.throwIfAborted(); return value;
  });
}
const root = "/api/admin/users";
export const colleaguesApi = {
  async session(signal: AbortSignal): Promise<CurrentUser> {
    const value = await read("/api/admin/auth/me", signal);
    if (!object(value) || !text(value.id) || !text(value.email) || !text(value.tenantId) || !text(value.role)) throw invalid();
    return { id: value.id, email: value.email, role: value.role, tenantId: value.tenantId };
  },
  async list(signal: AbortSignal): Promise<Colleagues> {
    const value = await read(root, signal);
    if (!object(value) || !Array.isArray(value.users) || !Array.isArray(value.invitations)) throw invalid();
    const rows = { users: value.users.map(person), invitations: value.invitations.map(invitation) };
    for (const collection of [rows.users, rows.invitations]) {
      if (new Set(collection.map(row => row.id)).size !== collection.length || new Set(collection.map(row => normalizedEmail(row.email))).size !== collection.length) throw invalid();
    }
    return rows;
  },
  async invite(email: string, role: ColleagueRole, signal: AbortSignal) {
    const row = invitation(await read(root + "/invitations", signal, "POST", { email: email.trim(), role }));
    if (normalizedEmail(row.email) !== normalizedEmail(email) || row.role !== role) throw invalid();
    return row;
  },
  async role(before: Colleague, role: ColleagueRole, signal: AbortSignal) {
    const row = person(await read(`${root}/${encodeURIComponent(before.id)}/role`, signal, "PUT", { role }));
    if (row.id !== before.id || normalizedEmail(row.email) !== normalizedEmail(before.email) || row.role !== role || row.isActive !== before.isActive) throw invalid();
    return row;
  },
  async activity(before: Colleague, signal: AbortSignal) {
    const row = person(await read(`${root}/${encodeURIComponent(before.id)}/${before.isActive ? "deactivate" : "activate"}`, signal, "POST"));
    if (row.id !== before.id || normalizedEmail(row.email) !== normalizedEmail(before.email) || row.role !== before.role || row.isActive === before.isActive) throw invalid();
    return row;
  },
  async withdraw(id: string, signal: AbortSignal) {
    await safe(() => requestNoContent(`${root}/invitations/${encodeURIComponent(id)}`, { method: "DELETE", signal })); signal.throwIfAborted();
  },
};
export const canChangeColleague = (actor: CurrentUser, target: Colleague) => ["Owner", "Admin"].includes(actor.role) && target.id !== actor.id && knownRole(target.role) !== "unknown" && (actor.role === "Owner" || target.role !== "Owner");
