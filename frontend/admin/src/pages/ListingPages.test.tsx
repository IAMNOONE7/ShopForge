// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminStore, type AttributeDefinition, type AttributeType, type Category, type Product, type StoreProduct } from "../api";
import { HttpError } from "../api/http";
import { attributeLabel, validDate, validDecimal } from "../components/listingAttributes";
import { listingDraft, validateListing } from "../components/listingValidation";
import { i18n, initializeI18n } from "../i18n";
import { StoreLayout } from "../layouts/StoreLayout";
import { SessionContext } from "../session";
import { ListingDetailPage, ListingsPage, NewListingPage } from "./ListingPages";

const store: AdminStore = { id: "store-a", name: "Store A", currency: "CZK", culture: "cs-CZ", status: "draft",
  theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 4 }, logoUrl: null, primaryHostName: null, returnWindowDays: 14, company: null };
const item: StoreProduct = { id: "listing-a", productId: "physical-a", sku: "CHAIR", name: "Chair", slug: "chair", description: "Plain description",
  price: 100, vatRate: 21, isVisible: true, sortOrder: 0, categoryIds: ["child"], seoTitle: "Saved SEO title", seoDescription: "Saved SEO text", seoSocialImageUrl: "https://example.test/social.png", seoNoIndex: true };
const parent: Category = { id: "root", name: "Furniture", slug: "furniture", parentId: null, sortOrder: 0, attributeIds: [], seoTitle: null, seoDescription: null, pageText: null };
const child: Category = { ...parent, id: "child", name: "Chairs", slug: "chairs", parentId: "root" };
const product: Product = { id: item.productId, sku: "CHAIR", ean: null, weightGrams: null, brand: null, optionNames: ["Colour"], images: [],
  variants: [{ id: "variant-red", sku: "CHAIR-RED", ean: null, weightGrams: null, partNumber: null, condition: null, optionValues: ["Red"], position: 0 }] };
function definition(code: string, type: AttributeType): AttributeDefinition {
  return { id: code, code, name: code, type, unit: null, sortOrder: 0, isFilterable: true, isVisibleOnProductPage: true,
    options: ["red", "blue"].map((code, sortOrder) => ({ id: code, code, name: code.toUpperCase(), sortOrder })) };
}
const definitions = [definition("text", "text"), definition("integer", "integer"), definition("decimal", "decimal"), definition("boolean", "boolean"), definition("date", "date"), definition("select", "select"), definition("multi", "multiSelect")];
const page = { items: [item], page: 1, pageSize: 25, totalCount: 26, hasMore: true };
function route(path = "/stores/store-a/products/listing-a", role = "Owner") {
  return render(<SessionContext.Provider value={{ user: { id: "user-a", email: "owner@example.test", role, tenantId: "tenant-a" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[path]}><Link to="/stores/store-b/products/listing-a">Switch store</Link><Link to="/stores/store-a/products/listing-b">Switch listing</Link>
      <Routes><Route element={<Outlet context={{ stores: { status: "ready", data: [store, { ...store, id: "store-b", name: "Store B" }], refreshing: false, refreshError: null }, reloadStores: () => undefined }} />}>
        <Route path="/stores/:storeId" element={<StoreLayout />}><Route path="products" element={<ListingsPage />} /><Route path="products/new" element={<NewListingPage />} /><Route path="products/:listingId" element={<ListingDetailPage />} /></Route>
      </Route></Routes>
    </MemoryRouter>
  </SessionContext.Provider>);
}
function editor() { return document.querySelector<HTMLFormElement>(".listing-editor form")!; }
function reads() {
  vi.spyOn(api, "findStoreProduct").mockResolvedValue(item);
  vi.spyOn(api, "categories").mockResolvedValue([parent, child]);
  vi.spyOn(api, "attributes").mockResolvedValue(definitions);
  vi.spyOn(api, "productAttributes").mockResolvedValue({ values: {} });
  vi.spyOn(api, "products").mockResolvedValue([product]);
}
beforeAll(async () => { await initializeI18n(); await i18n.changeLanguage("en"); });
beforeEach(() => { vi.stubGlobal("CSS", { escape: (name: string) => name }); HTMLElement.prototype.scrollIntoView = vi.fn(); });
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });

