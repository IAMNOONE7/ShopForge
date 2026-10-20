// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, renderHook, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminStore, type Category, type StoreProduct } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { StoreLayout } from "../layouts/StoreLayout";
import { SessionContext } from "../session";
import { useBulkListings } from "../useBulkListings";
import { ListingsPage } from "./ListingPages";

const store: AdminStore = { id: "store-a", name: "Store A", currency: "CZK", culture: "cs-CZ", status: "draft", theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 4 }, logoUrl: null, primaryHostName: null, returnWindowDays: 14, company: null };
const item: StoreProduct = { id: "a", productId: "physical-a", sku: "SKU-A", name: "Chair A", slug: "chair-a", description: null, price: 100, vatRate: 21, isVisible: true, sortOrder: 0, categoryIds: [], archivedAt: null, taxClass: "standard", seoTitle: null, seoDescription: null, seoSocialImageUrl: null, seoNoIndex: false };
const second = { ...item, id: "b", name: "Chair B", sku: "SKU-B" }, third = { ...item, id: "c", name: "Chair C", sku: "SKU-C" };
const category: Category = { id: "old", name: "Furniture", slug: "furniture", parentId: null, sortOrder: 0, attributeIds: [], seoTitle: null, seoDescription: null, pageText: null };
const sale = { ...category, id: "sale", name: "Sale" }, retiredCategory = { ...category, id: "retired", name: "Old category" };
function reads() {
  vi.spyOn(api, "taxClasses").mockResolvedValue([{ code: "books", name: "Books", rates: [{ country: "IE", rate: 0 }] }]);
  vi.spyOn(api, "storeProducts").mockImplementation(async (_s, _signal, query = {}) => ({ items: query.page === 2 ? [second, third] : [item, second], page: query.page ?? 1, pageSize: 25, totalCount: 26, hasMore: query.page !== 2 }));
  vi.spyOn(api, "categories").mockImplementation(async (_s, _signal, archived) => archived ? [retiredCategory] : [category, sale]);
  return vi.spyOn(api, "changeBulkListings").mockImplementation(async (_s, ids) => ({ asked: ids.length, changed: ids.length }));
}
function route(path = "/stores/store-a/products", role = "Owner") {
  return render(<SessionContext.Provider value={{ user: { id: "u", email: "owner@example.test", role, tenantId: "tenant" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[path]}><Link to="/stores/store-b/products">Switch store</Link><Routes>
      <Route element={<Outlet context={{ stores: { status: "ready", data: [store, { ...store, id: "store-b", name: "Store B" }], refreshing: false, refreshError: null }, reloadStores: () => undefined }} />}>
        <Route path="/stores/:storeId" element={<StoreLayout />}><Route path="products" element={<ListingsPage />} /></Route>
      </Route></Routes></MemoryRouter></SessionContext.Provider>);
}
async function selectPage(user: ReturnType<typeof userEvent.setup>) { const button = await screen.findByRole("button", { name: "Select this page" }); await waitFor(() => expect(button.matches(":disabled")).toBe(false)); await user.click(button); }
async function review(user: ReturnType<typeof userEvent.setup>) { await user.click(screen.getByRole("button", { name: "Review change" })); return screen.getByRole("form", { name: /selected listings/ }); }
async function apply(user: ReturnType<typeof userEvent.setup>) { const form = await review(user); await user.click(within(form).getByRole("checkbox")); await user.click(within(form).getByRole("button", { name: "Apply to selected listings" })); }
beforeAll(async () => { await initializeI18n(); });
beforeEach(async () => { await i18n.changeLanguage("en"); vi.stubGlobal("CSS", { escape: (s: string) => s }); HTMLElement.prototype.scrollIntoView = vi.fn(); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it("retains explicit deduplicated selections across pages, search and sort", async () => {
  const user = userEvent.setup(); const change = reads(); route(); await selectPage(user); expect(screen.getByText("Selected: 2 / 500")).toBeTruthy();
  expect(api.categories).not.toHaveBeenCalled(); await user.click(screen.getByRole("button", { name: "Next page" })); await screen.findByRole("link", { name: "Chair C" });
  await selectPage(user); expect(screen.getByText("Selected: 3 / 500")).toBeTruthy(); expect(screen.getByText("Selected outside this page: 1")).toBeTruthy();
  await user.type(screen.getByRole("textbox", { name: "Search listings" }), "CHAIR"); await user.click(screen.getByRole("button", { name: "Search" }));
  await waitFor(() => expect(api.storeProducts).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, pageSize: 25, q: "CHAIR", sort: "default" }));
  await user.selectOptions(screen.getByRole("combobox", { name: "Sort listings" }), "-price"); await waitFor(() => expect(screen.getByRole("button", { name: "Review change" }).matches(":disabled")).toBe(false));
  const form = await review(user); expect(document.activeElement?.textContent).toBe("Hide listings — 3 selected listings");
  fireEvent.submit(form); expect(change).not.toHaveBeenCalled(); await user.click(within(form).getByRole("checkbox")); await user.click(within(form).getByRole("button", { name: "Apply to selected listings" }));
  await screen.findByText("Asked: 3. Changed: 3."); expect(change).toHaveBeenCalledWith("store-a", ["a", "b", "c"], { action: "visibility", isVisible: false });
  expect(screen.getByText("Selected: 0 / 500")).toBeTruthy(); expect(api.storeProducts).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, pageSize: 25, q: "CHAIR", sort: "-price" });
});
it("can deselect a page or clear the exact selection without sending a write", async () => {
  const user = userEvent.setup(); const change = reads(); route(); await selectPage(user); await user.click(screen.getByRole("button", { name: "Deselect this page" })); expect(screen.getByText("Selected: 0 / 500")).toBeTruthy();
  await user.click(screen.getByRole("checkbox", { name: "Select Chair A (SKU-A)" })); await user.click(screen.getByRole("button", { name: "Clear selection" })); expect(screen.getByText("Selected: 0 / 500")).toBeTruthy(); expect(change).not.toHaveBeenCalled();
});
it.each(["?archived=true", "store"])("clears selection and drafts on collection/store departure: %s", async destination => {
  const user = userEvent.setup(); reads(); route(); await selectPage(user); await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "setPrice"); await user.type(screen.getByRole("textbox", { name: "New price (CZK)" }), "99,50");
  await user.click(screen.getByRole("link", { name: destination === "store" ? "Switch store" : "Archived" })); await screen.findByRole("button", { name: "Select this page" }); expect(screen.getByText("Selected: 0 / 500")).toBeTruthy(); expect(screen.queryByRole("textbox", { name: "New price (CZK)" })).toBeNull();
});
it("bounds and deduplicates all selection entry points to 500 IDs", () => {
  const { result } = renderHook(() => useBulkListings("store-a", false, true, vi.fn()));
  const rows = Array.from({ length: 500 }, (_, n) => ({ id: `id-${n}`, sku: `SKU-${n}`, name: `Listing ${n}` }));
  act(() => result.current.toggle([...rows, rows[0]], true)); expect(result.current.selection.size).toBe(500);
  act(() => result.current.toggle([{ ...rows[0], id: "extra" }], true)); expect(result.current.selection.size).toBe(500); expect(result.current.selection.has("extra")).toBe(false);
  act(() => result.current.toggle([rows[0]], false)); act(() => result.current.toggle([{ ...rows[0], id: "extra" }], true)); expect(result.current.selection.size).toBe(500); expect(result.current.selection.has("extra")).toBe(true);
});
it.each([["setPrice", "0", { action: "price", set: 0 }], ["changePrice", "-10,5", { action: "price", byPercent: -10.5 }]] as const)("confirms %s without replacing other fields", async (action, value, payload) => {
  const user = userEvent.setup(); const change = reads(); route(); await selectPage(user); await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), action);
  await user.type(screen.getByRole("textbox", { name: action === "setPrice" ? "New price (CZK)" : "Percentage change (%)" }), value); await apply(user);
  await screen.findByText("Asked: 2. Changed: 2."); expect(change).toHaveBeenCalledWith("store-a", ["a", "b"], payload);
});
it.each([["setPrice", "1.001"], ["changePrice", "-100"]])("retains invalid %s input without requesting a change", async (action, value) => {
  const user = userEvent.setup(); const change = reads(); route(); await selectPage(user); await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), action);
  const field = screen.getByRole("textbox", { name: action === "setPrice" ? "New price (CZK)" : "Percentage change (%)" }); await user.type(field, value); await user.click(screen.getByRole("button", { name: "Review change" }));
  expect(document.activeElement).toBe(field); expect(field.getAttribute("aria-invalid")).toBe("true"); expect(change).not.toHaveBeenCalled();
});
it("adds/removes categories independently and only removes archived categories", async () => {
  const user = userEvent.setup(); const change = reads(); route(); await selectPage(user); await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "categories");
  const old = await screen.findByRole("combobox", { name: "Furniture" }); await user.selectOptions(old, "remove"); await user.selectOptions(screen.getByRole("combobox", { name: "Sale" }), "add");
  const retired = screen.getByRole("combobox", { name: /Old category.*archived/ }); expect(within(retired).queryByRole("option", { name: "Add to category" })).toBeNull(); await user.selectOptions(retired, "remove");
  await apply(user); await screen.findByText("Asked: 2. Changed: 2."); expect(change).toHaveBeenCalledWith("store-a", ["a", "b"], { action: "categories", addCategoryIds: ["sale"], removeCategoryIds: ["old", "retired"] });
});
it("requires category intent, reports read failure, and can switch from category choices to price", async () => {
  const user = userEvent.setup(); const change = reads(); vi.mocked(api.categories).mockRejectedValueOnce(new HttpError("http", 503)); route(); await selectPage(user);
  await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "categories"); await screen.findByRole("alert"); expect(screen.getByRole("button", { name: "Review change" }).matches(":disabled")).toBe(true);
  await user.click(screen.getByRole("button", { name: "Try again" })); const saleField = await screen.findByRole("combobox", { name: "Sale" });
  await user.click(screen.getByRole("button", { name: "Review change" })); await screen.findByText(/Choose at least one valid/); expect(change).not.toHaveBeenCalled(); await user.selectOptions(saleField, "add");
  await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "setPrice"); await user.type(screen.getByRole("textbox", { name: "New price (CZK)" }), "12,50"); await apply(user);
  await screen.findByText("Asked: 2. Changed: 2."); expect(change).toHaveBeenCalledWith("store-a", ["a", "b"], { action: "price", set: 12.5 });
});
it("keeps drafts after a definitive refusal, maps field errors and requires a new confirmation", async () => {
  const user = userEvent.setup(); const change = reads(); change.mockRejectedValueOnce(new HttpError("http", 400, { errors: { Set: ["Invalid amount"] } })); route(); await selectPage(user);
  await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "setPrice"); await user.type(screen.getByRole("textbox", { name: "New price (CZK)" }), "19,50"); await apply(user);
  await screen.findByText(/The request was refused/); await user.click(screen.getByRole("button", { name: "Review the price or percentage." })); const field = screen.getByRole("textbox", { name: "New price (CZK)" }); expect(document.activeElement).toBe(field); expect((field as HTMLInputElement).value).toBe("19,50"); expect(screen.getByText("Selected: 2 / 500")).toBeTruthy(); expect(change).toHaveBeenCalledTimes(1);
  await waitFor(() => expect(screen.getByRole("button", { name: "Review change" }).matches(":disabled")).toBe(false)); await apply(user); await screen.findByText("Asked: 2. Changed: 2."); expect(change).toHaveBeenCalledTimes(2);
});
it.each([0, 1])("reports changed=%s as aggregate information and clears the submitted selection", async changed => {
  const user = userEvent.setup(); const change = reads(); change.mockResolvedValueOnce({ asked: 2, changed }); route(); await selectPage(user); await apply(user);
  await screen.findByText(`Asked: 2. Changed: ${changed}.`); expect(screen.getByText(/does not identify individual outcomes/)).toBeTruthy(); expect(screen.getByText("Selected: 0 / 500")).toBeTruthy(); expect(document.activeElement).toBe(document.getElementById("bulk-outcome"));
});
it.each([new HttpError("network", null), new HttpError("invalid-response", null), new HttpError("http", 503), new HttpError("http", 408), new HttpError("http", 409), new HttpError("http", 499)])("blocks reuse of unconfirmed changes and read retries never replay", async error => {
  const user = userEvent.setup(); const change = reads(); change.mockRejectedValueOnce(error); route(); await selectPage(user);
  await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "changePrice"); await user.type(screen.getByRole("textbox", { name: "Percentage change (%)" }), "5");
  vi.mocked(api.storeProducts).mockRejectedValueOnce(new HttpError("http", 503)); await apply(user); await screen.findByText(/The outcome is not confirmed/);
  const finish = screen.getByRole("button", { name: "Finish review and clear selection" }); expect(finish.matches(":disabled")).toBe(true); expect(screen.queryByRole("button", { name: "Apply to selected listings" })).toBeNull();
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(finish.matches(":disabled")).toBe(false)); expect(change).toHaveBeenCalledTimes(1);
  await user.click(finish); expect(screen.getByText("Selected: 0 / 500")).toBeTruthy(); expect(change).toHaveBeenCalledTimes(1);
});
it("locks duplicate requests and reloads the current query after a held write", async () => {
  const user = userEvent.setup(); const change = reads(); let release!: () => void; change.mockImplementation(() => new Promise(resolve => { release = () => resolve({ asked: 2, changed: 2 }); })); route(); await selectPage(user);
  const form = await review(user); await user.click(within(form).getByRole("checkbox")); fireEvent.submit(form); fireEvent.submit(form); expect(change).toHaveBeenCalledTimes(1); expect(screen.getByRole("button", { name: "Clear selection" }).matches(":disabled")).toBe(true);
  expect(screen.getByRole("button", { name: "Back to settings" }).matches(":disabled")).toBe(true); await user.selectOptions(screen.getByRole("combobox", { name: "Sort listings" }), "name");
  await waitFor(() => expect(api.storeProducts).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, pageSize: 25, q: "", sort: "name" })); const count = vi.mocked(api.storeProducts).mock.calls.length;
  await act(async () => release()); await screen.findByText("Asked: 2. Changed: 2."); await waitFor(() => expect(api.storeProducts).toHaveBeenCalledTimes(count + 1)); expect(api.storeProducts).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, pageSize: 25, q: "", sort: "name" });
});
it("ignores late results after a store switch and clears the new store selection", async () => {
  const user = userEvent.setup(); const change = reads(); let release!: () => void; change.mockImplementation(() => new Promise(resolve => { release = () => resolve({ asked: 2, changed: 2 }); })); route(); await selectPage(user); await apply(user);
  await user.click(screen.getByRole("link", { name: "Switch store" })); await screen.findByText(/visibility in Store B/); const count = vi.mocked(api.storeProducts).mock.calls.length; await act(async () => release());
  expect(screen.queryByText("Asked: 2. Changed: 2.")).toBeNull(); expect(screen.getByText("Selected: 0 / 500")).toBeTruthy(); expect(api.storeProducts).toHaveBeenCalledTimes(count);
});
it("uses only restore on the archived collection and sends the false archive flag", async () => {
  const user = userEvent.setup(); const change = reads(); route("/stores/store-a/products?archived=true"); await selectPage(user); const select = screen.getByRole("combobox", { name: "Bulk action" });
  expect(within(select).getAllByRole("option").map(option => option.textContent)).toEqual(["Restore listings"]); await apply(user); await screen.findByText("Asked: 2. Changed: 2."); expect(change).toHaveBeenCalledWith("store-a", ["a", "b"], { action: "archive", archived: false }); expect(api.storeProducts).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), expect.objectContaining({ archived: true }));
});
it("returns keyboard focus to settings on Escape without applying", async () => {
  const user = userEvent.setup(); const change = reads(); route(); await selectPage(user); await review(user); await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("combobox", { name: "Bulk action" }))); expect(change).not.toHaveBeenCalled();
});
describe.each(["Support", "OrderManager", "Warehouse", "unknown"])("%s", role => {
  it("reads listings without bulk controls or category reads", async () => { reads(); route(undefined, role); await screen.findByRole("link", { name: "Chair A" }); expect(screen.queryByRole("checkbox")).toBeNull(); expect(screen.queryByRole("heading", { name: "Bulk listing changes" })).toBeNull(); expect(api.categories).not.toHaveBeenCalled(); });
});
it.each(["Owner", "Admin", "CatalogManager"])("allows %s to use bulk controls", async role => { reads(); route(undefined, role); expect(await screen.findByRole("checkbox", { name: "Select Chair A (SKU-A)" })).toBeTruthy(); });
it("provides Czech confirmation with unchanged merchant names", async () => {
  const user = userEvent.setup(); await i18n.changeLanguage("cs"); reads(); route(); await user.click(await screen.findByRole("button", { name: "Vybrat tuto stránku" })); await user.click(screen.getByRole("button", { name: "Zkontrolovat změnu" }));
  expect(screen.getByRole("heading", { name: "Skrýt nabídky — vybráno 2 nabídek" })).toBeTruthy(); expect(within(document.querySelector(".listing-list")!).getByRole("link", { name: "Chair A" })).toBeTruthy(); expect(screen.getByRole("checkbox", { name: "Zkontroloval(a) jsem akci a všechny vybrané nabídky (2)." })).toBeTruthy();
});

