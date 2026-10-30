// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api, type AttributeDefinition, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { attributeDefinition, textAttribute } from "../test/attributeFixtures";
import { AttributeDetailPage, AttributesIndexPage } from "./AttributePages";
import { NewAttributeForm } from "../components/AttributeForms";

const store: AdminStore = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "published", company: null,
  primaryHostName: null, returnWindowDays: 14, logoUrl: null, theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 } };
let saved: AttributeDefinition[];
function route(role = "Owner", definition = saved[0]) {
  return render(<SessionContext value={{ user: { id: "user", role, tenantId: "tenant", email: "owner@example.test" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[`/stores/store-a/attributes/${definition.id}`]}><Routes><Route element={<Outlet context={{ store, reloadStores: vi.fn(), storeRefreshing: false, storeRefreshError: null }} />}>
      <Route path="/stores/:storeId/attributes/:attributeId" element={<AttributeDetailPage />} />
      <Route path="/stores/:storeId/attributes" element={<AttributesIndexPage />} />
    </Route></Routes></MemoryRouter>
  </SessionContext>);
}
const edit = () => within(screen.getByRole("region", { name: "Current attribute change" }));
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); saved = structuredClone([attributeDefinition, textAttribute]);
  vi.spyOn(api, "attributes").mockImplementation(async () => structuredClone(saved));
  vi.spyOn(api, "updateAttribute").mockImplementation(async (_store, id, input) => {
    saved = saved.map(item => item.id === id ? { ...item, ...input } : item); return structuredClone(saved.find(item => item.id === id)!);
  });
  vi.spyOn(api, "renameOption").mockImplementation(async (_store, id, optionId, name) => {
    const item = saved.find(item => item.id === id)!; item.options = item.options.map(option => option.id === optionId ? { ...option, name } : option); return structuredClone(item);
  });
  vi.spyOn(api, "reorderOptions").mockImplementation(async (_store, id, ids) => {
    const item = saved.find(item => item.id === id)!; item.options = ids.map(id => item.options.find(option => option.id === id)!); return structuredClone(item);
  });
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });

it.each(["text", "select", "multiSelect"] as const)("explicitly edits %s search/feed flags and preserves immutable identity and other settings", async type => {
  const index = type === "text" ? 1 : 0;
  const original = { ...saved[index], type, isSearchable: true }; saved[index] = original;
  const user = userEvent.setup(); route("CatalogManager", original);
  await user.click(await screen.findByRole("button", { name: "Edit definition" }));
  expect(edit().getByRole("textbox", { name: "Code" })).toHaveProperty("readOnly", true);
  expect(edit().getByRole("textbox", { name: "Type" })).toHaveProperty("readOnly", true);
  await user.click(edit().getByRole("checkbox", { name: "Include in feeds" }));
  await user.click(edit().getByRole("checkbox", { name: "Include in product search" }));
  await user.click(edit().getByRole("button", { name: "Save changes" }));
  await screen.findByText("Attribute saved.");
  expect(api.updateAttribute).toHaveBeenCalledWith("store-a", original.id, {
    name: original.name, unit: null, sortOrder: 2, isFilterable: original.isFilterable, isVisibleOnProductPage: true, isInFeeds: false, isSearchable: false,
  }, expect.any(AbortSignal));
  expect(saved[index].options).toEqual(original.options);
});

it.each(["integer", "decimal", "boolean", "date"] as const)("keeps search disabled on %s definitions", async type => {
  saved[0] = { ...saved[0], type, options: [] };
  const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Edit definition" }));
  expect(edit().getByRole("checkbox", { name: "Include in product search" })).toHaveProperty("disabled", true);
  expect(edit().getByRole("checkbox", { name: "Include in feeds" })).toHaveProperty("checked", true);
});

it.each(["Beech", "---"])("renames an option to %j without slug validation or changing any code", async name => {
  saved[0].isSearchable = true;
  const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Rename Oak" }));
  const field = edit().getByRole("textbox", { name: "Option name" }); await user.clear(field); await user.type(field, name);
  await user.click(edit().getByRole("button", { name: "Save changes" }));
  await screen.findByText("Option renamed; its code is unchanged.");
  expect(api.renameOption).toHaveBeenCalledWith("store-a", attributeDefinition.id, attributeDefinition.options[0].id, name, expect.any(AbortSignal));
  expect(saved[0].options[0].code).toBe("oak"); expect(saved[0].isInFeeds).toBe(true); expect(saved[0].isSearchable).toBe(true);
});

