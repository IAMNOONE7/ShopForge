export type StaffResetProof = { readonly realm: "staff"; readonly id: number; available: () => boolean; take: () => string | null };
export type StaffRecoveryEntry = { kind: "forgot" } | { kind: "reset"; proof: StaffResetProof };

let sequence = 0;

export function captureStaffRecoveryEntry(baseUrl = import.meta.env.BASE_URL): StaffRecoveryEntry | null {
  const prefix = "/" + baseUrl.split("/").filter(Boolean).join("/");
  const path = window.location.pathname.replace(/\/$/, "");
  const root = prefix === "/" ? "" : prefix;
  if (path !== `${root}/forgot-password` && path !== `${root}/reset-password`) return null;
  const values = new URLSearchParams(window.location.search).getAll("token");
  let token = values.length === 1 && values[0].trim() ? values[0] : null;
  // Capture before app reads; the credential stays in this one-use memory capsule.
  window.history.replaceState(null, "", window.location.pathname);
  if (path === `${root}/forgot-password`) return { kind: "forgot" };
  return { kind: "reset", proof: { realm: "staff", id: ++sequence, available: () => token !== null, take: () => { const value = token; token = null; return value; } } };
}