it.each([["show", { action: "visibility", isVisible: true }], ["archive", { action: "archive", archived: true }]] as const)("applies %s to the exact set and refreshes the original collection", async (action, payload) => {
  const user = userEvent.setup(); const change = reads(); route(); await selectPage(user); await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), action); await apply(user);
  await screen.findByText("Asked: 2. Changed: 2."); expect(change).toHaveBeenCalledWith("store-a", ["a", "b"], payload); expect(api.storeProducts).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, pageSize: 25, q: "", sort: "default" });
});
it("preserves removed category choices after refusal and prevents a stale addition", async () => {
  const user = userEvent.setup(); const change = reads(); change.mockRejectedValueOnce(new HttpError("http", 400, { errors: { AddCategoryIds: ["Not found"], RemoveCategoryIds: ["Not available"] } })); route(); await selectPage(user);
  await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "categories"); await user.selectOptions(await screen.findByRole("combobox", { name: "Sale" }), "add");
  vi.mocked(api.categories).mockResolvedValue([]); await apply(user); await screen.findByText("Category no longer available: sale"); expect(screen.getAllByRole("button", { name: "Review the category choices." })).toHaveLength(1);
  await user.click(screen.getByRole("button", { name: "Review change" })); await screen.findByText(/Choose at least one valid addition/); expect(change).toHaveBeenCalledTimes(1);
  await user.click(screen.getByRole("button", { name: "Remove unavailable choice sale" })); expect(screen.queryByText("Category no longer available: sale")).toBeNull();
});
it("clears earlier result feedback when a new selection begins", async () => {
  const user = userEvent.setup(); reads(); route(); await selectPage(user); await apply(user); await screen.findByText("Asked: 2. Changed: 2.");
  await selectPage(user); expect(screen.queryByText("Asked: 2. Changed: 2.")).toBeNull(); expect(document.querySelectorAll('.bulk-selected')).toHaveLength(1); expect(screen.getByText("Selected: 2 / 500")).toBeTruthy();
});


