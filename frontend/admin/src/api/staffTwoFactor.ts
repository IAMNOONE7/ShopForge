import type { CurrentUser } from "../api";
import { twoFactorSetup, twoFactorRecoveryCodes, type TwoFactorSetup } from "./twoFactorProof";
import { HttpError, isHttpError, requestJson, requestNoContent } from "./http";

export type StaffSecuritySession = CurrentUser & { isTwoFactorEnabled: boolean };
export type StaffTwoFactorSetup = TwoFactorSetup;
const object = (value: unknown): value is Record<string, unknown> => Boolean(value) && typeof value === "object" && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const invalid = () => new HttpError("invalid-response", 200);
const root = "/api/admin/account/two-factor";

async function safe<T>(action: () => Promise<T>): Promise<T> {
  try { return await action(); }
  catch (error) {
    if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter);
    throw error;
  }
}
async function read(path: string, signal: AbortSignal, method = "GET", body?: unknown) {
  return safe(async () => {
    const value = await requestJson<unknown>(path, { method, body, signal, cache: "no-store", expectedStatus: 200, notifyUnauthorized: false });
    signal.throwIfAborted(); return value;
  });
}
export const staffTwoFactorApi = {
  async session(signal: AbortSignal): Promise<StaffSecuritySession> {
    const value = await read("/api/admin/auth/me", signal);
    if (!object(value) || !text(value.id) || !text(value.email) || !text(value.role) || !text(value.tenantId) || typeof value.isTwoFactorEnabled !== "boolean") throw invalid();
    return { id: value.id, email: value.email, role: value.role, tenantId: value.tenantId, isTwoFactorEnabled: value.isTwoFactorEnabled };
  },
  async begin(email: string, signal: AbortSignal) { return twoFactorSetup(await read(root, signal, "POST"), email, "ShopForge"); },
  async confirm(code: string, signal: AbortSignal): Promise<string[]> {
    const value = await read(root + "/confirm", signal, "POST", { code });
    return twoFactorRecoveryCodes(value);
  },
  async turnOff(password: string, signal: AbortSignal) {
    await safe(() => requestNoContent(root + "/off", { method: "POST", body: { password }, signal, notifyUnauthorized: false }));
    signal.throwIfAborted();
  },
};
