// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { HttpError } from "../api/http";
import { metadataApi, type MetadataKind, type MetadataSnapshot } from "../api/metadata";
import { initializeI18n, i18n } from "../i18n";
import { SessionContext } from "../session";
import { metadataFixture, metadataId, metadataOtherId, metadataStore } from "../test/metadataFixtures";
import { MetadataPage } from "./MetadataPage";

let current: MetadataSnapshot;
const root = "/stores/" + metadataStore.id;
const path = (kind: MetadataKind, id = metadataId) => root + (kind === "store" ? "" : "/" + (kind === "listing" ? "products" : kind === "category" ? "categories" : "pages") + "/" + id) + "/metadata";
const session = (role = "Owner") => ({ id: "owner", email: "owner@example.test", role, tenantId: "tenant-a" });
function Shell({ initialRole, kind }: { initialRole: string; kind: MetadataKind }) {
  const [store, setStore] = useState(metadataStore), [role, setRole] = useState(initialRole), [staff, setStaff] = useState("owner"), [refreshing, setRefreshing] = useState(false);
  const navigate = useNavigate();
  return <SessionContext value={{ user: { ...session(role), id: staff }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => setStore({ ...metadataStore, id: metadataOtherId })}>Other store</button>
    <button onClick={() => setRole("Support")}>Other role</button><button onClick={() => setStaff("other-owner")}>Other session</button>
    <button onClick={() => navigate(path(kind, metadataOtherId))}>Other record</button>
    <button onClick={() => setStore({ ...metadataStore, name: "Renamed store" })}>Rename store</button>
    <button onClick={() => setRefreshing(value => !value)}>Toggle store refresh</button>
    <Outlet context={{ store, reloadStores: vi.fn(), storeRefreshing: refreshing, storeRefreshError: null }} />
  </SessionContext>;
}
function route(kind: MetadataKind = "listing", role = "Owner") {
  current = metadataFixture(kind);
  return render(<MemoryRouter initialEntries={[path(kind)]}><Routes><Route element={<Shell initialRole={role} kind={kind} />}>
    <Route path="/stores/:storeId/metadata" element={<MetadataPage kind="store" />} />
    <Route path="/stores/:storeId/products/:listingId/metadata" element={<MetadataPage kind="listing" />} />
    <Route path="/stores/:storeId/categories/:categoryId/metadata" element={<MetadataPage kind="category" />} />
    <Route path="/stores/:storeId/pages/:pageId/metadata" element={<MetadataPage kind="page" />} />
  </Route></Routes></MemoryRouter>);
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
async function edit(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: i18n.t("metadata:edit") }));
}
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  vi.stubGlobal("CSS", { escape: (value: string) => value }); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.spyOn(metadataApi, "read").mockImplementation(async () => structuredClone(current));
  vi.spyOn(metadataApi, "save").mockImplementation(async (snapshot, values) => {
    if (snapshot.kind === "store") current = { kind: "store", store: { ...snapshot.store, seo: { titleSuffix: values.title, description: values.description, socialImageUrl: values.socialImageUrl, noIndex: values.noIndex } } };
    else if (snapshot.kind === "listing") current = { ...snapshot, item: { ...snapshot.item, seoTitle: values.title, seoDescription: values.description, seoSocialImageUrl: values.socialImageUrl, seoNoIndex: values.noIndex } };
    else if (snapshot.kind === "category") current = { ...snapshot, item: { ...snapshot.item, seoTitle: values.title, seoDescription: values.description, pageText: values.pageText } };
    else current = { ...snapshot, item: { ...snapshot.item, seoTitle: values.title, seoDescription: values.description, seoNoIndex: values.noIndex } };
    return structuredClone(current);
  });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

