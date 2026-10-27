import type { Customer } from "../account";
import { HttpError, isHttpError, requestJson } from "./http";

function customer(value: unknown): Customer {
  if (!value || typeof value !== "object") throw new HttpError("invalid-response", 200);
  const row = value as Record<string, unknown>;
  if (typeof row.email !== "string" || !row.email.trim() || typeof row.firstName !== "string" ||
    typeof row.lastName !== "string" || !(row.phone === null || typeof row.phone === "string")) throw new HttpError("invalid-response", 200);
  return { email: row.email, firstName: row.firstName, lastName: row.lastName, phone: row.phone };
}
async function read(path: string, signal: AbortSignal, body?: unknown): Promise<unknown> {
  try {
    return await requestJson<unknown>(`/api/storefront/account/${path}`, { method: body === undefined ? "GET" : "POST", body, signal, notifyUnauthorized: false });
  } catch (error) {
    if (!isHttpError(error)) throw error;
    // These replies concern credentials; retain actionable field keys, never server text or references.
    const fields = Object.keys(error.problem?.errors ?? {}).filter(key => ["email", "newemail", "password"].includes(key.toLowerCase()));
    throw new HttpError(error.kind, error.status, fields.length ? { errors: Object.fromEntries(fields.map(key => [key, []])) } : null, null, error.retryAfter);
  }
}
function empty(value: unknown) { if (value !== undefined) throw new HttpError("invalid-response", 202); }
export const customerEmailApi = {
  profile: async (signal: AbortSignal) => customer(await read("me", signal)),
  resend: async (email: string, signal: AbortSignal) => empty(await read("verification/resend", signal, { email })),
  request: async (newEmail: string, password: string, signal: AbortSignal) => empty(await read("email", signal, { newEmail, password })),
  confirm: async (token: string, signal: AbortSignal) => customer(await read("email/confirm", signal, { token })),
  logout: async (signal: AbortSignal) => {
    empty(await requestJson<unknown>("/api/storefront/account/logout", { method: "POST", signal, notifyUnauthorized: false }));
  },
};
