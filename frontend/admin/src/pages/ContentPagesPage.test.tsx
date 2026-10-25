// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore } from "../api";
import { contentPagesApi, type ContentPage, type ContentPageInput } from "../api/contentPages";
import { HttpError } from "../api/http";
import { generatedCode } from "../components/attributeValidation";
import { initializeI18n, i18n } from "../i18n";
import { SessionContext } from "../session";
import { ContentPageDetailPage, ContentPagesPage, NewContentPage } from "./ContentPagesPage";

const id = "aaaaaaaa-1111-2222-3333-444444444444", otherId = "bbbbbbbb-1111-2222-3333-444444444444";
const page: ContentPage = { id, title: "Terms", slug: "terms", body: "First paragraph.\n\n<script>literal</script>", isPublished: true,
  seoTitle: "Saved search title", seoDescription: "Exact saved description", seoNoIndex: true };
const store: AdminStore = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "published", logoUrl: null, primaryHostName: "shop.localhost", company: null,
  returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 } };
let pages: ContentPage[];
const session = (role: string) => ({ id: "owner", email: "owner@example.test", role, tenantId: "tenant-a" });
function Shell({ initialRole = "Owner" }: { initialRole?: string }) {
  const [selected, setSelected] = useState(store), [role, setRole] = useState(initialRole), [staff, setStaff] = useState("owner"), [refreshing, setRefreshing] = useState(false);
  const navigate = useNavigate();
  return <SessionContext value={{ user: { ...session(role), id: staff }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => { setSelected({ ...store, id: "store-b" }); navigate("/stores/store-b/pages"); }}>Other store</button>
    <button onClick={() => setRole("Support")}>Other role</button><button onClick={() => setStaff("other-owner")}>Other session</button>
    <button onClick={() => navigate(`/stores/store-a/pages/${otherId}`)}>Other page</button>
    <button onClick={() => setSelected(current => ({ ...current, name: "Renamed store" }))}>Rename store</button>
    <button onClick={() => setRefreshing(value => !value)}>Toggle store refresh</button>
    <Outlet context={{ store: selected, reloadStores: vi.fn(), storeRefreshing: refreshing, storeRefreshError: null }} />
  </SessionContext>;
}
function route(path = `/stores/store-a/pages/${id}`, role = "Owner") {
  return render(<MemoryRouter initialEntries={[path]}><Routes><Route element={<Shell initialRole={role} />}>
    <Route path="/stores/:storeId/pages" element={<ContentPagesPage />} /><Route path="/stores/:storeId/pages/new" element={<NewContentPage />} />
    <Route path="/stores/:storeId/pages/:pageId" element={<ContentPageDetailPage />} />
  </Route></Routes></MemoryRouter>);
}
function saved(input: ContentPageInput, pageId = id): ContentPage {
  return { id: pageId, slug: input.slug ?? generatedCode(input.title), title: input.title.trim(), body: input.body.trim(), isPublished: input.isPublished,
    seoTitle: input.seo.title, seoDescription: input.seo.description, seoNoIndex: input.seo.noIndex };
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); pages = [structuredClone(page)];
  vi.stubGlobal("CSS", { escape: (value: string) => value }); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.spyOn(contentPagesApi, "list").mockImplementation(async () => structuredClone(pages));
  vi.spyOn(contentPagesApi, "create").mockImplementation(async (_store, input) => { const result = saved(input, otherId); pages.push(result); return result; });
  vi.spyOn(contentPagesApi, "update").mockImplementation(async (_store, pageId, input) => { const result = saved(input, pageId); pages = pages.map(page => page.id === pageId ? result : page); return result; });
  vi.spyOn(contentPagesApi, "remove").mockImplementation(async (_store, pageId) => { pages = pages.filter(page => page.id !== pageId); });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

async function edit(user: ReturnType<typeof userEvent.setup>) { await user.click(await screen.findByRole("button", { name: i18n.t("contentPages:edit") })); }
async function createDraft(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByRole("textbox", { name: i18n.t("contentPages:title") }), "Žluté židle");
  fireEvent.change(screen.getByRole("textbox", { name: i18n.t("contentPages:body") }), { target: { value: " First.\n\n<b>literal</b> " } });
}

