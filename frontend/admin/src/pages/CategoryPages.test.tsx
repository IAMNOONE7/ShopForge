// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminStore, type AttributeDefinition, type Category } from "../api";
import { HttpError } from "../api/http";
import { categoryPath, canParentCategory } from "../components/categoryTree";
import { categoryDraft, validateCategory } from "../components/categoryValidation";
import { i18n, initializeI18n } from "../i18n";
import { StoreLayout } from "../layouts/StoreLayout";
import { SessionContext } from "../session";
import { CategoriesPage, CategoryDetailPage, NewCategoryPage } from "./CategoryPages";

const store: AdminStore = { id: "store-a", name: "Store A", currency: "CZK", culture: "cs-CZ", status: "draft",
  theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 4 }, logoUrl: null, primaryHostName: null, returnWindowDays: 14, company: null };
const root: Category = { id: "root", name: "Furniture", slug: "furniture", parentId: null, sortOrder: 0, attributeIds: [],
  seoTitle: "Saved title", seoDescription: "Saved description", pageText: "Saved page text" };
const child: Category = { ...root, id: "child", name: "Chairs", slug: "chairs", parentId: "root", sortOrder: 2, attributeIds: ["material", "colour"] };
const leaf: Category = { ...root, id: "leaf", name: "Dining chairs", slug: "dining", parentId: "child" };
const material: AttributeDefinition = { id: "material", name: "Material", code: "material", type: "select", unit: null, isFilterable: true,
  isVisibleOnProductPage: true, sortOrder: 0, options: [] };
const colour = { ...material, id: "colour", code: "colour", name: "Colour" };
const tree = [root, child, leaf];

