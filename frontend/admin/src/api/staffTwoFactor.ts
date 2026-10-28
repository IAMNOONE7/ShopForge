import type { CurrentUser } from "../api";
import { HttpError, isHttpError, requestJson, requestNoContent } from "./http";

export type StaffSecuritySession = CurrentUser & { isTwoFactorEnabled: boolean };
export type StaffTwoFactorSetup = { secret: string; enrolmentUri: string };
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
function setup(value: unknown, email: string): StaffTwoFactorSetup {
  if (!object(value) || typeof value.secret !== "string" || !/^[A-Z2-7]{32}$/.test(value.secret) || typeof value.enrolmentUri !== "string") throw invalid();
  let uri: URL;
  try { uri = new URL(value.enrolmentUri); } catch { throw invalid(); }
  let account: string;
  try { account = decodeURIComponent(uri.pathname); } catch { throw invalid(); }
  if (uri.protocol !== "otpauth:" || uri.hostname !== "totp" || uri.port || uri.username || uri.password || uri.hash || account.toLowerCase() !== `/ShopForge:${email}`.toLowerCase() ||
    [...uri.searchParams].length !== 4 || uri.searchParams.get("secret") !== value.secret || uri.searchParams.get("issuer") !== "ShopForge" || uri.searchParams.get("digits") !== "6" || uri.searchParams.get("period") !== "30") throw invalid();
  return { secret: value.secret, enrolmentUri: value.enrolmentUri };
}
export const staffTwoFactorApi = {
  async session(signal: AbortSignal): Promise<StaffSecuritySession> {
    const value = await read("/api/admin/auth/me", signal);
    if (!object(value) || !text(value.id) || !text(value.email) || !text(value.role) || !text(value.tenantId) || typeof value.isTwoFactorEnabled !== "boolean") throw invalid();
    return { id: value.id, email: value.email, role: value.role, tenantId: value.tenantId, isTwoFactorEnabled: value.isTwoFactorEnabled };
  },
  async begin(email: string, signal: AbortSignal) { return setup(await read(root, signal, "POST"), email); },
  async confirm(code: string, signal: AbortSignal): Promise<string[]> {
    const value = await read(root + "/confirm", signal, "POST", { code });
    if (!object(value) || !Array.isArray(value.recoveryCodes) || value.recoveryCodes.length !== 10 || !value.recoveryCodes.every(code => typeof code === "string" && /^[A-F0-9]{6}-[A-F0-9]{6}$/.test(code)) || new Set(value.recoveryCodes).size !== 10) throw invalid();
    return value.recoveryCodes as string[];
  },
  async turnOff(password: string, signal: AbortSignal) {
    await safe(() => requestNoContent(root + "/off", { method: "POST", body: { password }, signal, notifyUnauthorized: false }));
    signal.throwIfAborted();
  },
};