describe("focused store listings", () => {
  it("uses server paging, sort and search, resets page and preserves the list query on detail links", async () => {
    const user = userEvent.setup(); reads();
    const list = vi.spyOn(api, "storeProducts").mockResolvedValue(page);
    const orders = vi.spyOn(api, "orders");
    route("/stores/store-a/products?page=2&sort=-price&q=CHAIR");
    const link = await screen.findByRole("link", { name: "Chair" });
    expect(link.getAttribute("href")).toBe("/stores/store-a/products/listing-a?page=2&q=CHAIR&sort=-price");
    expect(list).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 2, pageSize: 25, sort: "-price", q: "CHAIR" });
    expect(api.products).not.toHaveBeenCalled(); expect(api.categories).not.toHaveBeenCalled(); expect(orders).not.toHaveBeenCalled();
    await user.selectOptions(screen.getByRole("combobox", { name: "Sort listings" }), "name");
    await waitFor(() => expect(list).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, pageSize: 25, sort: "name", q: "CHAIR" }));
    await user.clear(screen.getByRole("textbox", { name: "Search listings" })); await user.type(screen.getByRole("textbox", { name: "Search listings" }), "x");
    const calls = list.mock.calls.length; await user.click(screen.getByRole("button", { name: "Search" })); expect(list).toHaveBeenCalledTimes(calls);
    await user.type(screen.getByRole("textbox", { name: "Search listings" }), "y"); await user.click(screen.getByRole("button", { name: "Search" }));
    await waitFor(() => expect(list).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, pageSize: 25, sort: "name", q: "xy" }));
    await user.click(screen.getByRole("button", { name: "Next page" }));
    await waitFor(() => expect(list).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 2, pageSize: 25, sort: "name", q: "xy" }));
    await user.click(screen.getByRole("link", { name: "Chair" }));
    expect(await screen.findByRole("heading", { name: "Chair" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "Back to listings" }).getAttribute("href")).toBe("/stores/store-a/products?page=2&q=xy&sort=name");
    expect(screen.getByRole("link", { name: "Stock for CHAIR-RED" }).getAttribute("href")).toBe("/stock/variant-red");
  });

  it("shows read failures separately from empty and out-of-range pages", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "storeProducts").mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue({ ...page, items: [], totalCount: 0, hasMore: false });
    route("/stores/store-a/products?page=9"); await screen.findByRole("alert"); expect(screen.queryByText("No active listings yet")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" })); expect(await screen.findByText("No listings on this page")).toBeTruthy();
  });

  it("retains a duplicate-product draft and creates using the returned canonical ID", async () => {
    const user = userEvent.setup(); reads();
    const created = { ...item, id: "new-id", name: "Žluté židle", slug: "zlute-zidle", price: 12.5, isVisible: false };
    vi.mocked(api.findStoreProduct).mockResolvedValue(created);
    const create = vi.spyOn(api, "listProduct").mockRejectedValueOnce(new HttpError("http", 409)).mockResolvedValue(created);
    route("/stores/store-a/products/new");
    await user.selectOptions(await screen.findByRole("combobox", { name: "Product to list…" }), product.id);
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Žluté židle"); await user.type(screen.getByRole("textbox", { name: "Price" }), "12,50");
    expect(screen.getByText("Derived address: zlute-zidle")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "List product" })); await screen.findByRole("alert");
    expect((screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement).value).toBe("Žluté židle");
    expect((screen.getByRole("textbox", { name: "Price" }) as HTMLInputElement).value).toBe("12,50");
    await user.click(screen.getByRole("button", { name: "List product" })); expect(await screen.findByRole("heading", { name: "Žluté židle" })).toBeTruthy();
    expect(create).toHaveBeenLastCalledWith("store-a", product.id, { name: "Žluté židle", slug: null, description: null, price: 12.5, vatRate: 21, isVisible: false, sortOrder: 0 });
    expect(api.findStoreProduct).toHaveBeenLastCalledWith("store-a", "new-id", expect.any(AbortSignal));
  });

  it("saves details independently, omits SEO and retains values/categories after a failed write", async () => {
    const user = userEvent.setup(); reads();
    const update = vi.spyOn(api, "updateStoreProduct").mockRejectedValueOnce(new HttpError("http", 400, { errors: { price: ["Invalid price"] } })).mockResolvedValue(item);
    const categories = vi.spyOn(api, "assignCategories"); const values = vi.spyOn(api, "setProductAttributes");
    route(); await user.click(await screen.findByRole("button", { name: "Edit listing" }));
    const fields = within(editor()); await user.clear(fields.getByRole("textbox", { name: "Price" })); await user.type(fields.getByRole("textbox", { name: "Price" }), "0");
    await user.click(fields.getByRole("checkbox", { name: "Visible in the storefront" })); await user.click(fields.getByRole("button", { name: "Save listing" }));
    const alert = await screen.findByRole("alert"); await user.click(within(alert).getByRole("button")); expect(document.activeElement).toBe(fields.getByRole("textbox", { name: "Price" }));
    expect((fields.getByRole("textbox", { name: "Price" }) as HTMLInputElement).value).toBe("0");
    await user.click(fields.getByRole("button", { name: "Save listing" })); await waitFor(() => expect(editor()).toBeNull());
    expect(update).toHaveBeenLastCalledWith("store-a", item.id, { name: "Chair", slug: "chair", description: "Plain description", price: 0, vatRate: 21, isVisible: false, sortOrder: 0 });
    expect(categories).not.toHaveBeenCalled(); expect(values).not.toHaveBeenCalled();
  });

  it("does not return to a departed store when a pending create completes", async () => {
    const user = userEvent.setup(); reads(); let resolve!: (value: StoreProduct) => void;
    vi.spyOn(api, "listProduct").mockReturnValue(new Promise((done) => { resolve = done; }));
    route("/stores/store-a/products/new");
    await user.selectOptions(await screen.findByRole("combobox", { name: "Product to list…" }), product.id);
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Pending chair"); await user.type(screen.getByRole("textbox", { name: "Price" }), "10");
    await user.click(screen.getByRole("button", { name: "List product" }));
    await user.click(screen.getByRole("link", { name: "Switch store" })); await screen.findByRole("heading", { name: "Chair" });
    const calls = vi.mocked(api.findStoreProduct).mock.calls.length;
    await act(async () => resolve({ ...item, id: "created-later" }));
    expect(api.findStoreProduct).toHaveBeenCalledTimes(calls);
    expect(api.findStoreProduct).toHaveBeenLastCalledWith("store-b", item.id, expect.any(AbortSignal));
  });

  it("retains unavailable category assignments until explicit removal and can clear all categories", async () => {
    const user = userEvent.setup(); reads(); vi.mocked(api.findStoreProduct).mockResolvedValue({ ...item, categoryIds: ["gone", "child"] });
    const assign = vi.spyOn(api, "assignCategories").mockRejectedValueOnce(new HttpError("http", 409)).mockResolvedValue(item);
    route(); await user.click(await screen.findByRole("button", { name: "Edit categories" }));
    await user.click(screen.getByRole("button", { name: "Save categories" })); expect(assign).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Remove unavailable category gone" }));
    await user.click(screen.getByRole("button", { name: "Save categories" })); await screen.findByRole("alert");
    expect((within(editor()).getByRole("checkbox", { name: "Furniture › Chairs" }) as HTMLInputElement).checked).toBe(true);
    await user.click(within(editor()).getByRole("checkbox", { name: "Furniture › Chairs" }));
    await user.click(screen.getByRole("button", { name: "Save categories" })); expect(assign).toHaveBeenLastCalledWith("store-a", item.id, []);
  });

  it("writes every attribute type using option codes and preserves false, zero and failed drafts", async () => {
    const user = userEvent.setup(); reads();
    const save = vi.spyOn(api, "setProductAttributes").mockRejectedValueOnce(new HttpError("http", 400, { errors: { "values.integer": ["Invalid"] } })).mockResolvedValue({ values: {} });
    route(); await user.click(await screen.findByRole("button", { name: "Edit attribute values" }));
    const fields = within(editor());
    await user.type(fields.getByRole("textbox", { name: "text" }), " Plain text "); await user.type(fields.getByRole("textbox", { name: "integer" }), "0");
    await user.type(fields.getByRole("textbox", { name: "decimal" }), "-12,5"); await user.selectOptions(fields.getByRole("combobox", { name: "boolean" }), "false");
    await user.type(fields.getByLabelText("date"), "2024-02-29"); await user.selectOptions(fields.getByRole("combobox", { name: "select" }), "blue");
    await user.click(fields.getByRole("checkbox", { name: "RED" })); await user.click(fields.getByRole("button", { name: "Save attribute values" }));
    const alert = await screen.findByRole("alert"); await user.click(within(alert).getByRole("button")); expect(document.activeElement).toBe(fields.getByRole("textbox", { name: "integer" }));
    expect(save).toHaveBeenLastCalledWith("store-a", item.id, { text: "Plain text", integer: 0, decimal: -12.5, boolean: false, date: "2024-02-29", select: "blue", multi: ["red"] });
    expect((fields.getByRole("combobox", { name: "boolean" }) as HTMLSelectElement).value).toBe("false");
    await user.clear(fields.getByRole("textbox", { name: "text" })); await user.click(fields.getByRole("checkbox", { name: "RED" }));
    await user.click(fields.getByRole("button", { name: "Save attribute values" }));
    expect(save).toHaveBeenLastCalledWith("store-a", item.id, { integer: 0, decimal: -12.5, boolean: false, date: "2024-02-29", select: "blue" });
  });

  it("requires explicit removal of unknown values, retired choices and unsafe integers", async () => {
    const user = userEvent.setup(); reads(); vi.mocked(api.productAttributes).mockResolvedValue({ values: { integer: 9007199254740992, select: "retired", multi: ["retired"], unknown: "saved" } });
    const save = vi.spyOn(api, "setProductAttributes").mockResolvedValue({ values: {} });
    route(); await user.click(await screen.findByRole("button", { name: "Edit attribute values" }));
    await user.click(screen.getByRole("button", { name: "Save attribute values" })); expect(save).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Clear integer" })); await user.click(screen.getByRole("button", { name: "Clear unknown" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "select" }), ""); await user.click(screen.getByRole("checkbox", { name: "Unavailable option (retired)" }));
    await user.click(screen.getByRole("button", { name: "Save attribute values" })); expect(save).toHaveBeenLastCalledWith("store-a", item.id, {});
  });

  it("keeps detail editing available after an independent values read failure", async () => {
    const user = userEvent.setup(); reads(); vi.mocked(api.productAttributes).mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue({ values: {} });
    route(); await screen.findByRole("alert"); expect((screen.getByRole("button", { name: "Edit attribute values" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "Edit listing" })); expect(screen.getByRole("textbox", { name: "Name" })).toBeTruthy();
    await user.click(within(editor()).getByRole("button", { name: "Cancel" })); await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect((screen.getByRole("button", { name: "Edit attribute values" }) as HTMLButtonElement).disabled).toBe(false));
  });

  it("locks duplicate saves and only retries the failed read after a confirmed write", async () => {
    const user = userEvent.setup(); reads(); let resolve!: (value: StoreProduct) => void;
    const save = vi.spyOn(api, "updateStoreProduct").mockReturnValue(new Promise((done) => { resolve = done; }));
    vi.mocked(api.findStoreProduct).mockResolvedValueOnce(item).mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue(item);
    route(); await user.click(await screen.findByRole("button", { name: "Edit listing" })); await user.click(screen.getByRole("button", { name: "Save listing" }));
    await user.click(screen.getByRole("button", { name: "Saving listing…" })); expect(save).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("textbox", { name: "Name" }).matches(":disabled")).toBe(true);
    await act(async () => resolve(item)); expect(await screen.findByText(/confirmed save does not need to be repeated/)).toBeTruthy(); expect(editor()).toBeNull();
    expect((screen.getByRole("button", { name: "Edit listing" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect((screen.getByRole("button", { name: "Edit listing" }) as HTMLButtonElement).disabled).toBe(false));
    expect(save).toHaveBeenCalledTimes(1);
  });

  it("retains a stale draft until cancel and discards it on store/listing switches", async () => {
    const user = userEvent.setup(); reads();
    vi.mocked(api.findStoreProduct).mockResolvedValueOnce(item).mockImplementation(async (storeId, listingId) => ({ ...item, id: listingId, name: storeId === "store-b" ? "Store B chair" : listingId === "listing-b" ? "Other chair" : "Server chair" }));
    route(); await user.click(await screen.findByRole("button", { name: "Edit listing" })); await user.type(screen.getByRole("textbox", { name: "Name" }), " draft");
    await user.click(screen.getByRole("button", { name: "Refresh listing data" })); await screen.findByText(/draft is retained for reference/);
    expect((screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement).value).toBe("Chair draft"); expect((screen.getByRole("button", { name: "Save listing" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(within(editor()).getByRole("button", { name: "Cancel" })); await user.click(screen.getByRole("button", { name: "Edit listing" }));
    expect((screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement).value).toBe("Server chair");
    await user.click(screen.getByRole("link", { name: "Switch store" })); await screen.findByRole("heading", { name: "Store B chair" }); expect(editor()).toBeNull();
    await user.click(screen.getByRole("button", { name: "Edit listing" })); await user.click(screen.getByRole("link", { name: "Switch listing" })); await screen.findByRole("heading", { name: "Other chair" }); expect(editor()).toBeNull();
  });

  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("uses catalog write permissions for %s", async (role) => {
    reads(); route(undefined, role); await screen.findByRole("heading", { name: "Chair" });
    expect(screen.queryByRole("button", { name: "Edit listing" }) !== null).toBe(["Owner", "Admin", "CatalogManager"].includes(role));
    expect(screen.getByRole("link", { name: "Stock for CHAIR-RED" })).toBeTruthy();
  });

  it("does not read forms for missing stores or unauthorized creation and labels missing active listings", async () => {
    reads(); route("/stores/missing/products/new"); await screen.findByText("Store not found"); expect(api.products).not.toHaveBeenCalled(); cleanup();
    route("/stores/store-a/products/new", "Support"); expect(screen.queryByRole("button", { name: "List product" })).toBeNull(); expect(api.products).not.toHaveBeenCalled(); cleanup();
    vi.mocked(api.findStoreProduct).mockResolvedValue(null); route(); expect(await screen.findByText("Listing not found")).toBeTruthy(); expect(screen.queryByRole("button", { name: "Edit listing" })).toBeNull();
  });

  it("localizes validation in Czech while retaining merchant content", async () => {
    const user = userEvent.setup(); reads(); await i18n.changeLanguage("cs"); route();
    await user.click(await screen.findByRole("button", { name: "Upravit nabídku" }));
    const name = within(editor()).getByRole("textbox", { name: "Název" }); await user.clear(name);
    await user.click(screen.getByRole("button", { name: "Uložit nabídku" })); expect(await screen.findByRole("alert")).toBeTruthy(); expect(document.activeElement).toBe(name);
  });

  it("validates price precision, VAT, Int32 order and real calendar dates without accepting exponent input", () => {
    expect(validateListing({ ...listingDraft(item), price: "0,00", vatRate: "100", sortOrder: "-2147483648" }, true)).toEqual([]);
    expect(validateListing({ ...listingDraft(item), price: "1.001", vatRate: "100.01", sortOrder: "2147483648" }, true).map((issue) => issue.field)).toEqual(["price", "vatRate", "sortOrder"]);
    expect(validDate("2024-02-29")).toBe(true); expect(validDate("2023-02-29")).toBe(false); expect(validDate("0000-01-01")).toBe(false);
    expect(validDecimal("1.000000000000000000")).toBe(true); expect(validDecimal("-0,0000012")).toBe(true);
    expect(validDecimal("1234567890123456")).toBe(false); expect(validDecimal("1e3")).toBe(false);
  });

  it("formats saved numeric/date values for the admin language and resolves option names", () => {
    expect(attributeLabel(definition("number", "decimal"), 12.5, "Yes", "No", "cs-CZ")).toBe("12,5");
    expect(attributeLabel(definition("date", "date"), "2024-02-29", "Yes", "No", "cs-CZ")).toBe("29. 2. 2024");
    expect(attributeLabel(definition("choice", "select"), "red", "Yes", "No", "en-GB")).toBe("RED");
    expect(attributeLabel(definition("flag", "boolean"), false, "Yes", "No", "en-GB")).toBe("No");
  });
});
