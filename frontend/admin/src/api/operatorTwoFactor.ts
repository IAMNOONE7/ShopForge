import { operatorAccountApi } from "./operatorAccount";
import { HttpError, isHttpError, requestJson, requestNoContent } from "./http";
import { twoFactorSetup, twoFactorRecoveryCodes } from "./twoFactorProof";

const root = "/api/platform/account/two-factor";
const options = { notifyUnauthorized: false, unauthorizedScope: "platform" as const };
async function safe<T>(action: () => Promise<T>): Promise<T> {
  try { return await action(); }
  catch (error) { if (isHttpError(error)) throw new HttpError(error.kind, error.status, null, null, error.retryAfter); throw error; }
}
async function post(path: string, signal: AbortSignal, body?: unknown) {
  return safe(async () => {
    const value = await requestJson<unknown>(path, { ...options, method: "POST", body, signal, cache: "no-store", expectedStatus: 200 });
    signal.throwIfAborted(); return value;
  });
}
export const operatorTwoFactorApi = {
  session: operatorAccountApi.session,
  async begin(email: string, signal: AbortSignal) { return twoFactorSetup(await post(root, signal), email, "ShopForge Platform"); },
  async confirm(code: string, signal: AbortSignal) { return twoFactorRecoveryCodes(await post(root + "/confirm", signal, { code })); },
  async turnOff(password: string, signal: AbortSignal) {
    await safe(() => requestNoContent(root + "/off", { ...options, method: "POST", body: { password }, signal }));
    signal.throwIfAborted();
  },
};
