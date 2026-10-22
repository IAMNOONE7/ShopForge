// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import { setupProgressApi, type SetupDetail, type SetupSummary } from "../api/setupProgress";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { StoreOperationsPage } from "./StoreOperationsPage";
import { StoreSetupPage, StoreSetupDetailPage } from "./StoreSetupPage";

const id = "11111111-1111-1111-1111-111111111111";
const store: AdminStore = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "published", logoUrl: null, company: null, primaryHostName: "shop.localhost", returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#eef4ff", borderRadius: 6 } };
const summary: SetupSummary = { id, kind: "email.domain", subject: null, state: "awaitingInput", currentStep: "verification", createdAt: "2026-10-22T07:00:00Z", finishedAt: null };
const detail: SetupDetail = { id, kind: summary.kind, subject: null, state: "awaitingInput", createdAt: summary.createdAt, finishedAt: null, changedAt: summary.createdAt, nextCheckAt: null,
  stepRunning: true, actions: ["continue", "cancel"], steps: [{ name: "verification", status: "waiting", resourceId: "resource", created: false, requestId: "request", keepsSecret: true,
    attempts: 0, checks: 0, waitingSince: summary.createdAt, note: "Publish the TXT record.", lastError: "Previous check failed", lastErrorAt: summary.createdAt, startedAt: null, finishedAt: null }] };