it("moves options locally and sends one complete order only on explicit save", async () => {
  const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Reorder options" }));
  await user.click(edit().getByRole("button", { name: "Move Walnut up" }));
  await user.click(edit().getByRole("button", { name: "Move Walnut up" }));
  expect(api.reorderOptions).not.toHaveBeenCalled();
  await user.click(edit().getByRole("button", { name: "Save option order" }));
  await screen.findByText("Option order saved.");
  expect(api.reorderOptions).toHaveBeenCalledWith("store-a", attributeDefinition.id,
    [attributeDefinition.options[2].id, attributeDefinition.options[0].id, attributeDefinition.options[1].id], expect.any(AbortSignal));
});

it("names an option removal, keeps an in-use refusal literal and restores opener focus on Escape", async () => {
  const user = userEvent.setup(); const remove = vi.spyOn(api, "removeOption").mockRejectedValue(new HttpError("http", 409, {
    title: "This option cannot be removed", detail: "2 listings are set to it. <b>Change those listings first.</b>",
  })); route();
  const opener = await screen.findByRole("button", { name: "Remove Oak" }); await user.click(opener);
  expect(edit().getByText("Store: Workshop. Attribute: Material (material).")).toBeTruthy(); expect(remove).not.toHaveBeenCalled();
  await user.click(edit().getByRole("button", { name: "Confirm removal" }));
  await screen.findByText("2 listings are set to it. <b>Change those listings first.</b>");
  expect(document.querySelector(".attribute-server-detail b")).toBeNull(); expect(saved[0].options).toHaveLength(3);
  expect(remove).toHaveBeenCalledTimes(1); await user.keyboard("{Escape}");
  await waitFor(() => expect(document.activeElement).toBe(opener));
});

it("uses a named guarded definition removal and navigates only after the actual empty success", async () => {
  const user = userEvent.setup(); const remove = vi.spyOn(api, "removeAttribute").mockImplementation(async (_store, id) => { saved = saved.filter(item => item.id !== id); });
  route(); await user.click(await screen.findByRole("button", { name: "Remove attribute" }));
  expect(edit().getByText(/server refuses removal while any listing/)).toBeTruthy(); expect(remove).not.toHaveBeenCalled();
  await user.click(edit().getByRole("button", { name: "Confirm removal" }));
  await screen.findByRole("heading", { name: "Attributes" });
  expect(remove).toHaveBeenCalledWith("store-a", attributeDefinition.id, expect.any(AbortSignal));
  expect(screen.queryByRole("link", { name: "Material" })).toBeNull(); expect((await screen.findAllByRole("link", { name: "Care" })).length).toBeGreaterThan(0);
});

it("preserves a changed draft while a fresh read detects changed settings, then blocks a forced stale submit", async () => {
  const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Edit definition" }));
  const name = edit().getByRole("textbox", { name: "Name" }); await user.clear(name); await user.type(name, "Local draft");
  saved[0] = { ...saved[0], name: "Changed elsewhere", isInFeeds: false };
  await user.click(screen.getByRole("button", { name: "Refresh attributes" })); await screen.findByText(/This definition changed while you were editing/);
  expect(name).toHaveProperty("value", "Local draft"); expect(edit().getByRole("button", { name: "Save changes" })).toHaveProperty("disabled", true);
  fireEvent.submit(document.querySelector(".attribute-active-editor form")!); expect(api.updateAttribute).not.toHaveBeenCalled();
});

it.each([503, 403])("blocks writes after a failed %s refresh and hides denied private facts", async status => {
  const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Edit definition" }));
  vi.mocked(api.attributes).mockRejectedValueOnce(new HttpError("http", status));
  await user.click(screen.getByRole("button", { name: "Refresh attributes" })); await screen.findByRole("alert");
  if (status === 403) { expect(screen.queryByText("Oak")).toBeNull(); expect(screen.queryByRole("region", { name: "Current attribute change" })).toBeNull(); }
  else { expect(edit().getByRole("button", { name: "Save changes" })).toHaveProperty("disabled", true); }
  expect(api.updateAttribute).not.toHaveBeenCalled();
});

it("requires a fresh-read review after an applied write loses its answer and never replays it", async () => {
  const user = userEvent.setup(); vi.mocked(api.updateAttribute).mockImplementationOnce(async (_store, _id, input) => {
    saved[0] = { ...saved[0], ...input }; throw new HttpError("network", null);
  }); route(); await user.click(await screen.findByRole("button", { name: "Edit definition" }));
  const field = edit().getByRole("textbox", { name: "Name" }); await user.clear(field); await user.type(field, "Applied timber");
  await user.click(edit().getByRole("button", { name: "Save changes" }));
  const review = await screen.findByRole("button", { name: "I reviewed the current values; discard draft" });
  await waitFor(() => expect(review).toHaveProperty("disabled", false));
  expect(api.updateAttribute).toHaveBeenCalledTimes(1); expect(screen.queryByText("Attribute saved.")).toBeNull();
  fireEvent.submit(document.querySelector(".attribute-active-editor form")!); expect(api.updateAttribute).toHaveBeenCalledTimes(1);
  await user.click(review); expect(screen.queryByRole("region", { name: "Current attribute change" })).toBeNull();
  expect(await screen.findByRole("heading", { name: "Applied timber" })).toBeTruthy();
});

