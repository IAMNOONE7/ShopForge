import { isHttpError } from "../api/http";

export function normalizedDomain(value: string): string | null {
  let host = value.trim().toLowerCase().replace(/\.+$/, "");
  if (!host || /[\s:/@?#%\\]/.test(host)) return null;
  try {
    if ([...host].some((character) => character.codePointAt(0)! > 127)) host = new URL(`http://${host}`).hostname.replace(/\.+$/, "");
  } catch { return null; }
  if (host.length > 253 || host.split(".").some((label) => !/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(label))) return null;
  return host;
}

export function domainProblem(error: unknown, operation: "add" | "verify" | "primary" | "remove") {
  if (!isHttpError(error)) return null;
  if (operation === "add" && error.status === 400 && error.problem?.errors?.hostName?.some((message) => message.includes("belongs to the platform."))) return "platformAddress";
  if (error.status !== 409) return null;
  const titles = { add: "That domain is already taken", verify: "The record is not there yet", primary: "That domain is not proved yet", remove: "The primary domain cannot be removed" };
  const messages = { add: "domainTaken", verify: "dnsPending", primary: "unverifiedRefused", remove: "primaryProtected" } as const;
  return error.problem?.title === titles[operation] ? messages[operation] : null;
}
