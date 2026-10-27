// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { HttpError, saveDownload, type Download } from "../api/http";
import { storeExportsApi } from "../api/storeExports";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { metadataListing, metadataStore } from "../test/metadataFixtures";
import { orderPage } from "../test/orderFixtures";
import { StoreExportsPage } from "./StoreExportsPage";

vi.mock("../api/http", async original => ({ ...await original<typeof import("../api/http")>(), saveDownload: vi.fn() }));
const download: Download = { blob: new Blob(["workbook"]), filename: "Žlutý katalog.xlsx" };
const account = (role = "Owner") => ({ id: "user", tenantId: "tenant-a", role, email: "staff@example.test" });
function Shell() {
  const [store, setStore] = useState(metadataStore), [user, setUser] = useState(account()), [refreshing, setRefreshing] = useState(false), [failed, setFailed] = useState(false);
  return <SessionContext value={{ user, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => setStore({ ...store, id: "other-store" })}>Other store</button><button onClick={() => setStore({ ...store, name: "Other name" })}>Rename store</button>
    <button onClick={() => setUser({ ...user, role: "CatalogManager" })}>Other role</button><button onClick={() => setUser({ ...user, id: "other-user" })}>Other session</button><button onClick={() => setUser({ ...user, tenantId: "other-tenant" })}>Other tenant</button>
    <button onClick={() => setRefreshing(value => !value)}>Toggle refresh</button><button onClick={() => setFailed(value => !value)}>Toggle refresh failure</button>
    <Outlet context={{ store, storeRefreshing: refreshing, storeRefreshError: failed ? new HttpError("http", 503) : null, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function route() { return render(<MemoryRouter initialEntries={["/exports?q=old&sort=-price&page=9&archived=true"]}><Routes><Route element={<Shell />}><Route path="exports" element={<StoreExportsPage />} /></Route></Routes></MemoryRouter>); }
const catalog = () => screen.getByRole("button", { name: i18n.t("exports:downloadCatalog") });
const orders = () => screen.getByRole("button", { name: i18n.t("exports:downloadOrders") });
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => { await i18n.changeLanguage("en"); HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value }); vi.spyOn(storeExportsApi, "read").mockResolvedValue(download); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.mocked(saveDownload).mockClear(); window.history.replaceState(null, "", "/"); });
describe("store export workspace", () => {
  it.each(["en", "cs"])("shows precise scope, capacity and round-trip limitations without eagerly exporting in %s", async language => {
    await i18n.changeLanguage(language); route(); expect(screen.getByText(i18n.t("exports:catalogScope"))).toBeTruthy(); expect(screen.getByText(i18n.t("exports:catalogLimit"))).toBeTruthy(); expect(screen.getByText(i18n.t("exports:ordersLimit"))).toBeTruthy(); expect(screen.getByText(i18n.t("exports:catalogAxes"))).toBeTruthy(); expect(screen.getByText(i18n.t("exports:catalogStock"))).toBeTruthy(); expect(screen.getByText(i18n.t("exports:completeness"))).toBeTruthy(); expect(storeExportsApi.read).not.toHaveBeenCalled();
  });
  it.each(["en", "cs"])("downloads each kind explicitly with no inherited list filters in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); route(); await user.click(catalog()); await waitFor(() => expect(saveDownload).toHaveBeenCalledOnce());
    expect(storeExportsApi.read).toHaveBeenNthCalledWith(1, metadataStore.id, "catalog", expect.any(AbortSignal)); expect(screen.getByText(i18n.t("exports:ready", { filename: download.filename }))).toBeTruthy(); expect(document.activeElement?.className).toBe("export-feedback");
    await user.click(orders()); await waitFor(() => expect(saveDownload).toHaveBeenCalledTimes(2)); expect(storeExportsApi.read).toHaveBeenNthCalledWith(2, metadataStore.id, "orders", expect.any(AbortSignal));
  });
  it("locks duplicate and parallel expensive requests until the file resolves", async () => {
    const held = deferred<Download>(); vi.mocked(storeExportsApi.read).mockReturnValueOnce(held.promise); route(); const catalogButton = catalog(), ordersButton = orders();
    act(() => { fireEvent.click(catalogButton); fireEvent.click(catalogButton); fireEvent.click(ordersButton); }); expect(storeExportsApi.read).toHaveBeenCalledOnce(); expect(orders()).toHaveProperty("disabled", true);
    await act(async () => held.resolve(download)); expect(saveDownload).toHaveBeenCalledOnce(); expect(catalog()).toHaveProperty("disabled", false);
  });
  it.each([400, 401, 403, 404, 429, 503])("keeps a %i error in the page and retries only on an explicit click", async status => {
    const user = userEvent.setup(); vi.mocked(storeExportsApi.read).mockRejectedValueOnce(new HttpError("http", status)); route(); await user.click(catalog()); await screen.findByRole("alert"); expect(saveDownload).not.toHaveBeenCalled(); expect(storeExportsApi.read).toHaveBeenCalledOnce(); expect(catalog()).toHaveProperty("disabled", false); await user.click(catalog()); await waitFor(() => expect(saveDownload).toHaveBeenCalledOnce()); expect(storeExportsApi.read).toHaveBeenCalledTimes(2);
  });
  it.each(["network", "invalid-response"] as const)("does not turn a %s response into a downloadable workbook", async kind => {
    const user = userEvent.setup(); vi.mocked(storeExportsApi.read).mockRejectedValueOnce(new HttpError(kind, null)); route(); await user.click(orders()); await screen.findByRole("alert"); expect(saveDownload).not.toHaveBeenCalled(); expect(storeExportsApi.read).toHaveBeenCalledOnce();
  });
  it("cancels locally, ignores the old file and can start another explicit read", async () => {
    const user = userEvent.setup(), held = deferred<Download>(); vi.mocked(storeExportsApi.read).mockReturnValueOnce(held.promise); route(); await user.click(catalog()); const signal = vi.mocked(storeExportsApi.read).mock.calls[0][2]; await user.click(screen.getByRole("button", { name: "Cancel download request" })); expect(signal?.aborted).toBe(true);
    await user.click(orders()); await waitFor(() => expect(saveDownload).toHaveBeenCalledOnce()); await act(async () => held.resolve(download)); expect(saveDownload).toHaveBeenCalledOnce(); expect(storeExportsApi.read).toHaveBeenCalledTimes(2);
  });
  it.each(["refresh", "refresh failure"])("blocks starting a download during a store %s", async issue => {
    const user = userEvent.setup(); route(); await user.click(screen.getByRole("button", { name: issue === "refresh" ? "Toggle refresh" : "Toggle refresh failure" })); expect(catalog()).toHaveProperty("disabled", true); fireEvent.click(catalog()); expect(storeExportsApi.read).not.toHaveBeenCalled();
  });
  it("aborts an in-flight response when the store becomes stale", async () => {
    const user = userEvent.setup(), held = deferred<Download>(); vi.mocked(storeExportsApi.read).mockReturnValueOnce(held.promise); route(); await user.click(catalog()); await user.click(screen.getByRole("button", { name: "Toggle refresh" })); expect(vi.mocked(storeExportsApi.read).mock.calls[0][2]?.aborted).toBe(true); await act(async () => held.resolve(download)); expect(saveDownload).not.toHaveBeenCalled(); await screen.findByText(i18n.t("exports:canceled"));
  });
  it.each(["store", "role", "session", "tenant", "name", "unmount"])("aborts and ignores a departed %s download", async departure => {
    const user = userEvent.setup(), held = deferred<Download>(); vi.mocked(storeExportsApi.read).mockReturnValueOnce(held.promise); const view = route(); await user.click(catalog());
    if (departure === "unmount") view.unmount(); else await user.click(screen.getByRole("button", { name: departure === "name" ? "Rename store" : "Other " + departure })); expect(vi.mocked(storeExportsApi.read).mock.calls[0][2]?.aborted).toBe(true);
    await act(async () => held.resolve(download)); expect(saveDownload).not.toHaveBeenCalled(); expect(screen.queryByText(i18n.t("exports:ready", { filename: download.filename }))).toBeNull();
  });
  it("renders hostile-looking filenames as inert text after handing off the file", async () => {
    const user = userEvent.setup(); vi.mocked(storeExportsApi.read).mockResolvedValueOnce({ ...download, filename: "<img src=x onerror=alert(1)>.xlsx" }); route(); await user.click(catalog()); await screen.findByText(i18n.t("exports:ready", { filename: "<img src=x onerror=alert(1)>.xlsx" })); expect(document.querySelector(".export-feedback img")).toBeNull();
  });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("gates the actual export route and navigation for %s without export requests on entry", async role => {
    vi.spyOn(api, "me").mockResolvedValue(account(role)); vi.spyOn(api, "stores").mockResolvedValue([metadataStore]); window.history.replaceState(null, "", "/stores/" + metadataStore.id + "/exports"); render(<App />); const allowed = ["Owner", "Admin", "CatalogManager"].includes(role);
    await screen.findByRole("heading", { name: allowed ? "Store exports" : "Permission required" }); expect(screen.queryByRole("link", { name: "Exports" }) !== null).toBe(allowed); expect(screen.queryByRole("button", { name: "Download catalog .xlsx" }) !== null).toBe(allowed); expect(screen.queryByRole("button", { name: "Download orders .xlsx" }) !== null).toBe(["Owner", "Admin"].includes(role)); expect(storeExportsApi.read).not.toHaveBeenCalled();
  });
  it.each(["products", "orders"])("links the real filtered %s list to export options without carrying its filters", async area => {
    const user = userEvent.setup(); vi.spyOn(api, "me").mockResolvedValue(account()); vi.spyOn(api, "stores").mockResolvedValue([metadataStore]);
    vi.spyOn(api, "storeProducts").mockResolvedValue({ items: [metadataListing], totalCount: 100, page: 2, pageSize: 25, hasMore: true }); vi.spyOn(api, "orders").mockResolvedValue({ ...orderPage, page: 2 });
    window.history.replaceState(null, "", "/stores/" + metadataStore.id + "/" + area + "?q=Chair&page=2&sort=-price"); render(<App />); const link = await screen.findByRole("link", { name: "Export options" }); expect(link.getAttribute("href")).toBe("/stores/" + metadataStore.id + "/exports"); await user.click(link); await screen.findByRole("heading", { name: "Store exports" }); expect(window.location.search).toBe(""); expect(storeExportsApi.read).not.toHaveBeenCalled();
  });
});
