// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { type AdminStore } from "../api";
import { feedsApi, type StoreFeed } from "../api/feeds";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { feedCategories, googleFeed, heurekaFeed, zboziFeed } from "../test/feedFixtures";
import { StoreFeedsPage } from "./StoreFeedsPage";
const store: AdminStore = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "published", company: null,
  primaryHostName: null, returnWindowDays: 14, logoUrl: null, theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 } };
let saved: StoreFeed[], mapped = structuredClone(feedCategories);
function Page({ role = "Owner", stale = false }: { role?: string; stale?: boolean }) {
  return <SessionContext value={{ user: { id: "user", role, tenantId: "tenant", email: "owner@example.test" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <Outlet context={{ store, reloadStores: vi.fn(), storeRefreshing: stale, storeRefreshError: null }} />
  </SessionContext>;
}
function route(feed = "google", role = "Owner", stale = false) {
  return render(<MemoryRouter initialEntries={[`/stores/store-a/feeds${feed ? '/' + feed : ''}`]}><Routes><Route element={<Page role={role} stale={stale} />}>
    <Route path="/stores/:storeId/feeds" element={<StoreFeedsPage />} /><Route path="/stores/:storeId/feeds/:feed" element={<StoreFeedsPage />} />
  </Route></Routes></MemoryRouter>);
}
const editor = () => within(screen.getByRole("region", { name: "Current feed change" }));
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); saved = structuredClone([googleFeed, heurekaFeed, zboziFeed]); mapped = structuredClone(feedCategories);
  vi.spyOn(feedsApi, "list").mockImplementation(async () => structuredClone(saved));
  vi.spyOn(feedsApi, "categories").mockImplementation(async () => structuredClone(mapped));
  vi.spyOn(feedsApi, "check").mockResolvedValue([]);
  vi.spyOn(feedsApi, "save").mockImplementation(async (_store, feed, enabled) => {
    const result = { ...saved.find(item => item.feed === feed)!, isEnabled: enabled }; saved = saved.map(item => item.feed === feed ? result : item); return structuredClone(result);
  });
  vi.spyOn(feedsApi, "mapCategory").mockImplementation(async (_store, _feed, id, value) => {
    mapped = mapped.map(row => row.categoryId === id ? { ...row, engineCategory: value } : row); return structuredClone(mapped.find(row => row.categoryId === id)!);
  });
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });
it("discovers formats, preserves null and prior metrics, and never auto-checks, maps, mutates or collects a file", async () => {
  route(""); expect(await screen.findByRole("link", { name: "Google Merchant Center" })).toHaveProperty("href", "http://localhost:3000/stores/store-a/feeds/google");
  expect(screen.getByRole("link", { name: "Heureka" })).toBeTruthy(); expect(screen.getByRole("link", { name: "Zboží.cz" })).toBeTruthy();
  expect(screen.getByText(/No recorded attempt/)).toBeTruthy(); expect(screen.getByText(zboziFeed.lastError!)).toBeTruthy();
  expect(feedsApi.check).not.toHaveBeenCalled(); expect(feedsApi.categories).not.toHaveBeenCalled(); expect(feedsApi.save).not.toHaveBeenCalled(); expect(document.querySelector('[href*="token="]')).toBeNull();
});
it("reveals and copies only the returned token address explicitly and hides it on refresh", async () => {
  const user = userEvent.setup(), copy = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue(); route();
  await screen.findByRole("button", { name: "Reveal collection address" }); expect(screen.queryByRole("textbox")).toBeNull();
  await user.click(screen.getByRole("button", { name: "Reveal collection address" })); expect(screen.getByRole("textbox", { name: "Collection address with access token" })).toHaveProperty("value", googleFeed.url);
  await user.click(screen.getByRole("button", { name: "Copy collection address" })); expect(copy).toHaveBeenCalledWith(googleFeed.url); await screen.findByText("Address copied.");
  await user.click(screen.getByRole("button", { name: "Refresh feed data" })); await waitFor(() => expect(screen.queryByRole("textbox")).toBeNull());
});
it("edits one explicit enabled setting without producing a file, replacing a token or touching another engine", async () => {
  const user = userEvent.setup(), run = vi.spyOn(feedsApi, "run"), rotate = vi.spyOn(feedsApi, "rotate"); route("google", "CatalogManager");
  await user.click(await screen.findByRole("button", { name: "Edit feed settings" })); await user.click(editor().getByRole("checkbox", { name: "Feed enabled" }));
  expect(feedsApi.save).not.toHaveBeenCalled(); await user.click(editor().getByRole("button", { name: "Save changes" })); await screen.findByText("Feed settings saved. No file was produced by this save.");
  expect(feedsApi.save).toHaveBeenCalledWith("store-a", "google", false, expect.any(AbortSignal)); expect(run).not.toHaveBeenCalled(); expect(rotate).not.toHaveBeenCalled(); expect(saved[1]).toEqual(heurekaFeed);
});
it("names token replacement, supports Escape focus restoration, and does not replay it", async () => {
  const user = userEvent.setup(); vi.spyOn(feedsApi, "rotate").mockImplementation(async () => { const result = { ...saved[0], url: googleFeed.url!.replace('fixture-google-access','fixture-new-access') }; saved[0] = result; return structuredClone(result); }); route();
  const button = await screen.findByRole("button", { name: "Replace access token" }); await user.click(button);
  expect(editor().getByText("Store: Workshop. Feed: Google Merchant Center.")).toBeTruthy(); expect(feedsApi.rotate).not.toHaveBeenCalled();
  await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(button)); await user.click(button); await user.click(editor().getByRole("button", { name: "Replace this feed’s token" }));
  await screen.findByText("Access token replaced. Reveal and copy the current address for the engine."); expect(feedsApi.rotate).toHaveBeenCalledTimes(1);
  await user.click(await screen.findByRole("button", { name: "Reveal collection address" })); expect(screen.getByRole("textbox")).toHaveProperty("value", saved[0].url);
});
it("produces once, renders its returned zero counts and requires enabling an unconfigured feed first", async () => {
  const user = userEvent.setup(); vi.spyOn(feedsApi, "run").mockImplementation(async () => { saved[0] = { ...googleFeed, lastProducts: 0, lastSkipped: 2 }; return structuredClone(saved[0]); }); route();
  await user.click(await screen.findByRole("button", { name: "Produce file now" })); await screen.findByText("Feed file produced. Acceptance and collection by the engine are separate."); expect(feedsApi.run).toHaveBeenCalledTimes(1);
  cleanup(); route("heureka"); expect(await screen.findByRole("button", { name: "Produce file now" })).toHaveProperty("disabled", true); expect(screen.getByRole("button", { name: "Replace access token" })).toHaveProperty("disabled", true);
});
it("refreshes a failed production attempt, preserves previous output and shows the literal reason", async () => {
  const user = userEvent.setup(); vi.spyOn(feedsApi, "run").mockImplementation(async () => { saved[0].lastError = "No domain <required>"; throw new HttpError("http", 409, { detail: "No domain <required>" }); }); route();
  await user.click(await screen.findByRole("button", { name: "Produce file now" })); await screen.findByText("Last attempt failed");
  expect(screen.getAllByText("No domain <required>")).toHaveLength(2); expect(document.querySelector('required')).toBeNull(); expect(screen.getByText(/previous successful file, if one exists/)).toBeTruthy(); expect(feedsApi.run).toHaveBeenCalledTimes(1);
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support"])("applies catalog permissions for %s while retaining check reads", async role => {
  const user = userEvent.setup(); route("google", role); await screen.findByRole("heading", { name: "Feed status" });
  expect(screen.queryByRole("button", { name: "Edit feed settings" }) !== null).toBe(["Owner", "Admin", "CatalogManager"].includes(role));
  await user.click(screen.getByRole("button", { name: "Check Google products" })); await screen.findByText(/No issues were reported/); expect(feedsApi.check).toHaveBeenCalledWith("store-a", "google", expect.any(AbortSignal));
});
it("only offers Google's manual check, pages literal form issues, and does not fake listing IDs", async () => {
  const user = userEvent.setup(); vi.mocked(feedsApi.check).mockResolvedValue(Array.from({ length: 21 }, (_, index) => ({ sku: 'SKU-' + index, name: 'Chair ' + index, problems: ['It has no image <required>.'] }))); route();
  await user.click(await screen.findByRole("button", { name: "Check Google products" })); await screen.findByText("Forms with reported issues: 21"); expect(screen.queryByText("Chair 20")).toBeNull();
  await user.click(screen.getByRole("button", { name: "Next page" })); expect(screen.getByText("Chair 20")).toBeTruthy(); expect(document.querySelector('[href*="SKU-"]')).toBeNull();
  cleanup(); route("heureka"); await screen.findByText(/no preflight check/); expect(screen.queryByRole("button", { name: "Check Google products" })).toBeNull(); expect(feedsApi.check).toHaveBeenCalledTimes(1);
});
it("keeps a failed check distinct from zero problems", async () => {
  const user = userEvent.setup(); vi.mocked(feedsApi.check).mockRejectedValue(new HttpError("http", 404)); route();
  await user.click(await screen.findByRole("button", { name: "Check Google products" })); await screen.findByText(/This error does not imply a successful check/); expect(screen.queryByText(/No issues were reported/)).toBeNull();
});
it("loads mappings explicitly, saves trimmed direct values and clears with null without inventing inheritance", async () => {
  const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Load category mappings" }));
  await user.click(await screen.findByRole("button", { name: "Edit mapping for Tables" })); await user.type(editor().getByRole("textbox", { name: "Engine category" }), "  Home > Tables  ");
  await user.click(editor().getByRole("button", { name: "Save changes" })); await screen.findByText(/Direct category mapping saved/); expect(feedsApi.mapCategory).toHaveBeenLastCalledWith("store-a", "google", feedCategories[1].categoryId, "Home > Tables", expect.any(AbortSignal));
  await user.click(await screen.findByRole("button", { name: "Edit mapping for Tables" })); await user.clear(editor().getByRole("textbox", { name: "Engine category" })); await user.click(editor().getByRole("button", { name: "Save changes" })); await screen.findByText(/Direct category mapping saved/);
  expect(feedsApi.mapCategory).toHaveBeenLastCalledWith("store-a", "google", feedCategories[1].categoryId, null, expect.any(AbortSignal)); expect(screen.getByText("No direct mapping")).toBeTruthy();
});
it("retains an invalid mapping after a known field refusal and focuses its editable input", async () => {
  const user = userEvent.setup(); vi.mocked(feedsApi.mapCategory).mockRejectedValue(new HttpError("http", 400, { errors: { engineCategory: ["Maximum 300 <characters>"] } })); route();
  await user.click(await screen.findByRole("button", { name: "Load category mappings" })); await user.click(await screen.findByRole("button", { name: "Edit mapping for Tables" }));
  await user.type(editor().getByRole("textbox"), "Retained"); await user.click(editor().getByRole("button", { name: "Save changes" })); await user.click(await screen.findByRole("button", { name: "Maximum 300 <characters>" }));
  expect(document.activeElement).toBe(editor().getByRole("textbox")); expect(editor().getByRole("textbox")).toHaveProperty("value", "Retained");
});
it("requires explicit fresh-value review after a lost applied token answer, with no repeated POST", async () => {
  const user = userEvent.setup(); vi.spyOn(feedsApi, "rotate").mockImplementation(async () => { saved[0].url = googleFeed.url!.replace('fixture-google-access','fixture-applied-access'); throw new HttpError("network", null); }); route();
  await user.click(await screen.findByRole("button", { name: "Replace access token" })); await user.click(editor().getByRole("button", { name: "Replace this feed’s token" }));
  const review = await screen.findByRole("button", { name: "Use current values and discard draft" }); await waitFor(() => expect(review).toHaveProperty("disabled", false));
  fireEvent.submit(editor().getByRole("button", { name: "Replace this feed’s token" }).closest('form')!); expect(feedsApi.rotate).toHaveBeenCalledTimes(1);
  await user.click(review); expect(screen.queryByRole("region", { name: "Current feed change" })).toBeNull(); expect(feedsApi.rotate).toHaveBeenCalledTimes(1);
});
it("keeps recovery locked when the fresh collection read fails", async () => {
  const user = userEvent.setup(); vi.spyOn(feedsApi, "rotate").mockImplementation(async () => { vi.mocked(feedsApi.list).mockRejectedValue(new HttpError("network", null)); throw new HttpError("network", null); }); route();
  await user.click(await screen.findByRole("button", { name: "Replace access token" })); await user.click(editor().getByRole("button", { name: "Replace this feed’s token" }));
  expect(await screen.findByRole("button", { name: "Use current values and discard draft" })).toHaveProperty("disabled", true); expect(screen.queryByRole("button", { name: "Reveal collection address" })).toBeNull();
});
it("detects observed changed settings while retaining the draft and blocks forced stale submissions", async () => {
  const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Edit feed settings" })); saved[0].lastProducts = 20;
  await user.click(screen.getByRole("button", { name: "Refresh feed data" })); await screen.findByText(/saved feed or category changed/); expect(editor().getByRole("checkbox")).toHaveProperty("checked", true);
  fireEvent.submit(editor().getByRole("button", { name: "Save changes" }).closest('form')!); expect(feedsApi.save).not.toHaveBeenCalled();
});
it.each([403, 404])("hides previously private facts when a refresh is denied with %s", async status => {
  const user = userEvent.setup(); route(); await user.click(await screen.findByRole("button", { name: "Reveal collection address" })); vi.mocked(feedsApi.list).mockRejectedValue(new HttpError("http", status));
  await user.click(screen.getByRole("button", { name: "Refresh feed data" })); await waitFor(() => expect(screen.queryByRole("heading", { name: "Feed status" })).toBeNull()); expect(screen.queryByRole("textbox")).toBeNull();
});
it("locks immediate repeated writes and aborts a departed scope without surfacing its late answer", async () => {
  let resolve!: (value: StoreFeed) => void; const run = vi.spyOn(feedsApi, "run").mockImplementation(() => new Promise(done => { resolve = done; })); route();
  const button = await screen.findByRole("button", { name: "Produce file now" }); fireEvent.click(button); fireEvent.click(button); expect(run).toHaveBeenCalledTimes(1);
  const signal = run.mock.calls[0][2]!; cleanup(); expect(signal.aborted).toBe(true); await act(async () => resolve(googleFeed)); expect(screen.queryByText(/Feed file produced/)).toBeNull();
});
it("uses the collection to reject an unknown route without probing imaginary details or checks", async () => {
  route("unknown"); await screen.findByText(/not in the store’s current feed list/); expect(feedsApi.check).not.toHaveBeenCalled(); expect(feedsApi.categories).not.toHaveBeenCalled(); expect(feedsApi.save).not.toHaveBeenCalled();
});
it("pauses mutations, checks and address disclosure while store facts are stale", async () => {
  route("google", "Owner", true); expect(await screen.findByRole("button", { name: "Edit feed settings" })).toHaveProperty("disabled", true); expect(screen.getByRole("button", { name: "Check Google products" })).toHaveProperty("disabled", true); expect(screen.queryByRole("button", { name: "Reveal collection address" })).toBeNull();
});