function Shell({ role: initialRole = "Owner" }: { role?: string }) {
  const [selected, setSelected] = useState(store), [role, setRole] = useState(initialRole); const navigate = useNavigate(), location = useLocation();
  return <SessionContext value={{ user: { id: "staff", email: "staff@example.test", role, tenantId: "tenant" }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => { setSelected({ ...store, id: "store-b", name: "Other shop" }); navigate(`/stores/store-b/setup${location.pathname.endsWith(id) ? "/" + id : ""}`); }}>Other store</button>
    <button onClick={() => setRole("Support")}>New role</button>
    <Outlet context={{ store: selected, storeRefreshing: false, storeRefreshError: null, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function app(path = "/stores/store-a/setup", role = "Owner") {
  return <MemoryRouter initialEntries={[path]}><Routes><Route element={<Shell role={role} />}>
    <Route path="/stores/:storeId/setup" element={<StoreSetupPage />} /><Route path="/stores/:storeId/setup/:operationId" element={<StoreSetupDetailPage />} />
    <Route path="/stores/:storeId/operations" element={<StoreOperationsPage />} />
  </Route></Routes></MemoryRouter>;
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en");
  vi.spyOn(setupProgressApi, "list").mockResolvedValue([summary]); vi.spyOn(setupProgressApi, "detail").mockResolvedValue(detail);
  vi.spyOn(api, "failedMessages").mockResolvedValue([]); vi.spyOn(api, "suppressedAddresses").mockResolvedValue([]);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("shows the latest window, nullable facts and direct links without guessed paging", async () => {
  const user = userEvent.setup(); render(app());
  const link = await screen.findByRole("link", { name: "Connect sending domain" }); expect(link.getAttribute("href")).toBe(`/stores/store-a/setup/${id}`);
  expect(screen.getByText(/50 most recently created/)).toBeTruthy(); expect(screen.getByText("Store-wide setup")).toBeTruthy(); expect(screen.getByText("verification")).toBeTruthy();
  expect(screen.queryByRole("button", { name: /Next page|Previous page/ })).toBeNull();
  await user.click(link); await screen.findByRole("heading", { name: "Recorded steps" });
  expect(setupProgressApi.detail).toHaveBeenCalledExactlyOnceWith("store-a", id, expect.any(AbortSignal));
});
it("reads an older direct detail without needing it in the latest list", async () => {
  vi.mocked(setupProgressApi.list).mockResolvedValue([]); render(app(`/stores/store-a/setup/${id}`));
  await screen.findByRole("heading", { name: "Recorded steps" }); expect(setupProgressApi.list).not.toHaveBeenCalled();
  expect(screen.getByText("Publish the TXT record.")).toBeTruthy(); expect(screen.getByText("Previous check failed")).toBeTruthy();
  expect(screen.getByText(i18n.t("setup:stepRunning"))).toBeTruthy(); expect(screen.getAllByText("0")).toHaveLength(2);
  const user = userEvent.setup(); await user.click(screen.getByText("Technical references"));
  expect(screen.getByText("Reference recorded; the secret is not included")).toBeTruthy(); expect(screen.getByText("resource")).toBeTruthy();
  expect(screen.queryByRole("button", { name: /Continue|Cancel|Retry setup/ })).toBeNull();
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support"])("allows %s to read progress without recovery controls", async role => {
  render(app(`/stores/store-a/setup/${id}`, role)); await screen.findByRole("heading", { name: "Recorded steps" });
  expect(screen.queryByRole("button", { name: /Continue|Cancel|Retry setup/ })).toBeNull();
});
it("keeps unknown kinds/statuses and malicious-looking notes inert without granting actions", async () => {
  vi.mocked(setupProgressApi.detail).mockResolvedValue({ ...detail, kind: "__proto__", state: "futureState", actions: ["futureWrite"], steps: [{ ...detail.steps[0], status: "futureStep", note: '<script>alert(1)</script>\n' + 'long-record-'.repeat(100) }] });
  render(app(`/stores/store-a/setup/${id}`)); await screen.findByText("Other setup: __proto__");
  expect(screen.getByText("Unrecognised status: futureState")).toBeTruthy(); expect(screen.getByText("Unrecognised status: futureStep")).toBeTruthy();
  expect(screen.getByText(/<script>/)).toBeTruthy(); expect(document.querySelector("script, img, [style]")).toBeNull();
});
it("keeps failed/empty/pending reads distinct and retries only the GET", async () => {
  vi.mocked(setupProgressApi.list).mockRejectedValueOnce(new HttpError("http", 503)); const user = userEvent.setup(); render(app());
  await screen.findByRole("alert"); expect(screen.queryByText("No recent setup processes")).toBeNull();
  vi.mocked(setupProgressApi.list).mockResolvedValueOnce([]); await user.click(screen.getByRole("button", { name: "Try again" }));
  await screen.findByRole("heading", { name: "No recent setup processes" }); expect(setupProgressApi.list).toHaveBeenCalledTimes(2);
});
it("retains a stale detail after a failed refresh and recovers by reading", async () => {
  const user = userEvent.setup(); render(app(`/stores/store-a/setup/${id}`)); await screen.findByText("Publish the TXT record.");
  vi.mocked(setupProgressApi.detail).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(screen.getByRole("button", { name: "Refresh progress" }));
  await screen.findByRole("alert"); expect(screen.getByText("Publish the TXT record.")).toBeTruthy(); expect(screen.getByText(/last loaded records/)).toBeTruthy();
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.queryByRole("alert")).toBeNull()); expect(setupProgressApi.detail).toHaveBeenCalledTimes(3);
});
it.each([403, 404])("hides a previous detail when refreshing returns %i", async status => {
  const user = userEvent.setup(); render(app(`/stores/store-a/setup/${id}`)); await screen.findByText("Publish the TXT record.");
  vi.mocked(setupProgressApi.detail).mockRejectedValueOnce(new HttpError("http", status)); await user.click(screen.getByRole("button", { name: "Refresh progress" })); await screen.findByRole("alert");
  expect(screen.queryByText("Publish the TXT record.")).toBeNull();
  if (status === 404) expect(screen.getByRole("heading", { name: "Setup process not found" })).toBeTruthy();
});
it("refuses invalid direct IDs without sending a detail or list request", async () => {
  render(app("/stores/store-a/setup/not-an-id")); expect(screen.getByRole("heading", { name: "Invalid setup link" })).toBeTruthy();
  expect(setupProgressApi.detail).not.toHaveBeenCalled(); expect(setupProgressApi.list).not.toHaveBeenCalled();
});
it("discards a late response when changing stores and aborts the previous read", async () => {
  const held = deferred<SetupSummary[]>(); vi.mocked(setupProgressApi.list).mockReturnValueOnce(held.promise).mockResolvedValueOnce([]);
  const user = userEvent.setup(); render(app()); await user.click(screen.getByRole("button", { name: "Other store" }));
  await screen.findByRole("heading", { name: "No recent setup processes" }); await act(async () => held.resolve([summary]));
  expect(screen.queryByText("Connect sending domain")).toBeNull(); expect(vi.mocked(setupProgressApi.list).mock.calls[0][1]?.aborted).toBe(true);
  expect(setupProgressApi.list).toHaveBeenLastCalledWith("store-b", expect.any(AbortSignal));
});
it("remounts its read scope when the staff role changes", async () => {
  const held = deferred<SetupSummary[]>(); vi.mocked(setupProgressApi.list).mockReturnValueOnce(held.promise).mockResolvedValueOnce([]);
  const user = userEvent.setup(); render(app()); await user.click(screen.getByRole("button", { name: "New role" })); await screen.findByRole("heading", { name: "No recent setup processes" });
  await act(async () => held.resolve([summary])); expect(screen.queryByText("Connect sending domain")).toBeNull(); expect(setupProgressApi.list).toHaveBeenCalledTimes(2);
});
it("discards a late direct detail when the store changes", async () => {
  const held = deferred<SetupDetail>(); vi.mocked(setupProgressApi.detail).mockReturnValueOnce(held.promise).mockRejectedValueOnce(new HttpError("http", 404));
  const user = userEvent.setup(); render(app(`/stores/store-a/setup/${id}`)); await user.click(screen.getByRole("button", { name: "Other store" }));
  await screen.findByRole("heading", { name: "Setup process not found" }); await act(async () => held.resolve(detail));
  expect(screen.queryByText("Publish the TXT record.")).toBeNull(); expect(vi.mocked(setupProgressApi.detail).mock.calls[0][2]?.aborted).toBe(true);
  expect(setupProgressApi.detail).toHaveBeenLastCalledWith("store-b", id, expect.any(AbortSignal));
});
it.each(["cs", "en"])("links the separate %s message screen without renaming its route", async language => {
  await i18n.changeLanguage(language); const user = userEvent.setup(); render(app()); await screen.findByText(i18n.t("setup:wholeStore"));
  await user.click(screen.getByRole("link", { name: i18n.t("navigation:operations") })); await screen.findByRole("heading", { name: i18n.t("operations:title") });
  const back = screen.getByRole("link", { name: i18n.t("navigation:setupProgress") }); expect(back.getAttribute("href")).toBe("/stores/store-a/setup");
  expect(api.failedMessages).toHaveBeenCalledWith("store-a", expect.any(AbortSignal)); expect(api.suppressedAddresses).toHaveBeenCalledWith("store-a", expect.any(AbortSignal));
});
it("routes a protected direct setup page through the real merchant shell", async () => {
  window.history.replaceState(null, "", `/stores/store-a/setup/${id}`);
  vi.spyOn(api, "me").mockResolvedValue({ id: "staff", tenantId: "tenant", role: "Support", email: "staff@example.test" }); vi.spyOn(api, "stores").mockResolvedValue([store]);
  render(<App />); await screen.findByRole("heading", { name: "Recorded steps" });
  const navigation = screen.getByRole("navigation", { name: "Administration" });
  expect(within(navigation).getByRole("link", { name: "Setup progress" }).getAttribute("href")).toBe("/stores/store-a/setup");
  expect(within(navigation).getByRole("link", { name: "Message delivery" }).getAttribute("href")).toBe("/stores/store-a/operations");
});