it("treats a wrong-code save answer as uncertain and keeps the original read identity", async () => {
  const user = userEvent.setup(); vi.mocked(api.updateAttribute).mockResolvedValue({ ...attributeDefinition, code: "wrong" });
  route(); await user.click(await screen.findByRole("button", { name: "Edit definition" })); await user.click(edit().getByRole("button", { name: "Save changes" }));
  await screen.findByRole("button", { name: "I reviewed the current values; discard draft" }); expect(screen.queryByText("Attribute saved.")).toBeNull();
});

it("locks rapid submissions and ignores a late write when the role loses editing permission", async () => {
  const user = userEvent.setup(); let answer!: (value: AttributeDefinition) => void;
  vi.mocked(api.updateAttribute).mockReturnValue(new Promise(resolve => { answer = resolve; }));
  const view = route(); await user.click(await screen.findByRole("button", { name: "Edit definition" }));
  fireEvent.submit(document.querySelector(".attribute-active-editor form")!); fireEvent.submit(document.querySelector(".attribute-active-editor form")!);
  await waitFor(() => expect(api.updateAttribute).toHaveBeenCalledTimes(1)); const signal = vi.mocked(api.updateAttribute).mock.calls[0][3]!;
  view.unmount(); route("Support"); await screen.findByText("Oak"); expect(signal.aborted).toBe(true);
  await act(async () => answer({ ...attributeDefinition, name: "Late change" })); expect(screen.queryByText("Attribute saved.")).toBeNull();
  expect(screen.queryByRole("button", { name: "Edit definition" })).toBeNull();
});

it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support"])("shows lifecycle controls according to the actual %s role", async role => {
  route(role); await screen.findByText("Oak"); const allowed = ["Owner", "Admin", "CatalogManager"].includes(role);
  expect(screen.queryByRole("button", { name: "Edit definition" }) !== null).toBe(allowed);
  expect(screen.queryByRole("button", { name: "Remove attribute" }) !== null).toBe(allowed);
  expect(screen.queryByRole("button", { name: "Rename Oak" }) !== null).toBe(allowed);
});

it.each(["integer", "decimal", "boolean", "date"] as const)("clears search eligibility when creation switches to %s, while retaining the explicit feed choice", async type => {
  const user = userEvent.setup(); const create = vi.spyOn(api, "createAttribute").mockResolvedValue(attributeDefinition);
  render(<MemoryRouter><NewAttributeForm storeId="store-a" onCreated={vi.fn()} /></MemoryRouter>);
  await user.type(screen.getByRole("textbox", { name: "Name" }), "Dimensions"); await user.selectOptions(screen.getByRole("combobox", { name: "Type" }), "text");
  await user.click(screen.getByRole("checkbox", { name: "Include in product search" })); await user.click(screen.getByRole("checkbox", { name: "Include in feeds" }));
  await user.selectOptions(screen.getByRole("combobox", { name: "Type" }), type); await user.click(screen.getByRole("button", { name: "Create attribute" }));
  expect(create.mock.calls[0][1]).toMatchObject({ type, isSearchable: false, isInFeeds: true });
});

it.each((["select", "multiSelect"] as const).flatMap(type => [true, false].map(isSearchable => ({ type, isSearchable }))))(
  "creates $type with explicit search eligibility $isSearchable and independent filter/feed flags", async ({ type, isSearchable }) => {
    const user = userEvent.setup(); const create = vi.spyOn(api, "createAttribute").mockResolvedValue(attributeDefinition);
    render(<MemoryRouter><NewAttributeForm storeId="store-a" onCreated={vi.fn()} /></MemoryRouter>);
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Tip");
    await user.selectOptions(screen.getByRole("combobox", { name: "Type" }), type);
    const search = screen.getByRole("checkbox", { name: "Include in product search" });
    expect(search).toHaveProperty("disabled", false);
    if (isSearchable) await user.click(search);
    await user.click(screen.getByRole("checkbox", { name: "Filter" }));
    await user.type(screen.getByRole("textbox", { name: "Initial options" }), "Torx\nPhillips");
    await user.click(screen.getByRole("button", { name: "Create attribute" }));
    expect(create).toHaveBeenCalledExactlyOnceWith("store-a", {
      name: "Tip", code: null, type, unit: null, sortOrder: 0, options: ["Torx", "Phillips"],
      isFilterable: false, isVisibleOnProductPage: true, isInFeeds: false, isSearchable,
    }, expect.any(AbortSignal));
  },
);

