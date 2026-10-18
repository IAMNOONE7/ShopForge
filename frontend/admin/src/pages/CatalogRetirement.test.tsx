// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminStore, type Category, type StoreProduct } from "../api";
import type { CatalogKind } from "../api/catalogLifecycle";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { StoreLayout } from "../layouts/StoreLayout";
import { SessionContext } from "../session";
import { CategoriesPage, CategoryDetailPage } from "./CategoryPages";
import { ListingsPage, ListingDetailPage } from "./ListingPages";

const store: AdminStore = { id: "store-a", name: "Store A", currency: "CZK", culture: "cs-CZ", status: "draft", theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 4 }, logoUrl: null, primaryHostName: null, returnWindowDays: 14, company: null };
const category: Category = { id: "record-a", name: "Chairs", slug: "chairs", parentId: null, sortOrder: 0, attributeIds: [], seoTitle: null, seoDescription: null, pageText: null };
const listing: StoreProduct = { id: "record-a", productId: "physical", sku: "CHAIR", name: "Chair", slug: "chair", description: null, price: 100, vatRate: 21, isVisible: true, sortOrder: 0, categoryIds: [], archivedAt: null, taxClass: null, seoTitle: null, seoDescription: null, seoSocialImageUrl: null, seoNoIndex: false };
const child = { ...category, id: "child", name: "Dining chairs", parentId: category.id };
let retired: boolean; let removed: boolean;
function reads() {
  retired = false; removed = false;
  vi.spyOn(api, "findStoreProduct").mockImplementation(async () => removed ? null : { ...listing, archivedAt: retired ? "2026-10-12T10:00:00Z" : null });
  vi.spyOn(api, "storeProducts").mockImplementation(async (_store, _signal, query = {}) => ({ items: !removed && !!query.archived === retired ? [{ ...listing, archivedAt: retired ? "2026-10-12T10:00:00Z" : null }] : [], totalCount: !removed && !!query.archived === retired ? 1 : 0, page: query.page ?? 1, pageSize: 25, hasMore: false }));
  vi.spyOn(api, "categories").mockImplementation(async (_store, _signal, archived = false) => !removed && archived === retired ? [category] : []);
  vi.spyOn(api, "attributes").mockResolvedValue([]); vi.spyOn(api, "productAttributes").mockResolvedValue({ values: {} }); vi.spyOn(api, "products").mockResolvedValue([]);
  return vi.spyOn(api, "changeCatalogLifecycle").mockImplementation(async (_store, _kind, _id, action) => { if (action === "delete") removed = true; else retired = action === "archive"; });
}
function route(kind: CatalogKind, suffix = "/record-a", role = "Owner") {
  return render(<SessionContext.Provider value={{ user: { id: "u", email: "owner@example.test", role, tenantId: "tenant" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[`/stores/store-a/${kind}${suffix}`]}><Link to={`/stores/store-b/${kind}/record-a`}>Switch store</Link>
      <Routes><Route element={<Outlet context={{ stores: { status: "ready", data: [store, { ...store, id: "store-b" }], refreshing: false, refreshError: null }, reloadStores: () => undefined }} />}>
        <Route path="/stores/:storeId" element={<StoreLayout />}><Route path="products" element={<ListingsPage />} /><Route path="products/:listingId" element={<ListingDetailPage />} /><Route path="categories" element={<CategoriesPage />} /><Route path="categories/:categoryId" element={<CategoryDetailPage />} /></Route>
      </Route></Routes>
    </MemoryRouter></SessionContext.Provider>);
}
function confirm() { return screen.getByRole("form"); }
async function openDelete(user: ReturnType<typeof userEvent.setup>) { await user.click(await screen.findByRole("button", { name: "Delete permanently" })); return within(confirm()); }
beforeAll(async () => { await initializeI18n(); });
beforeEach(async () => { await i18n.changeLanguage("en"); vi.stubGlobal("CSS", { escape: (s: string) => s }); HTMLElement.prototype.scrollIntoView = vi.fn(); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe.each(["products", "categories"] as CatalogKind[])("%s retirement", (kind) => {
  const name = kind === "products" ? listing.name : category.name; const editName = kind === "products" ? "Edit listing" : "Edit category";
  it("names the target, focuses confirmation, cancels with Escape and locks the editor", async () => {
    const user = userEvent.setup(); const change = reads(); route(kind); const opener = await screen.findByRole("button", { name: "Archive" }); await user.click(opener);
    expect(document.activeElement?.textContent).toBe(`Archive “${name}”?`); expect((screen.getByRole("button", { name: editName }) as HTMLButtonElement).disabled).toBe(true);
    await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(opener)); expect(change).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: editName })); expect((screen.getByRole("button", { name: "Archive" }) as HTMLButtonElement).disabled).toBe(true);
  });
  it("serializes archive and loads the authoritative archived collection after success", async () => {
    const user = userEvent.setup(); const change = reads(); let release!: () => void;
    change.mockImplementation(() => new Promise<void>(resolve => { release = () => { retired = true; resolve(); }; })); route(kind);
    await user.click(await screen.findByRole("button", { name: "Archive" })); fireEvent.submit(confirm()); fireEvent.submit(confirm()); expect(change).toHaveBeenCalledTimes(1);
    expect((screen.getByRole("button", { name: "Cancel" }) as HTMLButtonElement).disabled).toBe(true); await act(async () => release());
    await screen.findByText(`“${name}” is archived.`); expect(screen.getByRole("navigation", { name: "Catalog collection" }).querySelector('[aria-current="page"]')?.textContent).toBe("Archived");
    expect(await screen.findByRole("link", { name })).toBeTruthy(); expect(change).toHaveBeenCalledWith("store-a", kind, "record-a", "archive");
  });
  it("resolves archived deep links, keeps them read-only and restores into active records", async () => {
    const user = userEvent.setup(); const change = reads(); retired = true; route(kind, "/record-a?archived=true"); await user.click(await screen.findByRole("button", { name: "Restore" }));
    expect(screen.queryByRole("button", { name: editName })).toBeNull();
    if (kind === "products") { expect(api.productAttributes).not.toHaveBeenCalled(); expect(screen.getByText(/Attribute values cannot be read/)).toBeTruthy(); }
    expect(screen.getByRole("link", { name: kind === "products" ? "Back to listings" : "Back to categories" }).getAttribute("href")).toContain("?archived=true");
    await user.click(within(confirm()).getByRole("button", { name: "Restore" })); await screen.findByText(`“${name}” is restored.`);
    expect(screen.getByRole("navigation", { name: "Catalog collection" }).querySelector('[aria-current="page"]')?.textContent).toBe("Active"); expect(change).toHaveBeenCalledWith("store-a", kind, "record-a", "restore");
  });
  it.each([false, true])("requires irreversible confirmation and returns to the source collection (archived=%s)", async (archived) => {
    const user = userEvent.setup(); const change = reads(); retired = archived; route(kind, archived ? "/record-a?archived=true" : "/record-a"); const form = await openDelete(user);
    expect((form.getByRole("button", { name: "Delete permanently" }) as HTMLButtonElement).disabled).toBe(true); fireEvent.submit(confirm()); expect(change).not.toHaveBeenCalled();
    await user.click(form.getByRole("checkbox", { name: `I want to permanently delete “${name}”.` })); await user.click(form.getByRole("button", { name: "Delete permanently" })); await screen.findByText(`“${name}” was permanently deleted.`);
    expect(screen.getByRole("navigation", { name: "Catalog collection" }).querySelector('[aria-current="page"]')?.textContent).toBe(archived ? "Archived" : "Active"); expect(screen.queryByRole("link", { name })).toBeNull();
  });
  it("retains refused delete, clears verification and offers a separate named archive confirmation", async () => {
    const user = userEvent.setup(); const change = reads(); const reason = kind === "products" ? "it has been ordered" : "another category sits beneath it";
    change.mockRejectedValueOnce(new HttpError("http", 409, { detail: `It cannot be deleted because ${reason}. Archive it instead to take it out of use and keep it.` })); route(kind);
    const form = await openDelete(user); await user.click(form.getByRole("checkbox")); await user.click(form.getByRole("button", { name: "Delete permanently" }));
    await screen.findByText(kind === "products" ? /This listing has been ordered/ : /Another active category sits beneath/); expect((form.getByRole("checkbox") as HTMLInputElement).checked).toBe(false);
    await waitFor(() => expect((screen.getByRole("button", { name: "Archive" }) as HTMLButtonElement).disabled).toBe(false)); await user.click(screen.getByRole("button", { name: "Archive" }));
    expect(document.activeElement?.textContent).toBe(`Archive “${name}”?`); expect(change).toHaveBeenCalledTimes(1);
    await user.click(within(confirm()).getByRole("button", { name: "Archive" })); await screen.findByText(`“${name}” is archived.`);
  });
  it("locks retained failure until a successful read and never replays on read retry", async () => {
    const user = userEvent.setup(); const change = reads(); change.mockRejectedValueOnce(new HttpError("http", 503)); route(kind); const form = await openDelete(user);
    if (kind === "products") vi.mocked(api.findStoreProduct).mockRejectedValueOnce(new HttpError("http", 503)); else vi.mocked(api.categories).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(form.getByRole("checkbox")); await user.click(form.getByRole("button", { name: "Delete permanently" })); const retry = await screen.findByRole("button", { name: "Try again" });
    expect((form.getByRole("checkbox") as HTMLInputElement).disabled).toBe(true); await user.click(retry); await waitFor(() => expect((form.getByRole("checkbox") as HTMLInputElement).disabled).toBe(false));
    expect((form.getByRole("checkbox") as HTMLInputElement).checked).toBe(false); expect(change).toHaveBeenCalledTimes(1); expect(screen.getByRole("heading", { name: `Permanently delete “${name}”?` })).toBeTruthy();
  });
  it("refreshes a lost archive reply into archived state and prevents reuse of old confirmation", async () => {
    const user = userEvent.setup(); const change = reads(); change.mockImplementationOnce(async () => { retired = true; throw new HttpError("network", null); }); route(kind);
    await user.click(await screen.findByRole("button", { name: "Archive" })); await user.click(within(confirm()).getByRole("button", { name: "Archive" }));
    await screen.findByText(/This record changed while/); expect((within(confirm()).getByRole("button", { name: "Archive" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "Cancel" })); expect(screen.getByRole("button", { name: "Restore" })).toBeTruthy(); expect(change).toHaveBeenCalledTimes(1);
  });
  it("ignores late success after store departure", async () => {
    const user = userEvent.setup(); const change = reads(); let release!: () => void; change.mockImplementation(() => new Promise<void>(resolve => { release = resolve; })); route(kind);
    await user.click(await screen.findByRole("button", { name: "Archive" })); await user.click(within(confirm()).getByRole("button", { name: "Archive" })); await user.click(screen.getByRole("link", { name: "Switch store" })); await screen.findByRole("button", { name: "Archive" });
    const count = kind === "products" ? vi.mocked(api.findStoreProduct).mock.calls.length : vi.mocked(api.categories).mock.calls.length;
    await act(async () => release()); expect(screen.queryByText(`“${name}” is archived.`)).toBeNull(); expect(screen.getByRole("heading", { name, level: 1 })).toBeTruthy();
    expect(kind === "products" ? vi.mocked(api.findStoreProduct).mock.calls.length : vi.mocked(api.categories).mock.calls.length).toBe(count);
  });
  it.each(["Support", "Accountant", "OrderManager", "StockManager"])("keeps %s read-only", async (role) => {
    const change = reads(); route(kind, "/record-a", role); await screen.findByRole("heading", { name, level: 1 });
    expect(screen.queryByRole("button", { name: "Archive" })).toBeNull(); expect(screen.queryByRole("button", { name: "Delete permanently" })).toBeNull(); expect(change).not.toHaveBeenCalled();
  });
  it.each([404, 409])("retains restore refusal %s without moving to active records", async (status) => {
    const user = userEvent.setup(); const change = reads(); retired = true; change.mockRejectedValueOnce(new HttpError("http", status)); route(kind);
    await user.click(await screen.findByRole("button", { name: "Restore" })); await user.click(within(confirm()).getByRole("button", { name: "Restore" }));
    await screen.findByRole("alert"); expect(screen.getByRole("form")).toBeTruthy(); expect(screen.queryByText(`“${name}” is restored.`)).toBeNull(); expect(change).toHaveBeenCalledTimes(1);
  });
});
it("resets listing paging while retaining archived search/sort and canonical links", async () => {
  const user = userEvent.setup(); reads(); retired = true; route("products", "?archived=true&page=3&q=CHAIR&sort=-price");
  expect((await screen.findByRole("link", { name: "Chair" })).getAttribute("href")).toBe("/stores/store-a/products/record-a?archived=true&page=3&q=CHAIR&sort=-price");
  expect(api.storeProducts).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 3, pageSize: 25, q: "CHAIR", sort: "-price", archived: true });
  await user.click(screen.getByRole("link", { name: "Active" }));
  await waitFor(() => expect(api.storeProducts).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, pageSize: 25, q: "CHAIR", sort: "-price" }));
});
it("shows unavailable deeper ancestry without claiming a complete level or cascading archive", async () => {
  const user = userEvent.setup(); reads(); vi.mocked(api.categories).mockImplementation(async (_store, _signal, archived = false) => archived ? [] : [child, { ...category, parentId: "missing" }]);
  route("categories", ""); await screen.findByRole("link", { name: child.name }); const row = screen.getByRole("link", { name: child.name }).closest("li")!;
  expect(within(row).getByText("Parent unavailable (missing)")).toBeTruthy(); expect(within(row).getByText("Unavailable")).toBeTruthy();
  await user.click(screen.getByRole("link", { name: child.name })); await user.click(await screen.findByRole("button", { name: "Archive" })); expect(screen.getByText(/Only this category is archived/)).toBeTruthy();
});

