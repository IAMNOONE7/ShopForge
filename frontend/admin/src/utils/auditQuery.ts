export type AuditQuery = { storeId: string; action: string; from: string; to: string; page: number; pageSize: number };
export type AuditDraft = { storeId: string; action: string; from: string; to: string; pageSize: string };
export type AuditIssue = "storeId" | "action" | "from" | "to" | "range" | "page" | "pageSize";
export const auditGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export const maxAuditPage = (size: number) => Math.min(2_147_483_647, Math.floor(2_147_483_647 / size) + 1);

// Compare at DateTimeOffset precision without rounding a shared filter's fractional seconds to milliseconds.
export function auditInstant(value: string): bigint | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?(Z|[+-]\d{2}:\d{2})$/i.exec(value);
  if (!match) return null;
  const [, year, month, day, hour, minute, second = "00", fraction = "", zone] = match;
  const y = Number(year), m = Number(month), d = Number(day);
  const days = [31, y % 4 === 0 && (y % 100 !== 0 || y % 400 === 0) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  const offsetHour = zone.toUpperCase() === "Z" ? 0 : Number(zone.slice(1, 3));
  const offsetMinute = zone.toUpperCase() === "Z" ? 0 : Number(zone.slice(4));
  if (y < 1 || m < 1 || m > 12 || d < 1 || d > days[m - 1] || Number(hour) > 23 || Number(minute) > 59 || Number(second) > 59 || offsetHour > 14 || offsetMinute > 59 || offsetHour === 14 && offsetMinute !== 0) return null;
  const millis = Date.parse(`${year}-${month}-${day}T${hour}:${minute}:${second}.000${zone.toUpperCase()}`);
  if (!Number.isFinite(millis)) return null;
  const instant = BigInt(millis) * 10_000n + BigInt(fraction.padEnd(7, "0"));
  return instant < -621355968000000000n || instant > 2534023007999999999n ? null : instant;
}

export function checkedAuditDraft(draft: AuditDraft, page = "1") {
  const issues: AuditIssue[] = [];
  const storeId = draft.storeId.trim(), action = draft.action, from = draft.from.trim(), to = draft.to.trim();
  const size = Number(draft.pageSize), number = Number(page);
  if (storeId && !auditGuid.test(storeId)) issues.push("storeId");
  if (action.length > 60) issues.push("action");
  if (from && auditInstant(from) === null) issues.push("from");
  if (to && auditInstant(to) === null) issues.push("to");
  if (from && to && !issues.includes("from") && !issues.includes("to") && auditInstant(from)! >= auditInstant(to)!) issues.push("range");
  if (!/^\d+$/.test(draft.pageSize) || !Number.isInteger(size) || size < 1 || size > 100) issues.push("pageSize");
  if (!/^\d+$/.test(page) || !Number.isSafeInteger(number) || number < 1 || number > maxAuditPage(size || 50)) issues.push("page");
  const query: AuditQuery | null = issues.length ? null : { storeId, action: action.trim() ? action : "", from, to, page: number, pageSize: size };
  return { query, issues };
}

export function auditQuery(parameters: URLSearchParams) {
  const draft: AuditDraft = { storeId: parameters.get("storeId") ?? "", action: parameters.get("action") ?? "", from: parameters.get("from") ?? "", to: parameters.get("to") ?? "", pageSize: parameters.get("pageSize") ?? "50" };
  const checked = checkedAuditDraft(draft, parameters.get("page") ?? "1");
  const allowed = ["storeId", "action", "from", "to", "page", "pageSize"];
  const ambiguous = [...parameters.keys()].some(key => !allowed.includes(key) || parameters.getAll(key).length !== 1);
  return { draft, issues: checked.issues, invalidUrl: ambiguous || checked.query === null, query: ambiguous ? null : checked.query };
}

export function auditParameters(query: AuditQuery) {
  const parameters = new URLSearchParams();
  for (const key of ["storeId", "action", "from", "to"] as const) if (query[key]) parameters.set(key, query[key]);
  if (query.page > 1) parameters.set("page", String(query.page));
  if (query.pageSize !== 50) parameters.set("pageSize", String(query.pageSize));
  return parameters;
}
