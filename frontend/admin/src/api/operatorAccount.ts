import { HttpError, isHttpError, requestJson, requestNoContent } from "./http";
import type { PlatformUser } from "./platform";

async function safe<T>(action: () => Promise<T>): Promise<T> {
  try { return await action(); }
  catch (error) { if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter); throw error; }
}
const options = { notifyUnauthorized: false, unauthorizedScope: "platform" as const };
export const operatorAccountApi = {
  async session(signal: AbortSignal): Promise<PlatformUser> {
    return safe(async () => {
      const value = await requestJson<unknown>("/api/platform/auth/me", { ...options, signal, cache: "no-store", expectedStatus: 200 });
      signal.throwIfAborted();
      if (!value || typeof value !== "object" || Array.isArray(value)) throw new HttpError("invalid-response", 200);
      const row = value as Record<string, unknown>;
      if (typeof row.id !== "string" || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(row.id) || typeof row.email !== "string" || !row.email.trim() || typeof row.isTwoFactorEnabled !== "boolean" || "tenantId" in row || "role" in row) throw new HttpError("invalid-response", 200);
      return { id: row.id, email: row.email, isTwoFactorEnabled: row.isTwoFactorEnabled };
    });
  },
  async password(currentPassword: string, newPassword: string, signal: AbortSignal) {
    await safe(() => requestNoContent("/api/platform/account/password", { ...options, method: "POST", body: { currentPassword, newPassword }, signal }));
    signal.throwIfAborted();
  },
  async endOtherSessions(signal: AbortSignal) {
    await safe(() => requestNoContent("/api/platform/account/sign-out-everywhere", { ...options, method: "POST", signal }));
    signal.throwIfAborted();
  },
};
export function sameOperatorAccount(expected: PlatformUser, actual: PlatformUser) {
  return expected.id.toLowerCase() === actual.id.toLowerCase() && expected.email.trim().toLowerCase() === actual.email.trim().toLowerCase() && expected.isTwoFactorEnabled === actual.isTwoFactorEnabled;
}
