import { isHttpError } from "../api/http";
import { normalizedDomain } from "./domainValidation";

export type DomainZoneInput = { zone: string | null; name: string };
export function domainZoneInput(hostName: string, draft: string): DomainZoneInput | null {
  const host = normalizedDomain(hostName);
  if (!host) return null;
  const zone = draft.trim() ? normalizedDomain(draft) : null;
  const name = draft.trim() ? zone : host.split(".").slice(-2).join(".");
  if (!name || !name.includes(".") || host !== name && !host.endsWith(`.${name}`)) return null;
  return { zone, name };
}

export function domainZoneProblem(error: unknown) {
  if (!isHttpError(error) || error.kind !== "http") return null;
  if (error.status === 404) return "domainMissing";
  if (error.status !== 409) return null;
  if (error.problem?.title === "Not here") return "unavailable";
  if (error.problem?.title === "That zone is already taken") return "taken";
  if (error.problem?.title === "Not now") return "busy";
  return null;
}