describe.each(["products", "categories"] as CatalogKind[])("%s contract edge cases", kind => {
  it.each(["Admin", "CatalogManager"])("allows %s to manage retirement", async role => {
    reads(); route(kind, "/record-a", role); expect(await screen.findByRole("button", { name: "Archive" })).toBeTruthy();
  });
  it("reports a failed lifecycle read as an error rather than a missing record", async () => {
    reads();
    if (kind === "products") vi.mocked(api.findStoreProduct).mockRejectedValueOnce(new HttpError("http", 503));
    else vi.mocked(api.categories).mockResolvedValueOnce([]).mockRejectedValueOnce(new HttpError("http", 503));
    route(kind); await screen.findByRole("alert"); expect(screen.queryByText(kind === "products" ? "Listing not found" : "Category not found")).toBeNull(); expect(screen.queryByRole("button", { name: "Archive" })).toBeNull();
  });
  it("localizes Czech confirmations while preserving the merchant name", async () => {
    const user = userEvent.setup(); await i18n.changeLanguage("cs"); reads(); route(kind);
    await user.click(await screen.findByRole("button", { name: "Trvale smazat" }));
    expect(screen.getByRole("heading", { name: `Trvale smazat „${kind === "products" ? listing.name : category.name}“?` })).toBeTruthy();
    expect(screen.getByRole("checkbox")).toBeTruthy();
  });
});
