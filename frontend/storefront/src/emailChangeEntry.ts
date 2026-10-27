export type EmailChangeEntry = { available: () => boolean; take: () => string | null };

export function captureEmailChangeEntry(): EmailChangeEntry | null {
  if (window.location.pathname.replace(/\/$/, "") !== "/account/confirm-email") return null;
  const values = new URLSearchParams(window.location.search).getAll("token");
  let token = values.length === 1 && values[0].trim() ? values[0] : null;
  // Remove the credential before app reads begin; only this one-use memory capsule keeps it.
  window.history.replaceState(null, "", window.location.pathname);
  return { available: () => token !== null, take: () => { const value = token; token = null; return value; } };
}
