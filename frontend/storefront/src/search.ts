export const searchMinimum = 2;
export const searchMaximum = 200;

export function normalizeSearch(raw: string | null) {
  return raw?.normalize("NFC").trim() ?? "";
}

// Match the server's trimmed, bounded terms, including directly opened URLs.
export function searchTerm(raw: string | null) {
  const term = normalizeSearch(raw);
  if (term.length < searchMinimum) return "";
  if (term.length <= searchMaximum) return term;
  const last = term.charCodeAt(searchMaximum - 1);
  return term.slice(0, last >= 0xd800 && last <= 0xdbff ? searchMaximum - 1 : searchMaximum);
}
