import { HttpError } from "./http";

export type TwoFactorSetup = { secret: string; enrolmentUri: string };
const object = (value: unknown): value is Record<string, unknown> => Boolean(value) && typeof value === "object" && !Array.isArray(value);
const invalid = () => new HttpError("invalid-response", 200);
export function twoFactorSetup(value: unknown, email: string, issuer: "ShopForge" | "ShopForge Platform"): TwoFactorSetup {
  if (!object(value) || typeof value.secret !== "string" || !/^[A-Z2-7]{32}$/.test(value.secret) || typeof value.enrolmentUri !== "string") throw invalid();
  let uri: URL;
  try { uri = new URL(value.enrolmentUri); } catch { throw invalid(); }
  let account: string;
  try { account = decodeURIComponent(uri.pathname); } catch { throw invalid(); }
  if (uri.protocol !== "otpauth:" || uri.hostname !== "totp" || uri.port || uri.username || uri.password || uri.hash || account.toLowerCase() !== `/${issuer}:${email}`.toLowerCase() ||
    [...uri.searchParams].length !== 4 || uri.searchParams.get("secret") !== value.secret || uri.searchParams.get("issuer") !== issuer || uri.searchParams.get("digits") !== "6" || uri.searchParams.get("period") !== "30") throw invalid();
  return { secret: value.secret, enrolmentUri: value.enrolmentUri };
}
export function twoFactorRecoveryCodes(value: unknown): string[] {
  if (!object(value) || !Array.isArray(value.recoveryCodes) || value.recoveryCodes.length !== 10 || !value.recoveryCodes.every(code => typeof code === "string" && /^[A-F0-9]{6}-[A-F0-9]{6}$/.test(code)) || new Set(value.recoveryCodes).size !== 10) throw invalid();
  return value.recoveryCodes as string[];
}
