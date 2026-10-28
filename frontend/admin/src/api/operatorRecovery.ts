import { HttpError, isHttpError, requestAccepted, requestJson, requestNoContent } from "./http";

const root = "/api/platform/auth";
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
async function safe<T>(action: () => Promise<T>): Promise<T> {
  try { return await action(); }
  catch (error) { if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter); throw error; }
}
export const operatorRecoveryApi = {
  async forgot(email: string, signal: AbortSignal) {
    await safe(() => requestAccepted(root + "/password/forgot", { method: "POST", body: { email: email.trim() }, signal, notifyUnauthorized: false, unauthorizedScope: "platform" })); signal.throwIfAborted();
  },
  async reset(token: string, password: string, signal: AbortSignal) {
    await safe(() => requestNoContent(root + "/password/reset", { method: "POST", body: { token, password }, signal, notifyUnauthorized: false, unauthorizedScope: "platform" })); signal.throwIfAborted();
  },
  async session(signal: AbortSignal): Promise<{ email: string } | null> {
    return safe(async () => {
      let value: unknown;
      try { value = await requestJson<unknown>(root + "/me", { signal, cache: "no-store", expectedStatus: 200, notifyUnauthorized: false, unauthorizedScope: "platform" }); }
      catch (error) { signal.throwIfAborted(); if (isHttpError(error) && error.kind === "http" && error.status === 401) return null; throw error; }
      signal.throwIfAborted();
      if (!value || typeof value !== "object" || Array.isArray(value)) throw new HttpError("invalid-response", 200);
      const row = value as Record<string, unknown>;
      if (typeof row.id !== "string" || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(row.id) || !text(row.email) || typeof row.isTwoFactorEnabled !== "boolean" || "tenantId" in row || "role" in row) throw new HttpError("invalid-response", 200);
      return { email: row.email };
    });
  },
  async signOut(signal: AbortSignal) {
    await safe(() => requestJson<unknown>(root + "/logout", { method: "POST", signal, notifyUnauthorized: false, unauthorizedScope: "platform" })); signal.throwIfAborted();
  },
};
