import { afterEach, describe, expect, it, vi } from "vitest";
import { catalogImportApi, importFileLimit, importFileProblem, spreadsheetContentType } from "./catalogImport";

const report = { created: 1, updated: 2, skipped: 3, invalid: 1, failed: 0, ignoredColumns: ["supplier"], issues: [{ row: 4, column: "weight", message: "Missing grams" }] };
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const file = () => new File(["workbook"], "catalog.xlsx", { type: spreadsheetContentType });
afterEach(() => vi.unstubAllGlobals());
describe("catalog import contracts", () => {
  it("sends one scoped multipart file and returns checked row outcomes without inferring success", async () => {
    const fetch = vi.fn().mockResolvedValue(response({ ...report, unknown: "discarded", issues: [{ ...report.issues[0], extra: true }] })); vi.stubGlobal("fetch", fetch);
    const signal = new AbortController().signal, workbook = file();
    expect(await catalogImportApi.apply("store/a", workbook, signal)).toEqual(report);
    const [path, init] = fetch.mock.calls[0];
    expect(path).toBe("/api/admin/stores/store%2Fa/import"); expect(init.method).toBe("POST"); expect(init.signal).toBe(signal); expect(init.headers).toBeUndefined();
    expect([...init.body.keys()]).toEqual(["file"]); expect(init.body.get("file").name).toBe(workbook.name); expect(fetch).toHaveBeenCalledOnce();
  });
  it.each([null, {}, { ...report, created: -1 }, { ...report, updated: 1.5 }, { ...report, failed: "0" }, { ...report, skipped: 5000 },
    { ...report, ignoredColumns: null }, { ...report, ignoredColumns: [1] }, { ...report, issues: null }, { ...report, issues: Array(201).fill(report.issues[0]) },
    { ...report, issues: [{ row: -1, column: null, message: "Bad" }] }, { ...report, issues: [{ row: 0, message: "Bad" }] }, { ...report, issues: [{ row: 0, column: null, message: 1 }] },
  ])("rejects a malformed applied report without sending again: %j", async body => {
    const fetch = vi.fn().mockResolvedValue(response(body)); vi.stubGlobal("fetch", fetch);
    await expect(catalogImportApi.apply("a", file())).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("preserves zero outcomes, whole-workbook refusals and stock warnings literally", async () => {
    const value = { created: 0, updated: 0, skipped: 0, invalid: 0, failed: 5, ignoredColumns: [], issues: [{ row: 0, column: "visible", message: "Nothing was imported: SKU-X" }, { row: 5002, column: null, message: "<img src=x onerror=alert(1)>" }] };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(value))); expect(await catalogImportApi.apply("a", file())).toEqual(value);
  });
  it.each([["catalog.csv", 1, "fileType"], ["catalog.xlsx", 0, "fileEmpty"], ["catalog.xlsx", importFileLimit + 1, "fileSize"], ["CATALOG.XLSX", importFileLimit, null]] as const)("validates %s of %d bytes", (name, size, problem) => expect(importFileProblem({ name, size })).toBe(problem));
  it("blocks an invalid local file before transport", async () => {
    const fetch = vi.fn(); vi.stubGlobal("fetch", fetch); await expect(catalogImportApi.apply("a", new File([], "empty.xlsx"))).rejects.toThrow(); expect(fetch).not.toHaveBeenCalled();
  });
  it("downloads a scoped spreadsheet with safe filename and unchanged bytes", async () => {
    const bytes = new Uint8Array([0x50, 0x4b, 3, 4, 1, 2]);
    const fetch = vi.fn().mockResolvedValue(new Response(bytes, { headers: { "Content-Type": spreadsheetContentType, "Content-Disposition": 'attachment; filename="../template.xlsx"' } })); vi.stubGlobal("fetch", fetch);
    const download = await catalogImportApi.template("a/b"); expect(download.filename).toBe("template.xlsx"); expect(new Uint8Array(await download.blob.arrayBuffer())).toEqual(bytes); expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/a%2Fb/import/template");
  });
  it.each([["text/html", "<html>"], [spreadsheetContentType, ""], [spreadsheetContentType, "not a workbook"]])("rejects an unusable template (%s, %s)", async (type, value) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(value, { headers: { "Content-Type": type } }))); await expect(catalogImportApi.template("a")).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each([400, 413, 429, 503])("propagates %i without automatically replaying the import", async status => {
    const fetch = vi.fn().mockResolvedValue(response({ errors: { file: ["Cannot read workbook"] } }, status)); vi.stubGlobal("fetch", fetch);
    await expect(catalogImportApi.apply("a", file())).rejects.toMatchObject({ status }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("ignores an aborted late applied response", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(report))); const controller = new AbortController(); controller.abort();
    await expect(catalogImportApi.apply("a", file(), controller.signal)).rejects.toMatchObject({ name: "AbortError" });
  });
});
