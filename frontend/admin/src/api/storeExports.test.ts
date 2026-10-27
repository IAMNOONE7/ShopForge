import { afterEach, describe, expect, it, vi } from "vitest";
import { storeExportsApi } from "./storeExports";
import { spreadsheetContentType } from "./spreadsheet";
const bytes = new Uint8Array([0x50, 0x4b, 3, 4, 1, 2]);
const reply = (content: BodyInit = bytes, type = spreadsheetContentType, disposition?: string) => new Response(content, { headers: { "Content-Type": type, ...(disposition ? { "Content-Disposition": disposition } : {}) } });
afterEach(() => vi.unstubAllGlobals());
describe("store export downloads", () => {
  it.each(["catalog", "orders"] as const)("downloads %s by an encoded store-wide GET without list filters or mutation headers", async kind => {
    const fetch = vi.fn().mockResolvedValue(reply()); vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
    const file = await storeExportsApi.read("store/a?b", kind, signal);
    expect(fetch.mock.calls[0]).toEqual(["/api/admin/stores/store%2Fa%3Fb/" + (kind === "catalog" ? "export" : "orders/export"), expect.objectContaining({ method: "GET", signal, body: undefined, headers: undefined })]);
    expect(new Uint8Array(await file.blob.arrayBuffer())).toEqual(bytes); expect(file.filename).toBe(kind === "catalog" ? "shopforge-catalogue.xlsx" : "shopforge-orders.xlsx"); expect(fetch).toHaveBeenCalledOnce();
  });
  it.each([
    ['attachment; filename="shopforge-catalogue-2026-10-27.xlsx"', "shopforge-catalogue-2026-10-27.xlsx"],
    ["attachment; filename*=UTF-8''%C5%BDlut%C3%BD%20katalog.xlsx", "Žlutý katalog.xlsx"],
    ['attachment; filename="../../catalog.xlsx"', "catalog.xlsx"],
    ['attachment; filename="catalog.XLSX"', "catalog.XLSX"],
    ['attachment; filename="error.html"', "shopforge-catalogue.xlsx"],
    ['attachment; filename="catalog.xlsx.exe"', "shopforge-catalogue.xlsx"],
    ["attachment; filename*=UTF-8''%broken", "shopforge-catalogue.xlsx"],
  ])("uses a safe workbook filename from %s", async (header, filename) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(bytes, spreadsheetContentType, header))); expect((await storeExportsApi.read("a", "catalog")).filename).toBe(filename);
  });
  it.each([["text/html", "<html>"], ["application/json", "{}"], [spreadsheetContentType, ""], [spreadsheetContentType, "<html>error</html>"], [spreadsheetContentType, "PK"]])("does not offer an unusable %s response as a workbook", async (type, content) => {
    const fetch = vi.fn().mockResolvedValue(reply(content, type)); vi.stubGlobal("fetch", fetch); await expect(storeExportsApi.read("a", "orders")).rejects.toMatchObject({ kind: "invalid-response" }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("accepts the spreadsheet MIME with casing and parameters", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(reply(bytes, spreadsheetContentType.toUpperCase() + "; charset=binary"))); expect((await storeExportsApi.read("a", "catalog")).blob.size).toBe(bytes.length);
  });
  it.each([401, 403, 404, 429, 503])("retains %i as a read failure and never retries automatically", async status => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: "Not available" }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "30" } })); vi.stubGlobal("fetch", fetch);
    await expect(storeExportsApi.read("a", "orders")).rejects.toMatchObject({ status, retryAfter: "30" }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("propagates a network failure without replay", async () => {
    const fetch = vi.fn().mockRejectedValue(new TypeError("offline")); vi.stubGlobal("fetch", fetch); await expect(storeExportsApi.read("a", "catalog")).rejects.toMatchObject({ kind: "network" }); expect(fetch).toHaveBeenCalledOnce();
  });
  it("ignores an aborted late file response", async () => {
    const controller = new AbortController(); vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => { controller.abort(); return reply(); })); await expect(storeExportsApi.read("a", "catalog", controller.signal)).rejects.toMatchObject({ name: "AbortError" });
  });
});
