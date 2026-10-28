import { operatorPrefix } from "./routing";

export type OperatorResetProof = { readonly realm: "operator"; readonly id: number; available: () => boolean; take: () => string | null };
export type OperatorRecoveryEntry = { kind: "forgot" } | { kind: "reset"; proof: OperatorResetProof };
let sequence = 0;

export function captureOperatorRecoveryEntry(baseUrl = import.meta.env.BASE_URL): OperatorRecoveryEntry | null {
  const root = operatorPrefix(baseUrl), path = window.location.pathname.replace(/\/$/, "");
  if (path !== `${root}/forgot-password` && path !== `${root}/reset-password`) return null;
  const values = new URLSearchParams(window.location.search).getAll("token");
  let token = values.length === 1 && values[0].trim() ? values[0] : null;
  window.history.replaceState(null, "", window.location.pathname);
  if (path === `${root}/forgot-password`) return { kind: "forgot" };
  return { kind: "reset", proof: { realm: "operator", id: ++sequence, available: () => token !== null, take: () => { const value = token; token = null; return value; } } };
}
