import { HttpError, requestJson } from "./http";

import { readSpreadsheet } from "./spreadsheet";

export const importFileLimit = 10 * 1024 * 1024;
export { spreadsheetContentType } from "./spreadsheet";
export type ImportIssue = { row: number; column: string | null; message: string };
export type ImportReport = {
  created: number;
  updated: number;
  skipped: number;
  invalid: number;
  failed: number;
  ignoredColumns: string[];
  issues: ImportIssue[];
};
export const importCountFields = ["created", "updated", "skipped", "invalid", "failed"] as const;
export function importFileProblem(file: Pick<File, "name" | "size">) {
  if (!/\.xlsx$/i.test(file.name)) return "fileType";
  if (file.size === 0) return "fileEmpty";
  if (file.size > importFileLimit) return "fileSize";
  return null;
}
function checkedReport(value: unknown): ImportReport {
  const invalid = () => new HttpError("invalid-response", 200);
  if (!value || typeof value !== "object" || Array.isArray(value)) throw invalid();
  const record = value as Record<string, unknown>;
  if (!importCountFields.every(field => Number.isSafeInteger(record[field]) && (record[field] as number) >= 0)
    || importCountFields.reduce((sum, field) => sum + (record[field] as number), 0) > 5000
    || !Array.isArray(record.ignoredColumns) || !record.ignoredColumns.every(column => typeof column === "string")
    || !Array.isArray(record.issues) || record.issues.length > 200) throw invalid();
  const issues = record.issues.map((issue: unknown): ImportIssue => {
    if (!issue || typeof issue !== "object" || Array.isArray(issue)) throw invalid();
    const row = issue as Record<string, unknown>;
    if (!Number.isSafeInteger(row.row) || (row.row as number) < 0 || row.column !== null && typeof row.column !== "string" || typeof row.message !== "string") throw invalid();
    return { row: row.row as number, column: row.column as string | null, message: row.message };
  });
  return {
    created: record.created as number, updated: record.updated as number, skipped: record.skipped as number,
    invalid: record.invalid as number, failed: record.failed as number,
    ignoredColumns: [...record.ignoredColumns], issues,
  };
}
const root = (storeId: string) => "/api/admin/stores/" + encodeURIComponent(storeId) + "/import";
export const catalogImportApi = {
  template: (storeId: string, signal?: AbortSignal) => readSpreadsheet(root(storeId) + "/template", "shopforge-import-template.xlsx", signal),
  apply: async (storeId: string, file: File, signal?: AbortSignal) => {
    if (importFileProblem(file)) throw new TypeError("An .xlsx workbook within the upload limit is required.");
    const body = new FormData(); body.append("file", file);
    const report = await requestJson<unknown>(root(storeId), { method: "POST", body, signal });
    signal?.throwIfAborted();
    return checkedReport(report);
  },
};