describe("metadata workspace", () => {
  it.each(["store", "listing", "category", "page"] as const)("edits and clears all supported %s fields while keeping the original resource", async kind => {
    const user = userEvent.setup(); route(kind); await edit(user);
    expect(document.activeElement).toBe(screen.getByRole("textbox", { name: i18n.t(kind === "store" ? "metadata:titleSuffix" : "metadata:title") }));
    for (const input of screen.getAllByRole("textbox")) fireEvent.change(input, { target: { value: " " } });
    if (kind !== "category") await user.click(screen.getByRole("checkbox", { name: "Request no indexing" }));
    else expect(screen.queryByRole("checkbox")).toBeNull();
    if (kind !== "store" && kind !== "listing") expect(screen.queryByRole("textbox", { name: "Sharing image address" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Save metadata" })); await screen.findByText("Metadata saved.");
    expect(metadataApi.save).toHaveBeenCalledExactlyOnceWith(metadataFixture(kind), { title: null, description: null, socialImageUrl: null, noIndex: false, pageText: null }, expect.any(AbortSignal));
    expect(screen.queryByRole("textbox")).toBeNull(); expect(metadataApi.read).toHaveBeenCalledTimes(2);
  });
  it.each(["en", "cs"])("focuses validation, retains invalid input, previews fallbacks and cancels in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); route(); await edit(user);
    const title = screen.getByRole("textbox", { name: i18n.t("metadata:title") }), image = screen.getByRole("textbox", { name: i18n.t("metadata:socialImageUrl") });
    fireEvent.change(title, { target: { value: "x".repeat(201) } }); fireEvent.change(image, { target: { value: "javascript:alert(1)" } });
    await user.click(screen.getByRole("button", { name: i18n.t("metadata:save") }));
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("alert"))); expect(metadataApi.save).not.toHaveBeenCalled();
    await user.click(within(screen.getByRole("alert")).getAllByRole("button")[0]); expect(document.activeElement).toBe(title);
    fireEvent.change(title, { target: { value: "" } }); fireEvent.change(image, { target: { value: "" } });
    expect(screen.getByText("Žlutá židle — Dílna")).toBeTruthy(); expect(document.querySelector("img")).toBeNull();
    await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: i18n.t("metadata:edit") })));
    expect(screen.queryByRole("textbox")).toBeNull(); expect(metadataApi.save).not.toHaveBeenCalled();
  });
  it("renders category paragraphs literally and explains store no-index dominance", async () => {
    const user = userEvent.setup(); route("category"); await edit(user);
    const article = screen.getByRole("article", { name: "Category introduction" });
    expect(within(article).getByText("<script>literal</script>")).toBeTruthy(); expect(article.querySelector("script")).toBeNull(); expect(article.lang).toBe("cs-CZ");
    expect(screen.getByText(i18n.t("metadata:storeNoIndex"))).toBeTruthy(); expect(screen.queryByRole("checkbox")).toBeNull();
  });
  it("maps a server field refusal to the retained field and retries only explicitly", async () => {
    const user = userEvent.setup(); route(); await edit(user);
    vi.mocked(metadataApi.save).mockRejectedValueOnce(new HttpError("http", 400, { errors: { "seo.description": ["Invalid"] } }));
    await user.click(screen.getByRole("button", { name: "Save metadata" }));
    await user.click(await screen.findByRole("button", { name: i18n.t("metadata:descriptionInvalid") }));
    expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Description" })); expect(metadataApi.save).toHaveBeenCalledOnce();
    expect(screen.getByRole("textbox", { name: "Title override" })).toHaveProperty("value", "Exact title");
    await user.click(screen.getByRole("button", { name: "Save metadata" })); await screen.findByText("Metadata saved."); expect(metadataApi.save).toHaveBeenCalledTimes(2);
  });
  it("serializes submissions and adopts a confirmed write even if its refresh fails", async () => {
    const user = userEvent.setup(), held = deferred<MetadataSnapshot>(); route(); await edit(user);
    vi.mocked(metadataApi.save).mockReturnValueOnce(held.promise); vi.mocked(metadataApi.read).mockRejectedValueOnce(new HttpError("http", 503));
    const form = screen.getByRole("form", { name: "Edit metadata" }); act(() => { fireEvent.submit(form); fireEvent.submit(form); });
    expect(metadataApi.save).toHaveBeenCalledOnce(); expect(screen.getByRole("button", { name: "Cancel edits" })).toHaveProperty("disabled", true);
    await act(async () => held.resolve(current)); await screen.findByText("Metadata saved."); await screen.findByRole("button", { name: "Try again" });
    expect(screen.getByRole("button", { name: "Edit metadata" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Edit metadata" })).toHaveProperty("disabled", false));
    expect(metadataApi.save).toHaveBeenCalledOnce();
  });
  it.each(["record", "store defaults", "selected store"])("retains but blocks a draft after %s changes", async change => {
    const user = userEvent.setup(); route(); await edit(user);
    fireEvent.change(screen.getByRole("textbox", { name: "Title override" }), { target: { value: "Retained draft" } });
    if (change === "selected store") await user.click(screen.getByRole("button", { name: "Rename store" }));
    else {
      if (change === "store defaults") current.store.seo.description = "Changed store default";
      else if (current.kind === "listing") current.item.price = 2999;
      await user.click(screen.getByRole("button", { name: "Refresh metadata" }));
    }
    await screen.findByText(i18n.t("metadata:changed")); expect(screen.getByRole("textbox", { name: "Title override" })).toHaveProperty("value", "Retained draft");
    fireEvent.submit(screen.getByRole("form", { name: "Edit metadata" })); expect(metadataApi.save).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Cancel edits" })); expect(screen.queryByRole("textbox")).toBeNull();
  });
  it("blocks a save racing a refresh before React repaints", async () => {
    const user = userEvent.setup(), held = deferred<MetadataSnapshot>(); route(); await edit(user); vi.mocked(metadataApi.read).mockReturnValueOnce(held.promise);
    act(() => { fireEvent.click(screen.getByRole("button", { name: "Refresh metadata" })); fireEvent.submit(screen.getByRole("form", { name: "Edit metadata" })); });
    expect(metadataApi.save).not.toHaveBeenCalled(); await act(async () => held.resolve(current));
  });
  it("blocks writes while store details refresh and permits cancelling", async () => {
    const user = userEvent.setup(); route(); await edit(user); await user.click(screen.getByRole("button", { name: "Toggle store refresh" }));
    expect(screen.getByRole("button", { name: "Save metadata" })).toHaveProperty("disabled", true);
    fireEvent.submit(screen.getByRole("form", { name: "Edit metadata" })); expect(metadataApi.save).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Cancel edits" })); expect(screen.queryByRole("textbox")).toBeNull();
  });
  it.each([new HttpError("network", null), new HttpError("invalid-response", 200), new HttpError("http", 503)])("does not repeat an uncertain save after a failed recovery read: %j", async error => {
    const user = userEvent.setup(); route(); await edit(user);
    vi.mocked(metadataApi.save).mockRejectedValueOnce(error); vi.mocked(metadataApi.read).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Save metadata" })); await screen.findByText(i18n.t("metadata:uncertain"));
    expect(screen.getByRole("button", { name: "Finish review of current metadata" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Finish review of current metadata" })).toHaveProperty("disabled", false));
    expect(screen.getByRole("button", { name: "Save metadata" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Finish review of current metadata" })); expect(metadataApi.save).toHaveBeenCalledOnce();
  });
  it.each([401, 403, 404])("hides private metadata after a %i read", async status => {
    const user = userEvent.setup(); route(); await edit(user); vi.mocked(metadataApi.read).mockRejectedValueOnce(new HttpError("http", status));
    await user.click(screen.getByRole("button", { name: "Refresh metadata" })); await screen.findByRole("alert");
    expect(screen.queryByRole("textbox")).toBeNull(); expect(screen.queryByText("Exact title")).toBeNull();
  });
  it("shows a retired listing read-only", async () => {
    vi.mocked(metadataApi.read).mockImplementation(async () => {
      const result = metadataFixture("listing"); if (result.kind === "listing") result.item.archivedAt = "2026-10-25T10:00:00Z"; return result;
    });
    route(); expect(await screen.findByText(i18n.t("metadata:retired"))).toBeTruthy();
    expect(screen.getByRole("button", { name: "Edit metadata" })).toHaveProperty("disabled", true); expect(metadataApi.save).not.toHaveBeenCalled();
  });
  it.each(["store", "role", "session", "record", "unmount"])("aborts writes and discards late replies on %s departure", async departure => {
    const user = userEvent.setup(), held = deferred<MetadataSnapshot>(); const view = route(); await edit(user); vi.mocked(metadataApi.save).mockReturnValueOnce(held.promise);
    await user.click(screen.getByRole("button", { name: "Save metadata" }));
    if (departure === "unmount") view.unmount(); else await user.click(screen.getByRole("button", { name: "Other " + departure }));
    expect(vi.mocked(metadataApi.save).mock.calls[0][2]?.aborted).toBe(true);
    const count = vi.mocked(metadataApi.read).mock.calls.length; await act(async () => held.resolve(current));
    expect(screen.queryByText("Metadata saved.")).toBeNull(); expect(metadataApi.read).toHaveBeenCalledTimes(count);
  });
  it.each((["store", "listing", "category", "page"] as const).flatMap(kind => ["Owner", "Admin", "CatalogManager", "OrderManager", "Support"].map(role => ({ kind, role }))))("gates the actual $kind route for $role", async ({ kind, role }) => {
    current = metadataFixture(kind); vi.spyOn(api, "me").mockResolvedValue(session(role)); vi.spyOn(api, "stores").mockResolvedValue([metadataStore]);
    window.history.replaceState(null, "", path(kind)); render(<App />);
    const allowed = ["Owner", "Admin"].includes(role) || role === "CatalogManager" && ["listing", "category"].includes(kind);
    await screen.findByRole("heading", { name: allowed ? i18n.t(kind === "store" ? "metadata:storeHeading" : "metadata:pageHeading") : "Permission required" });
    expect(metadataApi.read).toHaveBeenCalledTimes(allowed ? 1 : 0);
  });
});
