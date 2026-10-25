export type InvitationEntry = { token: string | null };

export function captureInvitationEntry(baseUrl = import.meta.env.BASE_URL): InvitationEntry | null {
  const prefix = "/" + baseUrl.split("/").filter(Boolean).concat("invitations", "accept").join("/");
  if (window.location.pathname.replace(/\/$/, "") !== prefix) return null;
  const values = new URLSearchParams(window.location.search).getAll("token");
  const token = values.length === 1 && values[0].trim() ? values[0] : null;
  // Only memory keeps the credential. Unrelated query/hash data cannot become a return destination.
  window.history.replaceState(null, "", window.location.pathname);
  return { token };
}