it.each(["books", ""])("reviews and applies explicit bulk tax assignment %s with only selected IDs", async value => {
  const user = userEvent.setup(), change = reads(); route("/stores/store-a/products", "CatalogManager"); await selectPage(user);
  await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "taxClass");
  await screen.findByRole("option", { name: "Books (books)" });
  expect(screen.getByRole("button", { name: "Review change" }).matches(":disabled")).toBe(true);
  fireEvent.submit(screen.getByRole("form", { name: "Bulk change settings" })); expect(change).not.toHaveBeenCalled();
  await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), value);
  const form = await review(user); expect(within(form).getByText(value ? "Books (books)" : "Clear class — keep each listing’s fallback VAT")).toBeTruthy();
  expect(within(form).getByText(/gross price, fallback VAT rate/)).toBeTruthy();
  await user.click(within(form).getByRole("checkbox")); await user.click(within(form).getByRole("button", { name: "Apply to selected listings" }));
  await screen.findByText("Asked: 2. Changed: 2."); expect(change).toHaveBeenCalledExactlyOnceWith("store-a", ["a", "b"], { action: "tax-class", taxClass: value || null });
});
it("keeps a missing bulk class visible after refusal and requires replacement or explicit clearing", async () => {
  const user = userEvent.setup(), change = reads(); change.mockRejectedValueOnce(new HttpError("http", 400, { errors: { taxClass: ["Unknown class"] } }));
  route(); await selectPage(user); await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "taxClass"); await screen.findByRole("option", { name: "Books (books)" });
  await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), "books"); vi.mocked(api.taxClasses).mockResolvedValue([]); await apply(user);
  await screen.findByText(/Class books is no longer available/); expect(screen.getByRole("combobox", { name: "Tax class" })).toHaveProperty("value", "books");
  expect(screen.getByRole("button", { name: "Review change" }).matches(":disabled")).toBe(true); expect(change).toHaveBeenCalledOnce();
  await user.click(screen.getByRole("button", { name: "Review the tax-class choice." })); expect(document.activeElement).toBe(screen.getByRole("combobox", { name: "Tax class" }));
  await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), ""); await apply(user); await screen.findByText("Asked: 2. Changed: 2.");
  expect(change).toHaveBeenLastCalledWith("store-a", ["a", "b"], { action: "tax-class", taxClass: null });
});
it("permits deliberate bulk clearing during a failed lookup without treating failure as an empty class list", async () => {
  const user = userEvent.setup(), change = reads(); vi.mocked(api.taxClasses).mockRejectedValue(new HttpError("http", 503)); route(); await selectPage(user);
  await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "taxClass"); await screen.findByRole("alert");
  expect(screen.getByRole("combobox", { name: "Tax class" })).toHaveProperty("value", "?"); expect(screen.getByRole("button", { name: "Review change" }).matches(":disabled")).toBe(true);
  await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), ""); await apply(user); await screen.findByText("Asked: 2. Changed: 2.");
  expect(change).toHaveBeenCalledExactlyOnceWith("store-a", ["a", "b"], { action: "tax-class", taxClass: null });
});
it("never replays an uncertain bulk tax clear when listing reads recover", async () => {
  const user = userEvent.setup(), change = reads(); change.mockRejectedValueOnce(new HttpError("network", null)); route(); await selectPage(user);
  await user.selectOptions(screen.getByRole("combobox", { name: "Bulk action" }), "taxClass"); await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), "");
  vi.mocked(api.storeProducts).mockRejectedValueOnce(new HttpError("http", 503)); await apply(user); await screen.findByText(/The outcome is not confirmed/);
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Finish review and clear selection" }).matches(":disabled")).toBe(false));
  await user.click(screen.getByRole("button", { name: "Finish review and clear selection" })); expect(change).toHaveBeenCalledOnce(); expect(screen.getByText("Selected: 0 / 500")).toBeTruthy();
});
