// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api, type AdminStore, type ShippingMethod } from "../api";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { StoreMethodsPage } from "./StorePage";

const method: ShippingMethod = { code: "parcel", name: "Parcel", providerKey: "manual", price: 0, vatRate: 0, isActive: false, requiresPickupPoint: false, maxWeightGrams: null, countries: [], taxClass: "standard" };
const store: AdminStore = { id: "a", name: "A", currency: "CZK", culture: "cs-CZ", company: null, status: "draft", primaryHostName: null, logoUrl: null, returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 4 } };
type Options = { role?: string; tenant?: string; storeId?: string; stale?: boolean };
function Shell({ role = "Owner", tenant = "tenant-a", storeId = "a", stale = false }: Options) {
  return <SessionContext value={{ user: { id: "user", email: "user@example.test", tenantId: tenant, role }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <Outlet context={{ store: { ...store, id: storeId }, storeRefreshing: stale, storeRefreshError: null, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function app(options: Options = {}) { return <MemoryRouter initialEntries={["/stores/a/methods"]}><Routes><Route element={<Shell {...options} />}><Route path="/stores/:id/methods" element={<StoreMethodsPage />} /></Route></Routes></MemoryRouter>; }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(accept => { resolve = accept; }); return { promise, resolve }; }
const parcel = () => screen.getByRole("form", { name: "Parcel" });
beforeAll(async () => { HTMLElement.prototype.scrollIntoView = vi.fn(); await initializeI18n(); });
beforeEach(async () => {
  await i18n.changeLanguage("en"); vi.spyOn(api, "shippingMethods").mockResolvedValue([method]); vi.spyOn(api, "shippingProviders").mockResolvedValue(["manual"]);
  vi.spyOn(api, "paymentMethods").mockResolvedValue([]); vi.spyOn(api, "paymentProviders").mockResolvedValue(["manual"]); vi.spyOn(api, "pickupPoints").mockResolvedValue([]);
  vi.spyOn(api, "taxClasses").mockResolvedValue([{ code: "standard", name: "Standard", rates: [] }, { code: "books", name: "Books", rates: [] }]); vi.spyOn(api, "updateShippingMethod").mockResolvedValue(method);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it.each(["CatalogManager", "Warehouse", "Support"])("prevents direct shipping writes and tax lookups for %s", async role => {
  render(app({ role })); const form = await screen.findByRole("form", { name: "Parcel" }); fireEvent.submit(form);
  expect(within(form).getByRole("button", { name: "Save" }).matches(":disabled")).toBe(true); expect(api.updateShippingMethod).not.toHaveBeenCalled(); expect(api.taxClasses).not.toHaveBeenCalled();
});
it.each(["Owner", "Admin"])("allows shipping assignments for %s without exposing definition writes", async role => {
  const user = userEvent.setup(); render(app({ role })); const form = await screen.findByRole("form", { name: "Parcel" }); await screen.findAllByRole("option", { name: "Books (books)" });
  await user.selectOptions(within(form).getByRole("combobox", { name: "Tax class" }), "books"); await user.click(within(form).getByRole("button", { name: "Save" }));
  expect(api.updateShippingMethod).toHaveBeenCalledWith("a", "parcel", expect.objectContaining({ taxClass: "books" }));
});
it.each([{ tenant: "tenant-b" }, { storeId: "b" }, { role: "Admin" }])("discards unsaved shipping assignment and other drafts on scope change %j", async options => {
  const user = userEvent.setup(), view = render(app()); await screen.findAllByRole("option", { name: "Books (books)" });
  await user.selectOptions(within(parcel()).getByRole("combobox", { name: "Tax class" }), "books"); await user.type(within(parcel()).getByRole("textbox", { name: "Countries" }), "CZ");
  view.rerender(app(options)); await screen.findAllByRole("option", { name: "Books (books)" });
  expect(within(parcel()).getByRole("combobox", { name: "Tax class" })).toHaveProperty("value", "standard"); expect(within(parcel()).getByRole("textbox", { name: "Countries" })).toHaveProperty("value", ""); expect(api.updateShippingMethod).not.toHaveBeenCalled();
});
it("ignores a departed store's late shipping read", async () => {
  const old = deferred<ShippingMethod[]>(); vi.mocked(api.shippingMethods).mockReturnValueOnce(old.promise);
  const view = render(app()); view.rerender(app({ storeId: "b" })); await screen.findByRole("form", { name: "Parcel" });
  await act(async () => old.resolve([{ ...method, name: "Old store parcel", taxClass: "books" }])); expect(screen.queryByRole("form", { name: "Old store parcel" })).toBeNull();
});
it("does not reload or announce a departed store's late assignment write", async () => {
  const held = deferred<ShippingMethod>(); vi.mocked(api.updateShippingMethod).mockReturnValueOnce(held.promise); const view = render(app()); await screen.findByRole("form", { name: "Parcel" });
  fireEvent.submit(parcel()); expect(api.updateShippingMethod).toHaveBeenCalledOnce(); view.rerender(app({ storeId: "b" })); await screen.findByRole("form", { name: "Parcel" });
  const reads = vi.mocked(api.shippingMethods).mock.calls.length; await act(async () => held.resolve({ ...method, taxClass: "books" }));
  expect(api.shippingMethods).toHaveBeenCalledTimes(reads); expect(screen.queryByText(/The save was acknowledged/)).toBeNull();
});
it("blocks existing and new shipping saves while store details are stale", async () => {
  render(app({ stale: true })); await screen.findByRole("form", { name: "Parcel" }); fireEvent.submit(parcel());
  expect(api.updateShippingMethod).not.toHaveBeenCalled(); await waitFor(() => expect(within(parcel()).getByRole("button", { name: "Save" }).matches(":disabled")).toBe(true));
});
