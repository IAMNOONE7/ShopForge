// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { api, type Product } from "../api";
import { HttpError } from "../api/http";
import { ProductVariantsSection } from "../components/ProductVariantsSection";
import { validateOptions, validateVariant, variantDraft } from "../components/variantValidation";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { PhysicalProductPage } from "./PhysicalProductPage";

const only: Product = {
  id: "product-a", sku: "SHIRT-S", ean: "5901234123457", weightGrams: 250, brand: "Maker",
  optionNames: [], images: [], variants: [{
    id: "variant-a", sku: "SHIRT-S", ean: "5901234123457", weightGrams: 250,
    partNumber: "M-1", condition: "Used", optionValues: [], position: 0,
  }],
};
const multi: Product = {
  ...only, optionNames: ["Size", "Colour"], variants: [
    { ...only.variants[0], optionValues: ["S", "Red"] },
    { ...only.variants[0], id: "variant-b", sku: "SHIRT-L", optionValues: ["L", "Blue"], position: 1 },
  ],
};
function renderPage(role = "Owner") {
  return render(<SessionContext.Provider value={{ user: { id: "u", email: "u@example.test", role, tenantId: "tenant" },
    logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={["/products/product-a"]}>
      <Routes><Route path="/products/:productId" element={<PhysicalProductPage />} /></Routes>
    </MemoryRouter>
  </SessionContext.Provider>);
}
function section(product = multi, props = {}) {
  return <MemoryRouter><ProductVariantsSection product={product} canManage refreshing={false}
    refreshFailed={false} reloadProducts={() => undefined} {...props} /></MemoryRouter>;
}
beforeAll(async () => { await initializeI18n(); await i18n.changeLanguage("en"); });
afterEach(async () => { cleanup(); vi.restoreAllMocks(); await i18n.changeLanguage("en"); });

describe("product variant editor", () => {
  it("renames a single default through its variant ID, retains a conflict draft and reconciles the summary", async () => {
    const user = userEvent.setup();
    const updated = { ...only, sku: "RENAMED", ean: null, weightGrams: 0,
      variants: [{ ...only.variants[0], sku: "RENAMED", ean: null, weightGrams: 0 }] };
    const products = vi.spyOn(api, "products").mockResolvedValueOnce([only]).mockResolvedValue([updated]);
    const update = vi.spyOn(api, "updateVariant").mockRejectedValueOnce(new HttpError("http", 409))
      .mockResolvedValue(updated.variants[0]);
    const compatibility = vi.spyOn(api, "updateProduct");
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Edit SHIRT-S" }));
    const sku = screen.getByRole("textbox", { name: "SKU" }) as HTMLInputElement;
    expect(sku.readOnly).toBe(false);
    await user.clear(sku); await user.type(sku, " renamed ");
    await user.clear(screen.getByRole("textbox", { name: "EAN (optional)" }));
    await user.clear(screen.getByRole("textbox", { name: "Weight in grams (optional)" }));
    await user.type(screen.getByRole("textbox", { name: "Weight in grams (optional)" }), "0");
    await user.click(screen.getByRole("button", { name: "Save variant" }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(sku.value).toBe(" renamed ");
    expect(products).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Save variant" }));
    expect(await screen.findByRole("heading", { name: "Product RENAMED" })).toBeTruthy();
    expect(update).toHaveBeenLastCalledWith("product-a", "variant-a", {
      sku: "renamed", ean: null, weightGrams: 0, partNumber: "M-1", condition: "Used", optionValues: [],
    });
    expect(compatibility).not.toHaveBeenCalled();
    expect(screen.getByRole("link", { name: "RENAMED Open shared stock" }).getAttribute("href")).toBe("/stock/variant-a");
    expect((screen.getByRole("button", { name: "Remove RENAMED" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("validates axis names and all existing values, and moves values together with their axis", async () => {
    const user = userEvent.setup();
    const setOptions = vi.spyOn(api, "setProductOptions").mockResolvedValue(multi);
    render(section());
    await user.click(screen.getByRole("button", { name: "Edit option axes" }));
    const colour = screen.getByRole("textbox", { name: "Axis 2 name" });
    await user.clear(colour); await user.type(colour, " size ");
    const blue = screen.getByRole("textbox", { name: "SHIRT-L — size" });
    await user.clear(blue);
    await user.click(screen.getByRole("button", { name: "Save axes and all values" }));
    expect(setOptions).not.toHaveBeenCalled();
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("alert")));
    await user.click(within(screen.getByRole("alert")).getByRole("button", { name: /names must be distinct/ }));
    expect(document.activeElement).toBe(colour);
    await user.clear(colour); await user.type(colour, "Colour");
    await user.type(blue, "Blue");
    await user.click(screen.getByRole("button", { name: "Move axis 2 up" }));
    expect((screen.getByRole("textbox", { name: "Axis 1 name" }) as HTMLInputElement).value).toBe("Colour");
    expect((screen.getByRole("textbox", { name: "SHIRT-S — Colour" }) as HTMLInputElement).value).toBe("Red");
    await user.click(screen.getByRole("button", { name: "Save axes and all values" }));
    expect(setOptions).toHaveBeenCalledWith("product-a", { names: ["Colour", "Size"],
      values: { "variant-a": ["Red", "S"], "variant-b": ["Blue", "L"] } });
  });

  it("adds at most three axes, requires every value, and permits explicit removal of every axis", async () => {
    const user = userEvent.setup();
    const setOptions = vi.spyOn(api, "setProductOptions").mockResolvedValue(multi);
    render(section());
    await user.click(screen.getByRole("button", { name: "Edit option axes" }));
    await user.click(screen.getByRole("button", { name: "Add option axis" }));
    expect((screen.getByRole("button", { name: "Add option axis" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "Save axes and all values" }));
    expect(setOptions).not.toHaveBeenCalled();
    expect(within(screen.getByRole("alert")).getAllByRole("button", { name: /non-empty value/ })).toHaveLength(2);
    for (let index = 0; index < 3; index++) await user.click(screen.getByRole("button", { name: "Remove axis 1" }));
    await user.click(screen.getByRole("button", { name: "Save axes and all values" }));
    expect(setOptions).toHaveBeenCalledWith("product-a", { names: [], values: { "variant-a": [], "variant-b": [] } });
  });

  it("creates one explicit variant, serializes saves and blocks new writes until a successful refresh", async () => {
    const user = userEvent.setup();
    let resolve!: (variant: Product["variants"][number]) => void;
    const add = vi.spyOn(api, "addVariant").mockReturnValue(new Promise((done) => { resolve = done; }));
    const reload = vi.fn();
    const view = render(section(multi, { reloadProducts: reload }));
    await user.click(screen.getByRole("button", { name: "Add variant" }));
    await user.type(screen.getByRole("textbox", { name: "SKU" }), "SHIRT-M");
    await user.click(screen.getByRole("button", { name: "Create variant" }));
    expect(add).not.toHaveBeenCalled();
    await user.type(screen.getByRole("textbox", { name: "Size" }), "M");
    await user.type(screen.getByRole("textbox", { name: "Colour" }), "Green");
    await user.click(screen.getByRole("button", { name: "Create variant" }));
    await user.click(screen.getByRole("button", { name: "Saving product…" }));
    expect(add).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("textbox", { name: "SKU" }).matches(":disabled")).toBe(true);
    expect((screen.getByRole("button", { name: "Edit option axes" }) as HTMLButtonElement).disabled).toBe(true);
    await act(async () => resolve({ ...only.variants[0], id: "variant-c", sku: "SHIRT-M", optionValues: ["M", "Green"] }));
    expect(reload).toHaveBeenCalledTimes(1);
    view.rerender(section(multi, { refreshing: true }));
    expect((screen.getByRole("button", { name: "Add variant" }) as HTMLButtonElement).disabled).toBe(true);
    view.rerender(section(multi, { refreshFailed: true }));
    expect((screen.getByRole("button", { name: "Add variant" }) as HTMLButtonElement).disabled).toBe(true);
    view.rerender(section(multi));
    expect((screen.getByRole("button", { name: "Add variant" }) as HTMLButtonElement).disabled).toBe(false);
    expect(add).toHaveBeenCalledWith("product-a", {
      sku: "SHIRT-M", ean: null, weightGrams: null, partNumber: null, condition: null, optionValues: ["M", "Green"],
    });
  });

  it("names and confirms deletion, keeps a refused deletion open, then uses the refreshed default", async () => {
    const user = userEvent.setup();
    const remaining = { ...multi, sku: "SHIRT-L", variants: [{ ...multi.variants[1], position: 0 }] };
    vi.spyOn(api, "products").mockResolvedValueOnce([multi]).mockResolvedValue([remaining]);
    const remove = vi.spyOn(api, "deleteVariant").mockRejectedValueOnce(new HttpError("http", 409)).mockResolvedValue(undefined);
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Remove SHIRT-S" }));
    expect(screen.getByText(/The next remaining variant/)).toBeTruthy();
    expect(remove).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Remove SHIRT-S" })));
    await user.click(screen.getByRole("button", { name: "Remove SHIRT-S" }));
    await user.click(screen.getByRole("button", { name: "Remove variant" }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Remove variant SHIRT-S?" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Remove variant" }));
    expect(await screen.findByRole("heading", { name: "Product SHIRT-L" })).toBeTruthy();
    expect(remove).toHaveBeenCalledTimes(2);
    expect(remove).toHaveBeenLastCalledWith("product-a", "variant-a");
    expect((screen.getByRole("button", { name: "Remove SHIRT-L" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole("link", { name: "SHIRT-L Open shared stock" }).getAttribute("href")).toBe("/stock/variant-b");
  });

  it("retries only the read after a confirmed creation followed by a failed refresh", async () => {
    const user = userEvent.setup();
    const created = { ...only.variants[0], id: "variant-c", sku: "SHIRT-M", optionValues: ["M", "Green"], position: 2 };
    const products = vi.spyOn(api, "products").mockResolvedValueOnce([multi])
      .mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue([{ ...multi, variants: [...multi.variants, created] }]);
    const add = vi.spyOn(api, "addVariant").mockResolvedValue(created);
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Add variant" }));
    await user.type(screen.getByRole("textbox", { name: "SKU" }), "SHIRT-M");
    await user.type(screen.getByRole("textbox", { name: "Size" }), "M");
    await user.type(screen.getByRole("textbox", { name: "Colour" }), "Green");
    await user.click(screen.getByRole("button", { name: "Create variant" }));
    expect(await screen.findByText(/confirmed save does not need to be repeated/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Create variant" })).toBeNull();
    expect((screen.getByRole("button", { name: "Add variant" }) as HTMLButtonElement).disabled).toBe(true);
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByRole("link", { name: "SHIRT-M Open shared stock" })).toBeTruthy();
    expect((screen.getByRole("button", { name: "Add variant" }) as HTMLButtonElement).disabled).toBe(false);
    expect(add).toHaveBeenCalledTimes(1);
    expect(products).toHaveBeenCalledTimes(3);
  });

  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unrecognized"])("uses current catalog permissions for %s", async (role) => {
    vi.spyOn(api, "products").mockResolvedValue([multi]);
    renderPage(role);
    await screen.findByRole("heading", { name: "Product SHIRT-S" });
    expect(screen.queryByRole("button", { name: "Add variant" }) !== null).toBe(["Owner", "Admin", "CatalogManager"].includes(role));
    expect(screen.getByRole("link", { name: "SHIRT-L Open shared stock" }).getAttribute("href")).toBe("/stock/variant-b");
  });

  it.each([only, multi])("edits the product brand without clearing single-variant physical fields", async (product) => {
    const user = userEvent.setup();
    const update = vi.spyOn(api, "updateProduct").mockResolvedValue(product);
    render(section(product));
    await user.click(screen.getByRole("button", { name: "Edit brand" }));
    await user.clear(screen.getByRole("textbox", { name: "Brand" }));
    await user.click(screen.getByRole("button", { name: "Save brand" }));
    const physical = product.variants.length === 1 ? product.variants[0] : null;
    expect(update).toHaveBeenCalledWith("product-a", { brand: null, ean: physical?.ean ?? null,
      weightGrams: physical?.weightGrams ?? null, partNumber: physical?.partNumber ?? null, condition: physical?.condition ?? null });
  });

  it("retains the original draft when an external product change makes it unsafe to save", async () => {
    const user = userEvent.setup();
    const update = vi.spyOn(api, "updateVariant");
    const view = render(section());
    await user.click(screen.getByRole("button", { name: "Edit SHIRT-S" }));
    await user.clear(screen.getByRole("textbox", { name: "Size" }));
    await user.type(screen.getByRole("textbox", { name: "Size" }), "Draft size");
    view.rerender(section({ ...multi, optionNames: ["Changed", "Colour"] }));
    expect(screen.getByText(/Your draft is retained for reference/)).toBeTruthy();
    expect((screen.getByRole("textbox", { name: "Size" }) as HTMLInputElement).value).toBe("Draft size");
    expect((screen.getByRole("button", { name: "Save variant" }) as HTMLButtonElement).disabled).toBe(true);
    expect(update).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    await user.click(screen.getByRole("button", { name: "Edit SHIRT-S" }));
    expect(screen.getByRole("textbox", { name: "Changed" })).toBeTruthy();
  });

  it("renders Czech editor controls and required axis feedback", async () => {
    await i18n.changeLanguage("cs");
    const user = userEvent.setup();
    render(section());
    await user.click(screen.getByRole("button", { name: "Přidat variantu" }));
    await user.click(screen.getByRole("button", { name: "Vytvořit variantu" }));
    expect(screen.getByRole("alert")).toBeTruthy();
    expect(within(screen.getByRole("alert")).getAllByRole("button", { name: /Zadejte neprázdnou hodnotu/ })).toHaveLength(2);
  });

  it("matches SKU, GTIN and int32 weight boundaries without rejecting server-supported option combinations", () => {
    expect(validateVariant({ ...variantDraft(), sku: "X".repeat(65), ean: "5901234123458", weightGrams: "2147483648" }, [])
      .map((issue) => issue.field)).toEqual(["sku", "ean", "weightGrams"]);
    expect(validateVariant({ ...variantDraft(), sku: "X", ean: "5901234123457", weightGrams: "0", optionValues: ["S", "Red"] }, multi.optionNames)).toEqual([]);
    expect(validateOptions({ names: ["Size"], values: { "variant-a": ["Same"], "variant-b": ["Same"] } }, multi)).toEqual([]);
  });
});
