import { HttpError, isHttpError, requestAccepted, requestJson, requestNoContent } from "./http";

export type StaffRecoverySession = { email: string } | null;
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
async function safe<T>(action: () => Promise<T>): Promise<T> {
  try { return await action(); }
  catch (error) {
    if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter);
    throw error;
  }
}
export const staffRecoveryApi = {
  async forgot(email: string, signal: AbortSignal) {
    await safe(() => requestAccepted("/api/admin/auth/password/forgot", { method: "POST", body: { email: email.trim() }, signal, notifyUnauthorized: false })); signal.throwIfAborted();
  },
  async reset(token: string, password: string, signal: AbortSignal) {
    await safe(() => requestNoContent("/api/admin/auth/password/reset", { method: "POST", body: { token, password }, signal, notifyUnauthorized: false })); signal.throwIfAborted();
  },
  async session(signal: AbortSignal): Promise<StaffRecoverySession> {
    return safe(async () => {
      let value: unknown;
      try { value = await requestJson<unknown>("/api/admin/auth/me", { signal, cache: "no-store", notifyUnauthorized: false }); }
      catch (error) { signal.throwIfAborted(); if (isHttpError(error) && error.kind === "http" && error.status === 401) return null; throw error; }
      signal.throwIfAborted();
      if (!value || typeof value !== "object" || Array.isArray(value)) throw new HttpError("invalid-response", 200);
      const row = value as Record<string, unknown>;
      if (!text(row.id) || !text(row.email) || !text(row.tenantId) || !text(row.role)) throw new HttpError("invalid-response", 200);
      return { email: row.email };
    });
  },
  async signOut(signal: AbortSignal) {
    await safe(() => requestJson<unknown>("/api/admin/auth/logout", { method: "POST", signal, notifyUnauthorized: false })); signal.throwIfAborted();
  },
};
