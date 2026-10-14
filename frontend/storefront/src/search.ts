export const searchMinimum = 2;
export const searchMaximum = 200;

// Match the server's trimmed, bounded terms, including directly opened URLs.
export function searchTerm(raw: string | null) {
  const term = raw?.trim() ?? "";
  return term.length < searchMinimum ? "" : term.slice(0, searchMaximum);
}
