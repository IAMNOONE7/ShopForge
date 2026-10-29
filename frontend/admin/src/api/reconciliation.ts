import { HttpError, requestJson } from "./http";

export type ReportRange = { from: string; to: string };
export type ReportDifference = {
  date: string; kind: string; explained: boolean; orderNumber: string | null;
  transactionId: string | null; expected: number | null; settled: number | null;
  currency: string | null; detail: string;
};
export type ReconciliationReport = ReportRange & {
  provider: string; timeZone: string; statementsThrough: string | null;
  testPayments: number; unexplained: number;
  days: { date: string; paid: number; paidAmount: number; settled: number; settledAmount: number; fees: number; transferred: number; unexplained: number }[];
  differences: ReportDifference[];
};
export const differenceKinds = ["awaitingSettlement", "keptForRefund", "notSettled", "amountDiffers", "settledTwice", "settledButNotPaid", "notStartedHere", "unrecognisedLine", "unreadableLine"] as const;
export function calendarDay(value: unknown): number | null {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value) || value < "0001-01-01") return null;
  const time = Date.parse(value + "T00:00:00Z");
  return Number.isFinite(time) && new Date(time).toISOString().slice(0, 10) === value ? time / 86400000 : null;
}
export function isReportRange(range: ReportRange): boolean {
  const from = calendarDay(range.from), to = calendarDay(range.to);
  return from !== null && to !== null && to >= from && to - from < 31;
}
export function reportQuery(parameters: URLSearchParams): { range: ReportRange | null; invalid: boolean } {
  if (!parameters.has("from") && !parameters.has("to")) return { range: null, invalid: false };
  const range = { from: parameters.get("from") ?? "", to: parameters.get("to") ?? "" };
  return { range, invalid: parameters.getAll("from").length !== 1 || parameters.getAll("to").length !== 1 || !isReportRange(range) };
}
export function reportDate(value: string, locale: string) {
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeZone: "UTC" }).format(new Date(value + "T00:00:00Z"));
}
const record = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const text = (value: unknown) => typeof value === "string" && value.trim().length > 0;
const nullableText = (value: unknown) => value === null || typeof value === "string";
const count = (value: unknown) => typeof value === "number" && Number.isSafeInteger(value) && value >= 0;
const money = (value: unknown) => typeof value === "number" && Number.isFinite(value);
function isReport(value: unknown, range: ReportRange | null, provider?: string): value is ReconciliationReport {
  if (!record(value) || !text(value.provider) || !text(value.timeZone) || provider !== undefined && value.provider !== provider
    || typeof value.from !== "string" || typeof value.to !== "string" || !isReportRange({ from: value.from, to: value.to })
    || range !== null && (value.from !== range.from || value.to !== range.to)
    || value.statementsThrough !== null && calendarDay(value.statementsThrough) === null
    || !count(value.testPayments) || !count(value.unexplained) || !Array.isArray(value.days) || !Array.isArray(value.differences)) return false;
  const first = calendarDay(value.from)!, last = calendarDay(value.to)!, differences = value.differences;
  if (value.days.length !== last - first + 1 || !value.days.every((day, index) => record(day) && calendarDay(day.date) === first + index
    && [day.paid, day.settled, day.unexplained].every(count) && [day.paidAmount, day.settledAmount, day.fees, day.transferred].every(money))) return false;
  if (!differences.every(item => record(item) && calendarDay(item.date) !== null && calendarDay(item.date)! >= first && calendarDay(item.date)! <= last
    && text(item.kind) && typeof item.explained === "boolean" && nullableText(item.orderNumber) && nullableText(item.transactionId)
    && nullableText(item.currency) && (item.expected === null || money(item.expected)) && (item.settled === null || money(item.settled)) && typeof item.detail === "string")) return false;
  return value.unexplained === differences.filter(item => !item.explained).length
    && value.days.every(day => day.unexplained === differences.filter(item => item.date === day.date && !item.explained).length);
}
export const reconciliationApi = {
  async read(storeId: string, range: ReportRange | null, signal?: AbortSignal, provider?: string): Promise<ReconciliationReport> {
    signal?.throwIfAborted();
    if (range !== null && !isReportRange(range)) throw new RangeError("Invalid report range.");
    const query = new URLSearchParams(range ?? {});
    if (provider !== undefined) query.set("provider", provider);
    const value = await requestJson<unknown>(`/api/admin/stores/${encodeURIComponent(storeId)}/reconciliation${query.size ? "?" + query : ""}`, { signal });
    signal?.throwIfAborted();
    if (!isReport(value, range, provider)) throw new HttpError("invalid-response", 200);
    return value;
  },
};