describe("store content workspace", () => {
  it("keeps read failure, empty list and absent direct detail distinct", async () => {
    const user = userEvent.setup(); vi.mocked(contentPagesApi.list).mockRejectedValueOnce(new HttpError("http", 503)); pages = [];
    route("/stores/store-a/pages"); await screen.findByRole("alert"); expect(screen.queryByText("No content pages yet")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByText("No content pages yet");
    await user.click(screen.getByRole("link", { name: "New page" })); expect(await screen.findByRole("textbox", { name: "Page title" })).toBeTruthy();
  });
  it("resolves detail from the list, accepts equivalent ID casing and renders literal paragraphs", async () => {
    route(`/stores/store-a/pages/${id.toUpperCase()}`);
    expect(await screen.findByRole("heading", { name: "Terms" })).toBeTruthy();
    const text = screen.getByRole("article", { name: "Saved page text" });
    expect(within(text).getByText("<script>literal</script>").textContent).toBe("<script>literal</script>");
    expect(text.querySelector("script")).toBeNull(); expect(text.lang).toBe("cs-CZ");
    expect(contentPagesApi.list).toHaveBeenCalledExactlyOnceWith("store-a", expect.any(AbortSignal));
  });
  it.each([otherId, "bad-id"])("does not confuse an unavailable page %s with a new draft", async pageId => {
    route(`/stores/store-a/pages/${pageId}`); await screen.findByRole("heading", { name: "Page not found" });
    expect(screen.queryByRole("button", { name: "Create draft" })).toBeNull(); expect(contentPagesApi.list).toHaveBeenCalledTimes(pageId === "bad-id" ? 0 : 1);
  });
  it.each(["en", "cs"])("creates only a draft, previews inert text and navigates using its returned ID in %s", async language => {
    await i18n.changeLanguage(language); pages = []; const user = userEvent.setup(); route("/stores/store-a/pages/new");
    await screen.findByRole("textbox", { name: i18n.t("contentPages:title") });
    await user.click(screen.getByRole("button", { name: i18n.t("contentPages:create") }));
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("alert"))); expect(contentPagesApi.create).not.toHaveBeenCalled();
    await user.click(within(screen.getByRole("alert")).getByRole("button", { name: new RegExp(i18n.t("contentPages:title") + ":") }));
    expect(document.activeElement).toBe(screen.getByRole("textbox", { name: i18n.t("contentPages:title") }));
    await createDraft(user); expect(screen.queryByRole("alert")).toBeNull(); await user.click(screen.getByRole("button", { name: i18n.t("contentPages:preview") }));
    const preview = screen.getByRole("article", { name: i18n.t("contentPages:preview") });
    expect(within(preview).getByText("<b>literal</b>")).toBeTruthy(); expect(preview.querySelector("b")).toBeNull();
    await user.click(screen.getByRole("button", { name: i18n.t("contentPages:create") }));
    await screen.findByText(i18n.t("contentPages:notices.created"));
    expect(contentPagesApi.create).toHaveBeenCalledExactlyOnceWith("store-a", { title: "Žluté židle", slug: null, body: " First.\n\n<b>literal</b> ", isPublished: false,
      seo: { title: null, description: null, noIndex: false } }, expect.any(AbortSignal));
    expect(await screen.findByRole("heading", { name: "Žluté židle" })).toBeTruthy();
    expect(screen.getByText(i18n.t("contentPages:draft"), { selector: ".content-state" })).toBeTruthy(); expect(contentPagesApi.update).not.toHaveBeenCalled();
    expect(contentPagesApi.list).toHaveBeenCalledTimes(2);
  });
  it("retains text and SEO after a duplicate refusal, focuses its slug and retries only explicitly", async () => {
    const user = userEvent.setup(); route(); await edit(user);
    const input = screen.getByRole("textbox", { name: "Address slug" }); expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Page title" }));
    await user.clear(input); await user.type(input, "new-address");
    expect(screen.getByText(/old address will not redirect/)).toBeTruthy();
    fireEvent.change(screen.getByRole("textbox", { name: "Page text" }), { target: { value: "Changed\n\nText" } });
    vi.mocked(contentPagesApi.update).mockRejectedValueOnce(new HttpError("http", 409, { title: "The slug is already used in this store" }));
    await user.click(screen.getByRole("button", { name: "Save text" }));
    await user.click(await screen.findByRole("button", { name: i18n.t("contentPages:slugTaken") })); expect(document.activeElement).toBe(input);
    expect(screen.getByRole("textbox", { name: "Page text" })).toHaveProperty("value", "Changed\n\nText");
    await user.click(screen.getByRole("button", { name: "Save text" })); await screen.findByText("Page text saved.");
    expect(contentPagesApi.update).toHaveBeenLastCalledWith("store-a", id, { title: "Terms", slug: "new-address", body: "Changed\n\nText", isPublished: true,
      seo: { title: page.seoTitle, description: page.seoDescription, noIndex: true } }, expect.any(AbortSignal));
    expect(contentPagesApi.update).toHaveBeenCalledTimes(2); expect(screen.getByText("Published", { selector: ".content-state" })).toBeTruthy();
  });
  it.each(["title", "slug", "body"] as const)("maps server %s feedback to the retained editor", async field => {
    const user = userEvent.setup(); route(); await edit(user);
    vi.mocked(contentPagesApi.update).mockRejectedValueOnce(new HttpError("http", 400, { errors: { [field]: ["Invalid"] } }));
    await user.click(screen.getByRole("button", { name: "Save text" }));
    await user.click(await screen.findByRole("button", { name: i18n.t(`contentPages:${field}Invalid`) }));
    expect(document.activeElement).toBe(document.getElementById(`content-${field}`)); expect(contentPagesApi.update).toHaveBeenCalledOnce();
  });
  it.each([false, true])("confirms publication changes for published=%s and preserves all text/SEO", async published => {
    pages = [{ ...page, isPublished: published }]; const user = userEvent.setup(); route(); const action = published ? "unpublish" : "publish";
    const opener = await screen.findByRole("button", { name: i18n.t(`contentPages:actions.${action}`) }); await user.click(opener);
    expect(screen.queryByRole("link", { name: "Search and sharing" })).toBeNull();
    expect(contentPagesApi.update).not.toHaveBeenCalled();
    const form = screen.getByRole("form", { name: i18n.t(`contentPages:confirmTitles.${action}`) });
    expect(document.activeElement).toBe(within(form).getByRole("heading"));
    await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(opener));
    expect(screen.getByRole("link", { name: "Search and sharing" }).getAttribute("href")).toBe(`/stores/store-a/pages/${id}/metadata`); await user.click(opener);
    await user.click(screen.getByRole("button", { name: i18n.t(`contentPages:confirmActions.${action}`) }));
    await screen.findByText(i18n.t(`contentPages:notices.${published ? "unpublished" : "published"}`));
    expect(contentPagesApi.update).toHaveBeenCalledExactlyOnceWith("store-a", id, { title: page.title, slug: page.slug, body: page.body, isPublished: !published,
      seo: { title: page.seoTitle, description: page.seoDescription, noIndex: true } }, expect.any(AbortSignal));
  });
  it("locks repeated confirmation, adopts the saved response and retries only a failed refresh", async () => {
    const user = userEvent.setup(), held = deferred<ContentPage>(); pages = [{ ...page, isPublished: false }]; route();
    await user.click(await screen.findByRole("button", { name: "Publish page" }));
    vi.mocked(contentPagesApi.update).mockReturnValueOnce(held.promise); vi.mocked(contentPagesApi.list).mockRejectedValueOnce(new HttpError("http", 503));
    const form = screen.getByRole("form", { name: "Publish this page?" }); act(() => { fireEvent.submit(form); fireEvent.submit(form); });
    expect(contentPagesApi.update).toHaveBeenCalledOnce(); expect(screen.getByRole("button", { name: "Cancel" })).toHaveProperty("disabled", true);
    await act(async () => held.resolve(page)); await screen.findByText("Page published."); await screen.findByRole("button", { name: "Try again" });
    expect(screen.getByText("Published", { selector: ".content-state" })).toBeTruthy(); expect(screen.getByRole("button", { name: "Edit text" })).toHaveProperty("disabled", true);
    pages = [page]; await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Edit text" })).toHaveProperty("disabled", false)); expect(contentPagesApi.update).toHaveBeenCalledOnce();
  });
  it("blocks a confirmation racing a fresh GET before the UI can repaint", async () => {
    const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Unpublish page" }));
    const held = deferred<ContentPage[]>(); vi.mocked(contentPagesApi.list).mockReturnValueOnce(held.promise);
    act(() => { fireEvent.click(screen.getByRole("button", { name: "Refresh pages" })); fireEvent.submit(screen.getByRole("form", { name: "Unpublish this page?" })); });
    expect(contentPagesApi.update).not.toHaveBeenCalled(); await act(async () => held.resolve(pages));
  });
  it("blocks publication when saved metadata changes during its review", async () => {
    const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Unpublish page" }));
    pages = [{ ...page, seoNoIndex: false }]; await user.click(screen.getByRole("button", { name: "Refresh pages" }));
    await screen.findByText(i18n.t("contentPages:changed")); expect(screen.getByRole("button", { name: "Confirm unpublishing" })).toHaveProperty("disabled", true);
    fireEvent.submit(screen.getByRole("form", { name: "Unpublish this page?" })); expect(contentPagesApi.update).not.toHaveBeenCalled();
  });
  it("serializes draft creation and disables editing while the request is pending", async () => {
    const user = userEvent.setup(), held = deferred<ContentPage>(); pages = []; route("/stores/store-a/pages/new"); await createDraft(user);
    vi.mocked(contentPagesApi.create).mockReturnValueOnce(held.promise);
    const form = screen.getByRole("form", { name: "Write a draft" }); act(() => { fireEvent.submit(form); fireEvent.submit(form); });
    expect(contentPagesApi.create).toHaveBeenCalledOnce(); expect(screen.getByRole("textbox", { name: "Page title" }).matches(":disabled")).toBe(true);
    const created = saved(vi.mocked(contentPagesApi.create).mock.calls[0][1], otherId); pages = [created];
    await act(async () => held.resolve(created)); await screen.findByRole("heading", { name: "Žluté židle" }); expect(contentPagesApi.create).toHaveBeenCalledOnce();
  });
  it("does not save while store details are refreshing and restores cancel focus", async () => {
    const user = userEvent.setup(); route(); await edit(user); await user.click(screen.getByRole("button", { name: "Toggle store refresh" }));
    expect(screen.getByRole("button", { name: "Save text" })).toHaveProperty("disabled", true);
    fireEvent.submit(screen.getByRole("form", { name: "Edit page text" })); expect(contentPagesApi.update).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Toggle store refresh" })); screen.getByRole("textbox", { name: "Page text" }).focus(); await user.keyboard("{Escape}");
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Edit text" })));
  });
  it("names deletion, cancels without mutation and returns to the list only after confirmed deletion", async () => {
    const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Delete page" }));
    let form = screen.getByRole("form", { name: "Delete this page?" }); expect(within(form).getByText("Terms")).toBeTruthy(); expect(contentPagesApi.remove).not.toHaveBeenCalled();
    await user.click(within(form).getByRole("button", { name: "Cancel" })); await user.click(screen.getByRole("button", { name: "Delete page" }));
    form = screen.getByRole("form", { name: "Delete this page?" }); await user.click(within(form).getByRole("button", { name: "Delete permanently" }));
    await screen.findByText("Page deleted."); await screen.findByText("No content pages yet"); expect(contentPagesApi.remove).toHaveBeenCalledExactlyOnceWith("store-a", id, expect.any(AbortSignal));
  });
  it("does not call a 404 deletion confirmed, and resolves the missing page from the refreshed list", async () => {
    const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Delete page" }));
    vi.mocked(contentPagesApi.remove).mockImplementationOnce(async () => { pages = []; throw new HttpError("http", 404); });
    await user.click(screen.getByRole("button", { name: "Delete permanently" })); await screen.findByRole("heading", { name: "Page not found" });
    expect(screen.queryByText("Page deleted.")).toBeNull(); expect(contentPagesApi.remove).toHaveBeenCalledOnce();
  });
  it.each(["body", "metadata", "removed", "store"])("retains a draft and blocks saving when %s changes", async change => {
    const user = userEvent.setup(); route(); await edit(user); fireEvent.change(screen.getByRole("textbox", { name: "Page text" }), { target: { value: "Retained draft" } });
    if (change === "store") await user.click(screen.getByRole("button", { name: "Rename store" }));
    else { pages = change === "removed" ? [] : [{ ...page, ...(change === "body" ? { body: "Changed elsewhere" } : { seoDescription: "New metadata" }) }]; await user.click(screen.getByRole("button", { name: "Refresh pages" })); }
    await screen.findByText(i18n.t("contentPages:changed")); expect(screen.getByRole("textbox", { name: "Page text" })).toHaveProperty("value", "Retained draft");
    expect(screen.getByRole("button", { name: "Save text" })).toHaveProperty("disabled", true); expect(contentPagesApi.update).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Cancel edits" })); expect(screen.queryByRole("textbox", { name: "Page text" })).toBeNull();
  });
  it.each([new HttpError("network", null), new HttpError("invalid-response", 201), new HttpError("http", 503)])("never repeats an uncertain create, even after a failed recovery GET: %j", async error => {
    const user = userEvent.setup(); pages = []; route("/stores/store-a/pages/new"); await createDraft(user);
    vi.mocked(contentPagesApi.create).mockRejectedValueOnce(error); vi.mocked(contentPagesApi.list).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Create draft" })); await screen.findByText(i18n.t("contentPages:uncertain"));
    expect(screen.getByRole("button", { name: "Finish review of current pages" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Finish review of current pages" })).toHaveProperty("disabled", false));
    expect(screen.getByRole("button", { name: "Create draft" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Finish review of current pages" })); expect(screen.getByRole("textbox", { name: "Page title" })).toHaveProperty("value", "Žluté židle");
    expect(contentPagesApi.create).toHaveBeenCalledOnce();
  });
  it.each([401, 403, 404])("hides old private content after a %i read", async status => {
    const user = userEvent.setup(); route(); await edit(user); await user.click(screen.getByRole("button", { name: "Preview text" }));
    vi.mocked(contentPagesApi.list).mockRejectedValueOnce(new HttpError("http", status)); await user.click(screen.getByRole("button", { name: "Refresh pages" })); await screen.findByRole("alert");
    expect(screen.queryByRole("textbox", { name: "Page text" })).toBeNull(); expect(screen.queryByText("<script>literal</script>")).toBeNull(); expect(contentPagesApi.update).not.toHaveBeenCalled();
  });
  it.each(["store", "role", "session", "page", "unmount"])("aborts an in-flight save and discards late replies on %s departure", async departure => {
    const user = userEvent.setup(), held = deferred<ContentPage>(); const view = route(); await edit(user); vi.mocked(contentPagesApi.update).mockReturnValueOnce(held.promise);
    await user.click(screen.getByRole("button", { name: "Save text" }));
    if (departure === "unmount") view.unmount(); else await user.click(screen.getByRole("button", { name: `Other ${departure}` }));
    expect(vi.mocked(contentPagesApi.update).mock.calls[0][3]?.aborted).toBe(true);
    const count = vi.mocked(contentPagesApi.list).mock.calls.length; await act(async () => held.resolve(page));
    expect(screen.queryByText("Page text saved.")).toBeNull(); expect(contentPagesApi.list).toHaveBeenCalledTimes(count);
  });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("gates the actual direct route and navigation for %s", async role => {
    vi.spyOn(api, "me").mockResolvedValue(session(role)); vi.spyOn(api, "stores").mockResolvedValue([store]);
    window.history.replaceState(null, "", "/stores/store-a/pages"); render(<App />);
    if (["Owner", "Admin"].includes(role)) { await screen.findByRole("heading", { name: "Content pages" }); expect(screen.getByRole("link", { name: "Content pages" }).getAttribute("href")).toBe("/stores/store-a/pages"); }
    else { await screen.findByRole("heading", { name: "Permission required" }); expect(contentPagesApi.list).not.toHaveBeenCalled(); expect(screen.queryByRole("link", { name: "Content pages" })).toBeNull(); }
  });
});
