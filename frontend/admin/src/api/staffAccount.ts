import type { CurrentUser } from "../api";
import { HttpError, isHttpError, requestJson, requestNoContent } from "./http";

const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;

async function safe<T>(action: () => Promise<T>): Promise<T> {
  try { return await action(); }
  catch (error) {
    if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter);
    throw error;
  }
}

export const staffAccountApi = {
  async session(signal: AbortSignal): Promise<CurrentUser> {
    return safe(async () => {
      const value = await requestJson<unknown>("/api/admin/auth/me", { signal, cache: "no-store", notifyUnauthorized: false });
      signal.throwIfAborted();
      if (!value || typeof value !== "object" || Array.isArray(value)) throw new HttpError("invalid-response", 200);
      const row = value as Record<string, unknown>;
      if (!text(row.id) || !text(row.email) || !text(row.role) || !text(row.tenantId)) throw new HttpError("invalid-response", 200);
      return { id: row.id, email: row.email, role: row.role, tenantId: row.tenantId };
    });
  },
  async password(currentPassword: string, newPassword: string, signal: AbortSignal) {
    await safe(() => requestNoContent("/api/admin/account/password", { method: "POST", body: { currentPassword, newPassword }, signal, notifyUnauthorized: false }));
    signal.throwIfAborted();
  },
  async endOtherSessions(signal: AbortSignal) {
    await safe(() => requestNoContent("/api/admin/account/sign-out-everywhere", { method: "POST", signal, notifyUnauthorized: false }));
    signal.throwIfAborted();
  },
};

export function sameStaffAccount(expected: CurrentUser, actual: CurrentUser) {
  return expected.id === actual.id && expected.tenantId === actual.tenantId && expected.role === actual.role && expected.email.trim().toLowerCase() === actual.email.trim().toLowerCase();
}
