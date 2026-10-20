// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import type { TaxClass } from "../api/taxClasses";
import { AdminNavigation } from "../components/AdminNavigation";
import { initializeI18n, i18n } from "../i18n";
import { SessionContext } from "../session";
import { StoreTaxClassesPage } from "./StoreTaxClassesPage";
import { StoreOverviewPage } from "./StoreOverviewPage";

const books: TaxClass = { code: "books", name: "Books", rates: [{ country: "CZ", rate: 10 }, { country: "DE", rate: 7 }] };
const store: AdminStore = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "published", logoUrl: null, primaryHostName: null, returnWindowDays: 14,
  company: { legalName: "Workshop", line1: "Street", city: "Prague", postalCode: "10000", country: "CZ", registrationNumber: "12345678", vatNumber: null },
  theme: { primaryColor: "#123456", secondaryColor: "#eef4ff", borderRadius: 6 } };
let rows: TaxClass[];
type Options = { role?: string; tenant?: string; refreshing?: boolean; refreshError?: unknown; company?: AdminStore["company"] };
function Shell({ role = "Owner", tenant = "tenant-a", refreshing = false, refreshError = null, company = store.company }: Options) {
  const [selected, setSelected] = useState(store), navigate = useNavigate();
  return <SessionContext value={{ user: { id: "staff", email: "staff@example.test", tenantId: tenant, role }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => { setSelected({ ...store, id: "store-b" }); navigate("/stores/store-b/tax-classes"); }}>Other store</button>
    <Outlet context={{ store: { ...selected, company }, storeRefreshing: refreshing, storeRefreshError: refreshError, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function app(options: Options = {}, path = "/stores/store-a/tax-classes") { return <MemoryRouter initialEntries={[path]}><Routes><Route element={<Shell {...options} />}>
  <Route path="/stores/:storeId/tax-classes" element={<StoreTaxClassesPage />} /><Route path="/stores/:storeId" element={<StoreOverviewPage />} />
  </Route></Routes></MemoryRouter>; }
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (error: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
async function ready() { await screen.findByRole("heading", { name: "Books" }); }
const save = () => screen.getByRole("button", { name: "Save tax changes" });
beforeAll(async () => { HTMLElement.prototype.scrollIntoView = vi.fn(); await initializeI18n(); });
beforeEach(async () => {
  await i18n.changeLanguage("en"); rows = [{ ...books, rates: books.rates.map(rate => ({ ...rate })) }];
  vi.spyOn(api, "taxClasses").mockImplementation(async id => id === "store-a" ? structuredClone(rows) : []);
  vi.spyOn(api, "createTaxClass").mockImplementation(async (_store, code, name) => { const saved = { code, name, rates: [] }; rows.push(saved); return saved; });
  vi.spyOn(api, "renameTaxClass").mockImplementation(async (_store, code, name) => { rows = rows.map(row => row.code === code ? { ...row, name } : row); return { code, name, rates: [] }; });
  vi.spyOn(api, "deleteTaxClass").mockImplementation(async (_store, code) => { rows = rows.filter(row => row.code !== code); });
  vi.spyOn(api, "setTaxRate").mockImplementation(async (_store, code, country, rate) => { rows = rows.map(row => row.code === code ? { ...row, rates: [...row.rates.filter(row => row.country !== country), { country, rate }] } : row); return { country, rate }; });
  vi.spyOn(api, "removeTaxRate").mockImplementation(async (_store, code, country) => { rows = rows.map(row => row.code === code ? { ...row, rates: row.rates.filter(row => row.country !== country) } : row); });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("explains saved destination/home/fallback behavior, gross prices and zero without suggesting rates", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByText("How these rates apply"));
  expect(screen.getByText(/delivery address's country/)).toBeTruthy(); expect(screen.getByText(/configured 0% rate is a real rate/)).toBeTruthy();
  expect(screen.getByText(/do not edit product or delivery prices/)).toBeTruthy(); expect(screen.getByText("Store company country: CZ")).toBeTruthy();
  await user.click(screen.getByRole("button", { name: "Add country rate for books" }));
  expect(screen.getByRole("textbox", { name: "VAT rate (%)" })).toHaveProperty("value", "");
});
it("does not assume a home country from language or currency", async () => {
  render(app({ company: null })); await ready(); expect(screen.getByText(/no valid country code/)).toBeTruthy(); expect(screen.queryByText("Store company country: CZ")).toBeNull();
});
it("keeps pending, failed and empty reads distinct and retries only GET", async () => {
  const pending = deferred<TaxClass[]>(); vi.mocked(api.taxClasses).mockReturnValueOnce(pending.promise);
  const user = userEvent.setup(); render(app()); expect(screen.queryByText(/No tax classes are configured/)).toBeNull();
  await act(async () => pending.reject(new HttpError("http", 503))); await screen.findByRole("alert");
  expect(screen.queryByText(/No tax classes are configured/)).toBeNull(); rows = [];
  await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByText(/No tax classes are configured/);
  expect(api.taxClasses).toHaveBeenCalledTimes(2); expect(api.createTaxClass).not.toHaveBeenCalled();
});
it("generates a visible canonical code from Czech text and creates only after explicit submit", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Create tax class" }));
  await user.type(screen.getByRole("textbox", { name: "Class name" }), "Tištěné knihy");
  expect(screen.getByText("Code to save: tistene-knihy")).toBeTruthy(); expect(api.createTaxClass).not.toHaveBeenCalled();
  await user.click(save()); expect(await screen.findByText("Tax class tistene-knihy created.")).toBeTruthy();
  expect(api.createTaxClass).toHaveBeenCalledExactlyOnceWith("store-a", "tistene-knihy", "Tištěné knihy");
});
it("focuses invalid fields and prevents known duplicate or invalid class codes", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Create tax class" }));
  await user.click(save()); expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Class name" }));
  await user.type(screen.getByRole("textbox", { name: "Class name" }), "Books"); await user.click(save());
  expect(screen.getByText("This class code is already used in the store.")).toBeTruthy();
  const code = screen.getByRole("textbox", { name: "Class code" }); await user.type(code, "BAD CODE"); await user.click(save());
  expect(document.activeElement).toBe(code); expect(api.createTaxClass).not.toHaveBeenCalled();
});
it("retains a class draft and server duplicate refusal after refreshing the collection", async () => {
  vi.mocked(api.createTaxClass).mockRejectedValue(new HttpError("http", 409));
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Create tax class" }));
  await user.type(screen.getByRole("textbox", { name: "Class name" }), "Food"); await user.click(save());
  await screen.findByText(/code is already used in the store. Review/);
  expect(screen.getByRole("textbox", { name: "Class name" })).toHaveProperty("value", "Food"); expect(api.createTaxClass).toHaveBeenCalledOnce(); expect(api.taxClasses).toHaveBeenCalledTimes(2);
});
it("renames only the label, keeps the code fixed and re-reads the saved rates", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Rename class books" }));
  expect(screen.queryByRole("textbox", { name: "Class code" })).toBeNull(); const name = screen.getByRole("textbox", { name: "Class name" });
  await user.clear(name); await user.type(name, "Printed books"); await user.click(save());
  await screen.findByRole("heading", { name: "Printed books" });
  expect(api.renameTaxClass).toHaveBeenCalledExactlyOnceWith("store-a", "books", "Printed books"); expect(screen.getByText("CZ", { selector: "strong" })).toBeTruthy();
  expect(rows[0].rates).toEqual(books.rates);
});
it("keeps an acknowledged rename and existing rates when its refresh fails", async () => {
  const user = userEvent.setup(); render(app()); await ready(); vi.mocked(api.taxClasses).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(screen.getByRole("button", { name: "Rename class books" })); await user.click(save());
  await screen.findByRole("alert"); expect(screen.getByText(/renamed. Its saved country rates are retained/)).toBeTruthy();
  expect(screen.getByText("CZ", { selector: "strong" })).toBeTruthy(); expect(screen.getByRole("button", { name: "Add country rate for books" })).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Add country rate for books" })).toHaveProperty("disabled", false));
  expect(api.renameTaxClass).toHaveBeenCalledOnce();
});
it.each(["0", "12,50"])("saves an explicit country percentage without changing other rates (%s)", async value => {
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Add country rate for books" }));
  await user.type(screen.getByRole("textbox", { name: "Country code" }), "ie"); await user.type(screen.getByRole("textbox", { name: "VAT rate (%)" }), value);
  await user.click(save()); await screen.findByText("Country rate IE saved for books.");
  expect(api.setTaxRate).toHaveBeenCalledExactlyOnceWith("store-a", "books", "IE", Number(value.replace(",", ".")));
  expect(rows[0].rates.filter(rate => rate.country !== "IE")).toEqual(books.rates);
});
it("edits an existing country without offering a silent country move", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Edit rate DE for books" }));
  expect(screen.queryByRole("textbox", { name: "Country code" })).toBeNull(); const rate = screen.getByRole("textbox", { name: "VAT rate (%)" });
  await user.clear(rate); await user.type(rate, "0.29"); await user.click(save()); await screen.findByText("Country rate DE saved for books.");
  expect(api.setTaxRate).toHaveBeenCalledExactlyOnceWith("store-a", "books", "DE", 0.29);
});
it.each([{ country: "CZE", rate: "7" }, { country: "ČZ", rate: "7" }, { country: "CZ", rate: "7" }, { country: "IE", rate: "100.01" }, { country: "IE", rate: "0.001" }, { country: "IE", rate: "1e1" }])("blocks invalid/duplicate country-rate drafts: %j", async draft => {
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Add country rate for books" }));
  await user.type(screen.getByRole("textbox", { name: "Country code" }), draft.country); await user.type(screen.getByRole("textbox", { name: "VAT rate (%)" }), draft.rate); await user.click(save());
  expect(api.setTaxRate).not.toHaveBeenCalled(); expect(screen.getByRole("form").querySelector('[aria-invalid="true"]')).toBeTruthy();
});
it("retains a refused rate draft and focuses the localized server field issue", async () => {
  vi.mocked(api.setTaxRate).mockRejectedValue(new HttpError("http", 400, { errors: { rate: ["Wrong rate"] } }));
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Edit rate DE for books" }));
  await user.click(save()); const issue = await screen.findByRole("button", { name: "Enter a rate from 0 to 100 with at most two decimals." });
  await user.click(issue); expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "VAT rate (%)" }));
  expect(screen.getByRole("textbox", { name: "VAT rate (%)" })).toHaveProperty("value", "7"); expect(api.setTaxRate).toHaveBeenCalledOnce();
});
it("confirms removing a configured zero separately from setting it to zero", async () => {
  rows[0].rates[1].rate = 0; const user = userEvent.setup(); render(app()); await ready();
  await user.click(screen.getByRole("button", { name: "Remove rate DE for books" }));
  expect(within(screen.getByRole("form")).getByText("DE · 0 %")).toBeTruthy(); expect(api.removeTaxRate).not.toHaveBeenCalled();
  await user.click(within(screen.getByRole("form")).getByRole("button", { name: "Remove rate" }));
  await screen.findByText("Country rate DE removed from books."); expect(api.removeTaxRate).toHaveBeenCalledExactlyOnceWith("store-a", "books", "DE");
  expect(rows[0].rates).toEqual([books.rates[0]]);
});
it("requires confirmation for class deletion and restores focus on Escape", async () => {
  const user = userEvent.setup(); render(app()); await ready(); const opener = screen.getByRole("button", { name: "Delete class books" });
  await user.click(opener); expect(screen.getByRole("form").querySelector("h2")).toBe(document.activeElement);
  await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(opener)); expect(api.deleteTaxClass).not.toHaveBeenCalled();
  await user.click(opener); await user.click(within(screen.getByRole("form")).getByRole("button", { name: "Delete class" }));
  await screen.findByText("Tax class books deleted."); expect(api.deleteTaxClass).toHaveBeenCalledExactlyOnceWith("store-a", "books");
});
it("shows an in-use deletion refusal with the returned count as inert text", async () => {
  vi.mocked(api.deleteTaxClass).mockRejectedValue(new HttpError("http", 409, { detail: "It cannot be deleted because 2 listings are taxed as it. <script>unsafe</script>" }));
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Delete class books" }));
  await user.click(within(screen.getByRole("form")).getByRole("button", { name: "Delete class" }));
  await screen.findByText(/class is still assigned/); await user.click(screen.getByText("Server explanation"));
  expect(screen.getByText(/2 listings are taxed as it/)).toBeTruthy(); expect(document.querySelector("script")).toBeNull();
  expect(screen.getByRole("form")).toBeTruthy(); expect(api.deleteTaxClass).toHaveBeenCalledOnce();
});
it("invalidates an open rate draft after a refreshed class or rate changes", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Edit rate DE for books" }));
  rows[0].rates[1].rate = 5; await user.click(screen.getByRole("button", { name: "Refresh tax classes" })); await screen.findByText(/changed while the draft was open/);
  expect(save()).toHaveProperty("disabled", true); expect(screen.getByRole("textbox", { name: "VAT rate (%)" })).toHaveProperty("value", "7"); expect(api.setTaxRate).not.toHaveBeenCalled();
});
it("retains a lost-create draft, refreshes inventory and prevents a duplicate replay", async () => {
  vi.mocked(api.createTaxClass).mockImplementation(async (_store, code, name) => { rows.push({ code, name, rates: [] }); throw new HttpError("network", null); });
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Create tax class" }));
  await user.type(screen.getByRole("textbox", { name: "Class name" }), "Food"); await user.click(save()); await screen.findByText(/may have been saved/);
  await screen.findByRole("heading", { name: "Food" }); await user.click(save()); expect(api.createTaxClass).toHaveBeenCalledOnce();
  expect(screen.getByRole("textbox", { name: "Class name" })).toHaveProperty("value", "Food");
});
it("blocks another write after an uncertain rate change and failed read, then reviews the changed snapshot", async () => {
  const user = userEvent.setup(); render(app()); await ready();
  vi.mocked(api.taxClasses).mockRejectedValueOnce(new HttpError("http", 503));
  vi.mocked(api.setTaxRate).mockImplementation(async () => { rows[0].rates[1].rate = 0; throw new HttpError("network", null); });
  await user.click(screen.getByRole("button", { name: "Edit rate DE for books" })); await user.click(save());
  await screen.findByText(/may have been saved/); expect(save()).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByText(/changed while the draft was open/); expect(save()).toHaveProperty("disabled", true);
  expect(api.setTaxRate).toHaveBeenCalledOnce();
});
it("locks all writes against duplicate submissions while a change is pending", async () => {
  const pending = deferred<TaxClass>(); vi.mocked(api.renameTaxClass).mockReturnValue(pending.promise);
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Rename class books" }));
  const form = screen.getByRole("form"); fireEvent.submit(form); fireEvent.submit(form); expect(api.renameTaxClass).toHaveBeenCalledOnce();
  expect(screen.getByRole("button", { name: "Cancel" })).toHaveProperty("disabled", true); await act(async () => pending.resolve({ ...books, rates: [] }));
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("honors tax read/write permissions for %s", async role => {
  render(app({ role })); const readable = ["Owner", "Admin", "CatalogManager"].includes(role);
  if (readable) { await ready(); expect(screen.queryByRole("button", { name: "Create tax class" }) !== null).toBe(role !== "CatalogManager"); }
  else { await screen.findByRole("heading", { name: i18n.t("errors:permissionTitle") }); expect(api.taxClasses).not.toHaveBeenCalled(); }
});
it.each([{ refreshing: true }, { refreshError: new HttpError("http", 503) }])("blocks writes with stale store facts: %j", async options => {
  render(app(options)); await ready(); expect(screen.getByRole("button", { name: "Create tax class" })).toHaveProperty("disabled", true);
});
it("aborts old reads when switching stores and discards late tax classes", async () => {
  const pending = deferred<TaxClass[]>(); vi.mocked(api.taxClasses).mockReturnValueOnce(pending.promise);
  const user = userEvent.setup(); render(app()); const signal = vi.mocked(api.taxClasses).mock.calls[0][1]!;
  await user.click(screen.getByRole("button", { name: "Other store" })); await screen.findByText(/No tax classes are configured/); expect(signal.aborted).toBe(true);
  await act(async () => pending.resolve([books])); expect(screen.queryByRole("heading", { name: "Books" })).toBeNull();
});
it("keeps a late old-store write result and refresh out of the new store", async () => {
  const pending = deferred<TaxClass>(); vi.mocked(api.renameTaxClass).mockReturnValue(pending.promise);
  const user = userEvent.setup(); render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Rename class books" })); await user.click(save());
  await user.click(screen.getByRole("button", { name: "Other store" })); await screen.findByText(/No tax classes are configured/); await act(async () => pending.resolve({ ...books, rates: [] }));
  expect(api.taxClasses).toHaveBeenCalledTimes(2); expect(screen.queryByText(/Tax class books renamed/)).toBeNull();
});
it.each([{ role: "CatalogManager" }, { tenant: "tenant-b" }])("discards drafts after a session scope change: %j", async scope => {
  const user = userEvent.setup(); const view = render(app()); await ready(); await user.click(screen.getByRole("button", { name: "Rename class books" }));
  view.rerender(app(scope)); await ready(); expect(screen.queryByRole("form")).toBeNull(); expect(api.taxClasses).toHaveBeenCalledTimes(2);
});
it.each(["Owner", "CatalogManager", "Support"])("exposes tax navigation only to authorized readers (%s)", async role => {
  const view = render(<MemoryRouter><AdminNavigation stores={{ status: "ready", data: [store], refreshing: false, refreshError: null }} reloadStores={vi.fn()} selectedStore={store} role={role} /></MemoryRouter>);
  expect(screen.queryByRole("link", { name: "Tax classes" }) !== null).toBe(role !== "Support"); view.unmount();
  render(app({ role }, "/stores/store-a")); expect(screen.queryByRole("link", { name: "Tax classes" }) !== null).toBe(role !== "Support");
});
it.each(["cs", "en"])("localizes tax controls and retains production-compatible markup (%s)", async language => {
  await i18n.changeLanguage(language); render(app()); await ready();
  expect(screen.getByRole("heading", { name: i18n.t("tax:title"), level: 1 })).toBeTruthy(); expect(screen.getByText(i18n.t("tax:homeCountry", { country: "CZ" }))).toBeTruthy();
  expect(document.querySelector("style, [style], script")).toBeNull();
});