function route(path = "/stores/store-a/categories/child", role = "Owner") {
  return render(<SessionContext.Provider value={{ user: { id: "user-a", email: "owner@example.test", role, tenantId: "tenant-a" },
    logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[path]}>
      <Link to="/stores/store-b/categories/child">Switch store</Link><Link to="/stores/store-a/categories/root">Switch category</Link>
      <Routes><Route element={<Outlet context={{ stores: { status: "ready", data: [store, { ...store, id: "store-b", name: "Store B" }], refreshing: false, refreshError: null }, reloadStores: () => undefined }} />}>
        <Route path="/stores/:storeId" element={<StoreLayout />}>
          <Route path="categories" element={<CategoriesPage />} />
          <Route path="categories/new" element={<NewCategoryPage />} />
          <Route path="categories/:categoryId" element={<CategoryDetailPage />} />
        </Route>
      </Route></Routes>
    </MemoryRouter>
  </SessionContext.Provider>);
}
function editor() { return document.querySelector<HTMLFormElement>(".category-editor form")!; }
beforeAll(async () => { await initializeI18n(); await i18n.changeLanguage("en"); });
beforeEach(() => {
  vi.stubGlobal("CSS", { escape: (name: string) => name });
  HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });

describe("focused categories", () => {
  it("loads only the category list, exposes hierarchy context and navigates to focused details", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "categories").mockResolvedValue(tree);
    const attributes = vi.spyOn(api, "attributes").mockResolvedValue([material, colour]);
    const listings = vi.spyOn(api, "storeProducts");
    const orders = vi.spyOn(api, "orders");
    route("/stores/store-a/categories");
    await screen.findByRole("link", { name: "Dining chairs" });
    expect(screen.getByRole("link", { name: "Chairs" }).getAttribute("href")).toBe("/stores/store-a/categories/child");
    expect(screen.getByText("Furniture › Chairs")).toBeTruthy();
    expect(attributes).not.toHaveBeenCalled();
    expect(listings).not.toHaveBeenCalled(); expect(orders).not.toHaveBeenCalled();
    await user.click(screen.getByRole("link", { name: "Chairs" }));
    expect(await screen.findByRole("heading", { name: "Chairs" })).toBeTruthy();
    expect(within(screen.getByRole("navigation", { name: "Category ancestry" })).getByRole("link", { name: "Furniture" })).toBeTruthy();
  });

  it("distinguishes read errors from empty categories and lets the read retry", async () => {
    const user = userEvent.setup();
    const read = vi.spyOn(api, "categories").mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue([]);
    route("/stores/store-a/categories");
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText("No active categories yet")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("No active categories yet")).toBeTruthy();
    expect(read).toHaveBeenCalledTimes(2);
  });

  it("creates with a derived Czech slug, retains a failed draft and navigates using the returned ID", async () => {
    const user = userEvent.setup();
    const created = { ...root, id: "created", name: "Žluté židle", slug: "zlute-zidle", parentId: root.id, sortOrder: -2 };
    vi.spyOn(api, "categories").mockResolvedValueOnce(tree).mockResolvedValue([...tree, created]);
    vi.spyOn(api, "attributes").mockResolvedValue([]);
    const create = vi.spyOn(api, "createCategory").mockRejectedValueOnce(new HttpError("http", 409)).mockResolvedValue(created);
    route("/stores/store-a/categories/new");
    const name = await screen.findByRole("textbox", { name: "Name" });
    await user.type(name, "Žluté židle");
    expect(screen.getByText("Derived address: zlute-zidle")).toBeTruthy();
    await user.selectOptions(screen.getByRole("combobox", { name: "Parent category" }), "root");
    await user.clear(screen.getByRole("textbox", { name: "Display order" }));
    await user.type(screen.getByRole("textbox", { name: "Display order" }), "-2");
    await user.click(screen.getByRole("button", { name: "Create category" }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect((name as HTMLInputElement).value).toBe("Žluté židle");
    await user.click(screen.getByRole("button", { name: "Create category" }));
    expect(await screen.findByRole("heading", { name: "Žluté židle" })).toBeTruthy();
    expect(create).toHaveBeenLastCalledWith("store-a", { name: "Žluté židle", slug: null, sortOrder: -2, parentId: "root" });
  });

  it("saves name, slug, order and parent without replacing SEO, text or attribute assignments", async () => {
    const user = userEvent.setup();
    const updated = { ...child, name: "Seats", slug: "seats", sortOrder: 8, parentId: null };
    vi.spyOn(api, "categories").mockResolvedValueOnce(tree).mockResolvedValue([root, updated, leaf]);
    vi.spyOn(api, "attributes").mockResolvedValue([material, colour]);
    const update = vi.spyOn(api, "updateCategory").mockResolvedValue(updated);
    route();
    await user.click(await screen.findByRole("button", { name: "Edit category" }));
    const fields = within(editor());
    await user.clear(fields.getByRole("textbox", { name: "Name" })); await user.type(fields.getByRole("textbox", { name: "Name" }), "Seats");
    await user.clear(fields.getByRole("textbox", { name: "URL slug" })); await user.type(fields.getByRole("textbox", { name: "URL slug" }), "seats");
    await user.clear(fields.getByRole("textbox", { name: "Display order" })); await user.type(fields.getByRole("textbox", { name: "Display order" }), "8");
    await user.selectOptions(fields.getByRole("combobox", { name: "Parent category" }), "");
    await user.click(fields.getByRole("button", { name: "Save category" }));
    expect(await screen.findByRole("heading", { name: "Seats" })).toBeTruthy();
    expect(update).toHaveBeenCalledWith("store-a", "child", { name: "Seats", slug: "seats", sortOrder: 8, parentId: null });
    expect(within(screen.getByRole("navigation", { name: "Category ancestry" })).queryByRole("link", { name: "Furniture" })).toBeNull();
    expect(screen.getByText("Material")).toBeTruthy();
  });

  it("disables self, descendants and parents that would put the branch past the five-level limit", async () => {
    const user = userEvent.setup();
    const chain = [1, 2, 3, 4, 5].map((level) => ({ ...root, id: `level-${level}`, name: `Level ${level}`, parentId: level === 1 ? null : `level-${level - 1}` }));
    vi.spyOn(api, "categories").mockResolvedValue([...tree, ...chain]); vi.spyOn(api, "attributes").mockResolvedValue([]);
    route(); await user.click(await screen.findByRole("button", { name: "Edit category" }));
    const select = screen.getByRole("combobox", { name: "Parent category" }) as HTMLSelectElement;
    const option = (id: string) => [...select.options].find((item) => item.value === id)!;
    expect(option("child").disabled).toBe(true); expect(option("leaf").disabled).toBe(true);
    expect(option("level-3").disabled).toBe(false); expect(option("level-4").disabled).toBe(true);
    expect(option("level-5").disabled).toBe(true);
  });

  it("keeps backend parent validation and edits visible with a focusable field error", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "categories").mockResolvedValue(tree); vi.spyOn(api, "attributes").mockResolvedValue([]);
    const update = vi.spyOn(api, "updateCategory").mockRejectedValue(new HttpError("http", 400, { errors: { parentId: ["Parent vanished"] } }));
    route(); await user.click(await screen.findByRole("button", { name: "Edit category" }));
    await user.selectOptions(screen.getByRole("combobox", { name: "Parent category" }), "");
    await user.click(screen.getByRole("button", { name: "Save category" }));
    const alert = await screen.findByRole("alert");
    expect(update).toHaveBeenCalledTimes(1);
    await user.click(within(alert).getByRole("button"));
    expect(document.activeElement).toBe(screen.getByRole("combobox", { name: "Parent category" }));
    expect((screen.getByRole("combobox", { name: "Parent category" }) as HTMLSelectElement).value).toBe("");
  });

  it("orders assignments, preserves failed changes and can explicitly clear all assignments", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "categories").mockResolvedValue(tree); vi.spyOn(api, "attributes").mockResolvedValue([material, colour]);
    const assign = vi.spyOn(api, "assignCategoryAttributes").mockRejectedValueOnce(new HttpError("http", 409)).mockResolvedValue([]);
    route(); await user.click(await screen.findByRole("button", { name: "Edit attribute assignment" }));
    await user.click(within(editor()).getByRole("button", { name: "Move Colour up" }));
    await user.click(within(editor()).getByRole("button", { name: "Save attribute order" }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(assign).toHaveBeenLastCalledWith("store-a", "child", ["colour", "material"]);
    expect(within(editor()).getAllByRole("listitem").map((item) => item.querySelector("strong")?.textContent)).toEqual(["Colour", "Material"]);
    await user.click(within(editor()).getByRole("button", { name: "Remove Colour" }));
    await user.click(within(editor()).getByRole("button", { name: "Remove Material" }));
    expect(within(editor()).getByText("No attributes are assigned to this category.")).toBeTruthy();
    await user.click(within(editor()).getByRole("button", { name: "Save attribute order" }));
    expect(assign).toHaveBeenLastCalledWith("store-a", "child", []);
  });

  it("allows details to edit independently of an attribute read failure", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "categories").mockResolvedValue(tree);
    const attributes = vi.spyOn(api, "attributes").mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue([material]);
    route(); await screen.findByRole("alert");
    expect((screen.getByRole("button", { name: "Edit attribute assignment" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "Edit category" }));
    expect(screen.getByRole("textbox", { name: "Name" })).toBeTruthy();
    await user.click(within(editor()).getByRole("button", { name: "Cancel" }));
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect((screen.getByRole("button", { name: "Edit attribute assignment" }) as HTMLButtonElement).disabled).toBe(false));
    expect(attributes).toHaveBeenCalledTimes(2);
  });

  it("serializes writes and retries only a failed read after a confirmed save", async () => {
    const user = userEvent.setup();
    let resolve!: (category: Category) => void;
    const update = vi.spyOn(api, "updateCategory").mockReturnValue(new Promise((done) => { resolve = done; }));
    vi.spyOn(api, "categories").mockResolvedValueOnce(tree).mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue(tree);
    vi.spyOn(api, "attributes").mockResolvedValue([material, colour]);
    route(); await user.click(await screen.findByRole("button", { name: "Edit category" }));
    await user.click(screen.getByRole("button", { name: "Save category" }));
    await user.click(screen.getByRole("button", { name: "Saving category…" }));
    expect(update).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("textbox", { name: "Name" }).matches(":disabled")).toBe(true);
    expect((screen.getByRole("button", { name: "Edit attribute assignment" }) as HTMLButtonElement).disabled).toBe(true);
    await act(async () => resolve(child));
    expect(await screen.findByText(/confirmed save does not need to be repeated/)).toBeTruthy();
    expect(editor()).toBeNull();
    expect((screen.getByRole("button", { name: "Edit category" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect((screen.getByRole("button", { name: "Edit category" }) as HTMLButtonElement).disabled).toBe(false));
    expect(update).toHaveBeenCalledTimes(1);
  });

  it("retains a stale draft for reference until it is explicitly cancelled", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "categories").mockResolvedValueOnce(tree).mockResolvedValue([root, { ...child, name: "Server name" }, leaf]);
    vi.spyOn(api, "attributes").mockResolvedValue([]);
    route(); await user.click(await screen.findByRole("button", { name: "Edit category" }));
    await user.type(screen.getByRole("textbox", { name: "Name" }), " draft");
    await user.click(screen.getByRole("button", { name: "Refresh category data" }));
    expect(await screen.findByText(/draft is retained for reference/)).toBeTruthy();
    expect((screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement).value).toBe("Chairs draft");
    expect((screen.getByRole("button", { name: "Save category" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(within(editor()).getByRole("button", { name: "Cancel" }));
    await user.click(screen.getByRole("button", { name: "Edit category" }));
    expect((screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement).value).toBe("Server name");
  });

  it("discards drafts when switching store or category identity", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "categories").mockImplementation(async (id) => id === "store-a" ? tree : [{ ...child, name: "Store B chairs", parentId: null }]);
    vi.spyOn(api, "attributes").mockResolvedValue([]);
    route(); await user.click(await screen.findByRole("button", { name: "Edit category" }));
    await user.type(screen.getByRole("textbox", { name: "Name" }), " private draft");
    await user.click(screen.getByRole("link", { name: "Switch store" }));
    await screen.findByRole("heading", { name: "Store B chairs" });
    expect(screen.queryByRole("textbox", { name: "Name" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Edit category" }));
    expect((screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement).value).toBe("Store B chairs");
    await user.click(screen.getByRole("link", { name: "Switch category" }));
    await screen.findByRole("heading", { name: "Furniture" });
    expect(screen.queryByRole("textbox", { name: "Name" })).toBeNull();
  });

  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("uses catalog write permissions for %s", async (role) => {
    vi.spyOn(api, "categories").mockResolvedValue(tree); vi.spyOn(api, "attributes").mockResolvedValue([material]);
    route(undefined, role); await screen.findByRole("heading", { name: "Chairs" });
    expect(screen.queryByRole("button", { name: "Edit category" }) !== null).toBe(["Owner", "Admin", "CatalogManager"].includes(role));
    expect(screen.getByRole("navigation", { name: "Category ancestry" })).toBeTruthy();
  });

  it("does not load category forms for a missing store or unauthorized creation", async () => {
    const categories = vi.spyOn(api, "categories");
    route("/stores/missing/categories/new"); expect(await screen.findByText("Store not found")).toBeTruthy();
    expect(categories).not.toHaveBeenCalled(); cleanup();
    route("/stores/store-a/categories/new", "Support");
    expect(screen.queryByRole("button", { name: "Create category" })).toBeNull();
    expect(categories).not.toHaveBeenCalled();
  });

  it("distinguishes a missing category and an unavailable parent without inventing an empty assignment", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "categories").mockResolvedValue([{ ...child, parentId: "archived-parent" }]);
    vi.spyOn(api, "attributes").mockResolvedValue([material, colour]);
    route(); await user.click(await screen.findByRole("button", { name: "Edit category" }));
    expect((screen.getByRole("combobox", { name: "Parent category" }) as HTMLSelectElement).value).toBe("archived-parent");
    await user.click(screen.getByRole("button", { name: "Save category" }));
    expect(screen.getByRole("alert")).toBeTruthy();
    await user.selectOptions(screen.getByRole("combobox", { name: "Parent category" }), "");
    expect(screen.getByText("Material")).toBeTruthy(); cleanup();
    route("/stores/store-a/categories/missing"); expect(await screen.findByText("Category not found")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Edit category" })).toBeNull();
  });

  it("offers Czech validation while preserving merchant category names", async () => {
    const user = userEvent.setup(); await i18n.changeLanguage("cs");
    vi.spyOn(api, "categories").mockResolvedValue(tree); vi.spyOn(api, "attributes").mockResolvedValue([]);
    route("/stores/store-a/categories/new");
    await user.click(await screen.findByRole("button", { name: "Vytvořit kategorii" }));
    expect(screen.getByRole("alert")).toBeTruthy(); expect(screen.getByText(/Zadejte název kategorie/)).toBeTruthy();
    expect(screen.getByRole("option", { name: "Furniture" })).toBeTruthy();
  });

  it("validates slug, name and signed integer boundaries and terminates corrupt ancestry", () => {
    expect(validateCategory({ ...categoryDraft(), name: "?", sortOrder: "2147483648" }, []).map((issue) => issue.field)).toEqual(["slug", "sortOrder"]);
    expect(validateCategory({ ...categoryDraft(), name: "Židle", sortOrder: "-2147483648" }, [])).toEqual([]);
    expect(validateCategory({ ...categoryDraft(), name: "x".repeat(201), slug: "Bad--slug", sortOrder: "1.5" }, []).map((issue) => issue.field)).toEqual(["name", "slug", "sortOrder"]);
    expect(canParentCategory(tree, "leaf", "child")).toBe(false);
    expect(categoryPath([{ ...root, parentId: "child" }, child], "child")).toHaveLength(2);
  });
});
