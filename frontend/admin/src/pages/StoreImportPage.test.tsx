// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { catalogImportApi, importFileLimit, type ImportReport } from "../api/catalogImport";
import { HttpError, saveDownload } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { metadataStore } from "../test/metadataFixtures";
import { StoreImportPage } from "./StoreImportPage";

vi.mock("../api/http", async original => ({ ...await original<typeof import("../api/http")>(), saveDownload: vi.fn() }));
const report: ImportReport = { created: 1, updated: 2, skipped: 3, invalid: 1, failed: 0, ignoredColumns: ["supplier"], issues: [{ row: 5, column: "weight", message: "Weight must be at least 1 gram." }] };
const account = (role = "Owner") => ({ id: "owner", email: "owner@example.test", role, tenantId: "tenant-a" });
const workbook = (name = "catalog.xlsx") => new File(["workbook"], name);
function Shell() {
  const [store, setStore] = useState(metadataStore), [role, setRole] = useState("Owner"), [staff, setStaff] = useState("owner"), [refreshing, setRefreshing] = useState(false);
  return <SessionContext value={{ user: { ...account(role), id: staff }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => setStore({ ...metadataStore, id: "other-store" })}>Other store</button><button onClick={() => setRole("CatalogManager")}>Other role</button><button onClick={() => setStaff("other-staff")}>Other session</button>
    <button onClick={() => setStore({ ...store, currency: "EUR" })}>Change store currency</button><button onClick={() => setRefreshing(value => !value)}>Toggle store refresh</button>
    <Outlet context={{ store, storeRefreshing: refreshing, storeRefreshError: null, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function route() { return render(<MemoryRouter initialEntries={["/import"]}><Routes><Route element={<Shell />}><Route path="import" element={<StoreImportPage />} /></Route></Routes></MemoryRouter>); }
const select = (file = workbook()) => fireEvent.change(screen.getByLabelText(i18n.t("import:chooseFile")), { target: { files: [file] } });
const submit = () => screen.getByRole("button", { name: i18n.t("import:submit") });
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); vi.stubGlobal("CSS", { escape: (value: string) => value }); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.spyOn(catalogImportApi, "apply").mockResolvedValue(report);
  vi.spyOn(catalogImportApi, "template").mockResolvedValue({ blob: new Blob(["zip"]), filename: "template.xlsx" });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.mocked(saveDownload).mockClear(); window.history.replaceState(null, "", "/"); });
describe("catalog import workspace", () => {
  it.each(["en", "cs"])("retains a selected file and imports only after explicit submit in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); route(); select();
    expect(screen.getByText("catalog.xlsx")).toBeTruthy(); expect(catalogImportApi.apply).not.toHaveBeenCalled();
    await user.click(submit()); await screen.findByRole("heading", { name: i18n.t("import:reportTitle") });
    expect(catalogImportApi.apply).toHaveBeenCalledExactlyOnceWith(metadataStore.id, expect.objectContaining({ name: "catalog.xlsx" }), expect.any(AbortSignal));
    expect(screen.getByText(i18n.t("import:counts.skipped"))).toBeTruthy(); expect(screen.getByText("supplier")).toBeTruthy(); expect(submit()).toHaveProperty("disabled", true);
    expect(document.activeElement).toBe(screen.getByRole("heading", { name: i18n.t("import:reportTitle") }).closest(".import-feedback"));
    select(); expect(submit()).toHaveProperty("disabled", false); expect(screen.queryByRole("heading", { name: i18n.t("import:reportTitle") })).toBeNull(); expect(catalogImportApi.apply).toHaveBeenCalledOnce();
  });
  it("opens the native chooser through a localized keyboard-accessible button", async () => {
    const user = userEvent.setup(); route(); const input = screen.getByLabelText("Choose an .xlsx workbook"), choose = screen.getByRole("button", { name: "Choose an .xlsx workbook" });
    const click = vi.spyOn(input, "click"); choose.focus(); await user.keyboard("{Enter}"); expect(click).toHaveBeenCalledOnce(); expect(catalogImportApi.apply).not.toHaveBeenCalled();
  });
  it("requires a selection and focuses the file control before transport", async () => {
    const user = userEvent.setup(); route(); await user.click(submit()); expect(document.activeElement).toBe(screen.getByRole("button", { name: "Choose an .xlsx workbook" })); expect(screen.getByText(i18n.t("import:fileRequired"))).toBeTruthy(); expect(catalogImportApi.apply).not.toHaveBeenCalled();
  });
  it.each(["fileType", "fileEmpty", "fileSize"] as const)("keeps an earlier selection on %s but blocks submission until corrected", async problem => {
    route(); select(); const file = problem === "fileType" ? workbook("wrong.csv") : problem === "fileEmpty" ? new File([], "empty.xlsx") : workbook("large.xlsx");
    if (problem === "fileSize") Object.defineProperty(file, "size", { value: importFileLimit + 1 }); select(file);
    expect(screen.getByText(i18n.t(`import:${problem}`))).toBeTruthy(); expect(screen.getByText("catalog.xlsx")).toBeTruthy(); expect(submit()).toHaveProperty("disabled", true);
    fireEvent.submit(submit().closest("form")!); expect(catalogImportApi.apply).not.toHaveBeenCalled(); select(); expect(submit()).toHaveProperty("disabled", false);
  });
  it("keeps a canceled selection and supports explicit clearing", async () => {
    const user = userEvent.setup(); route(); select(); fireEvent.change(screen.getByLabelText("Choose an .xlsx workbook"), { target: { files: [] } }); expect(screen.getByText("catalog.xlsx")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Clear selection" })); expect(screen.queryByText("catalog.xlsx")).toBeNull(); expect(document.activeElement).toBe(screen.getByRole("button", { name: "Choose an .xlsx workbook" })); expect(catalogImportApi.apply).not.toHaveBeenCalled();
  });
  it("downloads a checked template explicitly and keeps selection after a download refusal", async () => {
    const user = userEvent.setup(); route(); select(); await user.click(screen.getByRole("button", { name: "Download template" })); await waitFor(() => expect(saveDownload).toHaveBeenCalledOnce());
    vi.mocked(catalogImportApi.template).mockRejectedValueOnce(new HttpError("invalid-response", 200)); await user.click(screen.getByRole("button", { name: "Download template" })); await screen.findByRole("alert");
    expect(screen.getByText("catalog.xlsx")).toBeTruthy(); expect(saveDownload).toHaveBeenCalledOnce(); expect(catalogImportApi.apply).not.toHaveBeenCalled();
  });
  it("serializes double imports and locks the selected file until the report arrives", async () => {
    const held = deferred<ImportReport>(); vi.mocked(catalogImportApi.apply).mockReturnValueOnce(held.promise); route(); select(); const form = submit().closest("form")!;
    act(() => { fireEvent.submit(form); fireEvent.submit(form); }); expect(catalogImportApi.apply).toHaveBeenCalledOnce(); expect(screen.getByLabelText("Choose an .xlsx workbook")).toHaveProperty("disabled", true);
    await act(async () => held.resolve(report)); await screen.findByRole("heading", { name: "Import report" });
  });
  it("does not race an import against a template download", async () => {
    const held = deferred<{ blob: Blob; filename: string }>(); vi.mocked(catalogImportApi.template).mockReturnValueOnce(held.promise); route(); select();
    act(() => { fireEvent.click(screen.getByRole("button", { name: "Download template" })); fireEvent.submit(submit().closest("form")!); }); expect(catalogImportApi.apply).not.toHaveBeenCalled();
    await act(async () => held.resolve({ blob: new Blob(), filename: "template.xlsx" }));
  });
  it.each([400, 413, 429])("retains refused file after %i and never retries automatically", async status => {
    const user = userEvent.setup(); vi.mocked(catalogImportApi.apply).mockRejectedValueOnce(new HttpError("http", status, status === 400 ? { errors: { file: ["The first row must contain column names. <b>literal</b>"] } } : null)); route(); select();
    await user.click(submit()); await screen.findByText(i18n.t("import:refused")); expect(screen.getByText("catalog.xlsx")).toBeTruthy(); expect(submit()).toHaveProperty("disabled", false); expect(catalogImportApi.apply).toHaveBeenCalledOnce();
    if (status === 400) { expect(screen.getByText("The first row must contain column names. <b>literal</b>")).toBeTruthy(); expect(document.querySelector(".import-feedback b")).toBeNull(); await user.click(screen.getByRole("button", { name: i18n.t("import:fileRefused") })); expect(document.activeElement).toBe(screen.getByRole("button", { name: "Choose an .xlsx workbook" })); }
    await user.click(submit()); await screen.findByRole("heading", { name: "Import report" }); expect(catalogImportApi.apply).toHaveBeenCalledTimes(2);
  });
  it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 200)])("blocks all new imports after an unclear outcome: %j", async error => {
    const user = userEvent.setup(); vi.mocked(catalogImportApi.apply).mockRejectedValueOnce(error); route(); select(); await user.click(submit()); await screen.findByText(i18n.t("import:uncertain"));
    expect(submit()).toHaveProperty("disabled", true); expect(screen.getByLabelText("Choose an .xlsx workbook")).toHaveProperty("disabled", true); expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
    fireEvent.submit(submit().closest("form")!); expect(catalogImportApi.apply).toHaveBeenCalledOnce(); expect(screen.getByRole("link", { name: "Review store listings" }).getAttribute("href")).toBe("/stores/" + metadataStore.id + "/products"); expect(screen.queryByRole("heading", { name: "Import report" })).toBeNull();
  });
  it("shows returned zero counts, whole-workbook refusal and literal diagnostics without claiming success", async () => {
    const user = userEvent.setup(); vi.mocked(catalogImportApi.apply).mockResolvedValueOnce({ ...report, created: 0, updated: 0, failed: 3, issues: [{ row: 0, column: "visible", message: "Nothing imported: SKU-LEGACY <img src=x>" }] }); route(); select(); await user.click(submit());
    await screen.findByText("Whole workbook (row 0)"); expect(screen.getByText("Nothing imported: SKU-LEGACY <img src=x>")).toBeTruthy(); expect(document.querySelector(".import-results img")).toBeNull();
    const counts = document.querySelector(".import-counts")!; expect(within(counts as HTMLElement).getByText("Created rows").nextElementSibling?.textContent).toBe("0");
  });
  it("reports an empty result without inventing a success or diagnostics", async () => {
    const user = userEvent.setup(); vi.mocked(catalogImportApi.apply).mockResolvedValueOnce({ created: 0, updated: 0, skipped: 0, invalid: 0, failed: 0, ignoredColumns: [], issues: [] }); route(); select(); await user.click(submit()); await screen.findByText(i18n.t("import:noIssues")); expect(screen.queryByText("Ignored columns")).toBeNull();
  });
  it("paginates returned diagnostics and labels null columns and actual worksheet rows", async () => {
    const user = userEvent.setup(); vi.mocked(catalogImportApi.apply).mockResolvedValueOnce({ ...report, issues: Array.from({ length: 21 }, (_, index) => ({ row: index + 2, column: null, message: "Problem " + index })) }); route(); select(); await user.click(submit());
    await screen.findByText("Problem 0"); expect(screen.queryByText("Problem 20")).toBeNull(); await user.click(screen.getByRole("button", { name: "Next diagnostics" })); expect(screen.getByText("Problem 20")).toBeTruthy(); expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Import report" }));
  });
  it("blocks a stale store and requires reselection after store settings change", async () => {
    const user = userEvent.setup(); route(); select(); await user.click(screen.getByRole("button", { name: "Toggle store refresh" })); fireEvent.submit(submit().closest("form")!); expect(catalogImportApi.apply).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Toggle store refresh" })); await user.click(screen.getByRole("button", { name: "Change store currency" })); await screen.findByText(i18n.t("import:storeChanged")); fireEvent.submit(submit().closest("form")!); expect(catalogImportApi.apply).not.toHaveBeenCalled(); select(); expect(submit()).toHaveProperty("disabled", false);
  });
  it.each(["store", "role", "session", "unmount"])("aborts upload and discards late reports on %s departure", async departure => {
    const user = userEvent.setup(), held = deferred<ImportReport>(); vi.mocked(catalogImportApi.apply).mockReturnValueOnce(held.promise); const view = route(); select(); await user.click(submit());
    if (departure === "unmount") view.unmount(); else await user.click(screen.getByRole("button", { name: "Other " + departure })); expect(vi.mocked(catalogImportApi.apply).mock.calls[0][2]?.aborted).toBe(true);
    await act(async () => held.resolve(report)); expect(screen.queryByRole("heading", { name: "Import report" })).toBeNull(); expect(catalogImportApi.apply).toHaveBeenCalledOnce();
  });
  it("discards a late template after departure without triggering a download", async () => {
    const user = userEvent.setup(), held = deferred<{ blob: Blob; filename: string }>(); vi.mocked(catalogImportApi.template).mockReturnValueOnce(held.promise); const view = route(); await user.click(screen.getByRole("button", { name: "Download template" })); view.unmount();
    expect(vi.mocked(catalogImportApi.template).mock.calls[0][1]?.aborted).toBe(true); await act(async () => held.resolve({ blob: new Blob(), filename: "old.xlsx" })); expect(saveDownload).not.toHaveBeenCalled();
  });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("gates the actual direct import route for %s without eager import or template requests", async role => {
    vi.spyOn(api, "me").mockResolvedValue(account(role)); vi.spyOn(api, "stores").mockResolvedValue([metadataStore]); window.history.replaceState(null, "", "/stores/" + metadataStore.id + "/import"); render(<App />);
    const allowed = ["Owner", "Admin", "CatalogManager"].includes(role); await screen.findByRole("heading", { name: allowed ? "Catalog import" : "Permission required" });
    expect(screen.queryByLabelText("Choose an .xlsx workbook") !== null).toBe(allowed); expect(catalogImportApi.apply).not.toHaveBeenCalled(); expect(catalogImportApi.template).not.toHaveBeenCalled();
  });
});
