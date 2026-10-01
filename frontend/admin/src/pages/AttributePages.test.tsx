// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { api, type AdminStore, type AttributeDefinition, type AttributeType } from "../api";
import { EditAttributeForm, NewAttributeForm } from "../components/AttributeForms";
import { AttributeOptions } from "../components/AttributeOptions";
import { generatedCode, validateAttribute, validateOption, type AttributeDraft } from "../components/attributeValidation";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { AttributeDetailPage, AttributesIndexPage, NewAttributePage } from "./AttributePages";

const store: AdminStore = { id: "store-a", name: "Store", currency: "CZK", culture: "cs-CZ", status: "draft", theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 4 }, logoUrl: null, primaryHostName: null, returnWindowDays: 14, company: null };
const definition: AttributeDefinition = { id: "attribute-a", code: "material", name: "Material", type: "select", unit: null, isFilterable: true, isVisibleOnProductPage: true, sortOrder: 2, options: [{ id: "option-a", code: "cotton", name: "Cotton" }] };

function route(path: string, role = "Owner") {
  return render(<SessionContext.Provider value={{ user: { id: "user-a", email: "owner@example.test", role, tenantId: "tenant-a" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[path]}><Routes><Route element={<Outlet context={{ store, reloadStores: () => undefined }} />}>
      <Route path="/attributes" element={<AttributesIndexPage />} />
      <Route path="/attributes/:attributeId" element={<AttributeDetailPage />} />
    </Route></Routes></MemoryRouter>
  </SessionContext.Provider>);
}

beforeAll(async () => { await initializeI18n(); await i18n.changeLanguage("en"); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
afterAll(() => i18n.changeLanguage("en"));

describe("attribute definitions", () => {
  it("validates generated codes, duplicate option codes, and sort order", () => {
    const draft: AttributeDraft = { name: "Žlutá barva", code: "", type: "select", unit: "", isFilterable: true, isVisibleOnProductPage: true, sortOrder: "0", options: "Červená\nCervena" };
    expect(generatedCode(draft.name)).toBe("zluta-barva");
    expect(validateAttribute(draft, true).map((issue) => issue.key)).toEqual(["optionsDuplicate"]);
    expect(validateAttribute({ ...draft, options: "Blue", code: "Bad Code", sortOrder: "1.2" }, true).map((issue) => issue.field)).toEqual(["code", "sortOrder"]);
    expect(validateOption("Cóttón", ["cotton"])).toBe("optionsDuplicate");
  });

  it.each(["select", "multiSelect", "decimal", "integer", "boolean", "date", "text"] as AttributeType[])("creates the supported %s type", async (type) => {
    const user = userEvent.setup();
    const create = vi.spyOn(api, "createAttribute").mockResolvedValue({ ...definition, type });
    render(<NewAttributeForm storeId="store-a" onCreated={() => undefined} />);
    await user.type(screen.getByRole("textbox", { name: /Name/ }), "Sample");
    await user.selectOptions(screen.getByRole("combobox", { name: "Type" }), type);
    await user.click(screen.getByRole("button", { name: "Create attribute" }));
    await waitFor(() => expect(create).toHaveBeenCalledTimes(1));
    expect(create.mock.calls[0][1]).toMatchObject({ name: "Sample", type, code: null, sortOrder: 0, isFilterable: type !== "text" });
    if (type === "text") expect(screen.getByRole("checkbox", { name: "Filter" }).hasAttribute("disabled")).toBe(true);
  });

  it("returns to the attribute detail route after creation", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "createAttribute").mockResolvedValue(definition);
    vi.spyOn(api, "attributes").mockResolvedValue([definition]);
    render(<MemoryRouter initialEntries={["/stores/store-a/attributes/new"]}><Routes>
      <Route element={<Outlet context={{ store, reloadStores: () => undefined }} />}>
        <Route path="/stores/:storeId/attributes/new" element={<NewAttributePage />} />
        <Route path="/stores/:storeId/attributes/:attributeId" element={<SessionContext.Provider value={{ user: { id: "user-a", email: "owner@example.test", role: "Owner", tenantId: "tenant-a" }, logout: async () => undefined, logoutPending: false, logoutError: null }}><AttributeDetailPage /></SessionContext.Provider>} />
      </Route>
    </Routes></MemoryRouter>);
    expect(screen.getByRole("link", { name: "Back to attributes" }).getAttribute("href")).toBe("/stores/store-a/attributes");
    await user.type(screen.getByRole("textbox", { name: /Name/ }), "Material");
    await user.click(screen.getByRole("button", { name: "Create attribute" }));
    expect(await screen.findByRole("heading", { name: "Edit definition" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "Back to attributes" }).getAttribute("href")).toBe("/stores/store-a/attributes");
  });

  it("keeps edit identity fixed and preserves unrelated settings on update failure", async () => {
    const user = userEvent.setup();
    const update = vi.spyOn(api, "updateAttribute").mockRejectedValue(new Error("offline"));
    render(<EditAttributeForm storeId="store-a" definition={definition} reload={() => undefined} />);
    expect((screen.getByRole("textbox", { name: "Code" }) as HTMLInputElement).readOnly).toBe(true);
    expect((screen.getByRole("textbox", { name: "Type" }) as HTMLInputElement).readOnly).toBe(true);
    const name = screen.getByRole("textbox", { name: "Name" });
    await user.clear(name);
    await user.type(name, "Fabric");
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    await waitFor(() => expect(update).toHaveBeenCalledWith("store-a", "attribute-a", { name: "Fabric", unit: null, isFilterable: true, isVisibleOnProductPage: true, sortOrder: 2 }));
    expect((name as HTMLInputElement).value).toBe("Fabric");
    expect(await screen.findByText(/could not be saved/i)).toBeTruthy();
  });

  it("keeps options visible while rejecting duplicate codes and failed option writes", async () => {
    const user = userEvent.setup();
    const add = vi.spyOn(api, "addOption").mockRejectedValue(new Error("offline"));
    render(<AttributeOptions storeId="store-a" definition={definition} allowed reload={() => undefined} />);
    const field = screen.getByRole("textbox", { name: "New option" });
    await user.type(field, "Cóttón");
    await user.click(screen.getByRole("button", { name: "Add option" }));
    expect(add).not.toHaveBeenCalled();
    expect(screen.getByText(/unique code/)).toBeTruthy();
    await user.clear(field);
    await user.type(field, "Linen");
    await user.click(screen.getByRole("button", { name: "Add option" }));
    await waitFor(() => expect(add).toHaveBeenCalledTimes(1));
    expect((field as HTMLInputElement).value).toBe("Linen");
    expect(screen.getByText("Cotton")).toBeTruthy();
  });

  it("does not present a failed attributes read as an empty list or empty options", async () => {
    vi.spyOn(api, "attributes").mockRejectedValue(new Error("offline"));
    route("/attributes");
    expect(await screen.findByText(/could not be loaded/i)).toBeTruthy();
    expect(screen.queryByText("No attributes yet")).toBeNull();
    cleanup();
    route("/attributes/attribute-a");
    expect(await screen.findByText(/could not be loaded/i)).toBeTruthy();
    expect(screen.queryByText(/No options yet/)).toBeNull();
  });

  it("shows read-only details and options for non-catalog roles", async () => {
    vi.spyOn(api, "attributes").mockResolvedValue([definition]);
    route("/attributes/attribute-a", "Support");
    expect(await screen.findByText("Cotton")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Save changes" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Add option" })).toBeNull();
  });
});
