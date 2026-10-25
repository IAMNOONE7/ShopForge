import { knownRole, type AdminRole } from "../session";
import { HttpError, isHttpError, requestJson } from "./http";

export type OpenInvitation = { email: string; role: Exclude<AdminRole, "unknown"> };
const record = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const invalid = () => new HttpError("invalid-response", 200);
export const invitationsApi = {
  details: async (token: string, signal?: AbortSignal): Promise<OpenInvitation> => {
    const value = await requestJson<unknown>("/api/admin/invitations/details", { method: "POST", body: { token }, signal, notifyUnauthorized: false });
    signal?.throwIfAborted();
    if (!record(value) || !text(value.email) || !text(value.role) || knownRole(value.role) === "unknown") throw invalid();
    return { email: value.email, role: value.role as OpenInvitation["role"] };
  },
  session: async (signal?: AbortSignal): Promise<{ email: string } | null> => {
    let value: unknown;
    try { value = await requestJson<unknown>("/api/admin/auth/me", { signal, notifyUnauthorized: false }); }
    catch (error) { signal?.throwIfAborted(); if (isHttpError(error) && error.kind === "http" && error.status === 401) return null; throw error; }
    signal?.throwIfAborted();
    if (!record(value) || !text(value.email) || !text(value.id) || !text(value.tenantId) || !text(value.role)) throw invalid();
    return { email: value.email };
  },
  signOut: async (signal?: AbortSignal) => {
    await requestJson<unknown>("/api/admin/auth/logout", { method: "POST", signal, notifyUnauthorized: false });
    signal?.throwIfAborted();
  },
  accept: async (token: string, password: string, invitation: OpenInvitation, signal?: AbortSignal) => {
    const value = await requestJson<unknown>("/api/admin/invitations/accept", { method: "POST", body: { token, password }, signal, notifyUnauthorized: false });
    signal?.throwIfAborted();
    if (!record(value) || !text(value.email) || value.email.toLowerCase() !== invitation.email.toLowerCase()) throw invalid();
    return { email: value.email };
  },
};