it("retains a search choice between text and option types without turning it on after an unsupported type", async () => {
  const user = userEvent.setup(); render(<MemoryRouter><NewAttributeForm storeId="store-a" onCreated={vi.fn()} /></MemoryRouter>);
  const type = screen.getByRole("combobox", { name: "Type" });
  const search = screen.getByRole("checkbox", { name: "Include in product search" });
  await user.click(search);
  await user.type(screen.getByRole("textbox", { name: "Initial options" }), "Torx");
  await user.selectOptions(type, "multiSelect");
  expect(search).toHaveProperty("checked", true);
  expect(screen.getByRole("textbox", { name: "Initial options" })).toHaveProperty("value", "Torx");
  await user.selectOptions(type, "text"); expect(search).toHaveProperty("checked", true);
  await user.selectOptions(type, "select"); expect(search).toHaveProperty("checked", true);
  expect(screen.getByRole("textbox", { name: "Initial options" })).toHaveProperty("value", "");
  await user.selectOptions(type, "integer"); expect(search).toHaveProperty("checked", false);
  await user.selectOptions(type, "multiSelect"); expect(search).toHaveProperty("checked", false);
});

it("keeps an option draft after an older API refuses its search flag and retries only after an explicit change", async () => {
  const user = userEvent.setup(); const onCreated = vi.fn();
  const create = vi.spyOn(api, "createAttribute").mockRejectedValueOnce(new HttpError("http", 400, {
    errors: { isSearchable: ["Only text attributes can contribute words to search."] },
  })).mockResolvedValue(attributeDefinition);
  render(<MemoryRouter><NewAttributeForm storeId="store-a" onCreated={onCreated} /></MemoryRouter>);
  await user.type(screen.getByRole("textbox", { name: "Name" }), "Tip");
  const search = screen.getByRole("checkbox", { name: "Include in product search" });
  await user.click(search);
  await user.type(screen.getByRole("textbox", { name: "Initial options" }), "Torx");
  await user.click(screen.getByRole("button", { name: "Create attribute" })); await screen.findByRole("alert");
  expect(create).toHaveBeenCalledTimes(1); expect(onCreated).not.toHaveBeenCalled();
  expect(screen.getByRole("textbox", { name: "Name" })).toHaveProperty("value", "Tip");
  expect(screen.getByRole("textbox", { name: "Initial options" })).toHaveProperty("value", "Torx");
  expect(search).toHaveProperty("checked", true);
  await user.click(search); await user.click(screen.getByRole("button", { name: "Create attribute" }));
  await waitFor(() => expect(onCreated).toHaveBeenCalledTimes(1));
  expect(create.mock.calls.map(([, input]) => input)).toEqual([
    { name: "Tip", code: null, type: "select", unit: null, sortOrder: 0, options: ["Torx"], isFilterable: true, isVisibleOnProductPage: true, isInFeeds: false, isSearchable: true },
    { name: "Tip", code: null, type: "select", unit: null, sortOrder: 0, options: ["Torx"], isFilterable: true, isVisibleOnProductPage: true, isInFeeds: false, isSearchable: false },
  ]);
});

it("retains an uncertain creation without offering another POST and links to actual definitions for recovery", async () => {
  const user = userEvent.setup(); const create = vi.spyOn(api, "createAttribute").mockRejectedValue(new HttpError("network", null));
  render(<MemoryRouter><NewAttributeForm storeId="store-a" onCreated={vi.fn()} /></MemoryRouter>);
  await user.type(screen.getByRole("textbox", { name: "Name" }), "Care"); await user.click(screen.getByRole("button", { name: "Create attribute" }));
  await screen.findByText(/This attribute may have been created/); expect(screen.getByRole("button", { name: "Create attribute" })).toHaveProperty("disabled", true);
  expect(screen.getByRole("link", { name: "Back to attributes" }).getAttribute("href")).toBe("/stores/store-a/attributes");
  fireEvent.submit(document.querySelector("form")!); expect(create).toHaveBeenCalledTimes(1);
});

it("links a server-generated option-code refusal to the retained name field", async () => {
  const user = userEvent.setup(); const add = vi.spyOn(api, "addOption").mockRejectedValue(new HttpError("http", 400, { errors: { code: ["This option code is already in use."] } }));
  route(); await user.click(await screen.findByRole("button", { name: "Add option" }));
  const name = edit().getByRole("textbox", { name: "New option" }); await user.type(name, "Other timber");
  await user.click(edit().getByRole("button", { name: "Add option" }));
  await user.click(await screen.findByRole("button", { name: "This option code is already in use." }));
  expect(document.activeElement).toBe(name); expect(name).toHaveProperty("value", "Other timber"); expect(add).toHaveBeenCalledTimes(1);
});
