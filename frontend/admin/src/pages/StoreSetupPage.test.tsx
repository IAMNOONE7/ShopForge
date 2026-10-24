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
  const [staff, setStaff] = useState("staff"), [storeRefreshing, setStoreRefreshing] = useState(false);
  return <SessionContext value={{ user: { id: staff, email: "staff@example.test", role, tenantId: "tenant" }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => { setSelected({ ...store, id: "store-b", name: "Other shop" }); navigate(`/stores/store-b/setup${location.pathname.endsWith(id) ? "/" + id : ""}`); }}>Other store</button>
    <button onClick={() => setRole("Support")}>New role</button>
    <button onClick={() => setStaff("other-staff")}>New session</button>
    <button onClick={() => navigate(`/stores/${selected.id}/setup/22222222-2222-2222-2222-222222222222`)}>Other process</button>
    <button onClick={() => setStoreRefreshing(value => !value)}>Toggle store refresh</button>
    <Outlet context={{ store: selected, storeRefreshing, storeRefreshError: null, reloadStores: vi.fn() }} />
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
  vi.spyOn(setupProgressApi, "act").mockRejectedValue(new Error("Unexpected setup action"));
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
  expect((screen.getByRole("button", { name: "Continue setup" }) as HTMLButtonElement).disabled).toBe(true);
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("allows %s to read progress while guarding leased recovery", async role => {
  render(app(`/stores/store-a/setup/${id}`, role)); await screen.findByRole("heading", { name: "Recorded steps" });
  const controls = screen.queryAllByRole("button", { name: /Continue setup|Cancel setup|Retry setup/ });
  if (["Owner", "Admin"].includes(role)) { expect(controls).toHaveLength(2); expect(controls.every(button => (button as HTMLButtonElement).disabled)).toBe(true); }
  else expect(controls).toHaveLength(0);
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

function recoveryItem(change: Partial<SetupDetail> = {}): SetupDetail { return { ...detail, stepRunning: false, subject: "mail.workshop.cz", ...change }; }
async function recovery(change: Partial<SetupDetail> = {}, role = "Owner") {
  vi.mocked(setupProgressApi.detail).mockResolvedValue(recoveryItem(change)); const user = userEvent.setup();
  const view = render(app(`/stores/store-a/setup/${id}`, role)); await screen.findByRole("heading", { name: "Recorded steps" }); return { user, ...view };
}
it.each(["Owner", "Admin"])("lets %s review the exact named target, close with Escape and return focus without writing", async role => {
  const { user } = await recovery({}, role), opener = screen.getByRole("button", { name: "Cancel setup" });
  await user.click(opener); const form = screen.getByRole("form", { name: "Review setup cancellation" });
  await waitFor(() => expect(document.activeElement).toBe(within(form).getByRole("heading")));
  expect(within(form).getByText("Workshop")).toBeTruthy(); expect(within(form).getByText("mail.workshop.cz")).toBeTruthy(); expect(within(form).getByText(id)).toBeTruthy();
  expect(within(form).getByText(/Existing or irreversible resources may remain/)).toBeTruthy();
  await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(opener)); expect(screen.queryByRole("form")).toBeNull(); expect(setupProgressApi.act).not.toHaveBeenCalled();
});
it.each([
  { state: "failed", actions: ["retry"], action: "retry", label: "Retry setup", confirm: "Confirm retry", next: "running" },
  { state: "failed", actions: ["retry"], action: "retry", label: "Retry setup", confirm: "Confirm retry", next: "cancelling" },
  { state: "awaitingInput", actions: ["continue"], action: "continue", label: "Continue setup", confirm: "Confirm continuation", next: "running" },
  { state: "awaitingInput", actions: ["cancel"], action: "cancel", label: "Cancel setup", confirm: "Confirm cancellation", next: "cancelling" },
])("submits $action once and shows the authoritative $next reply while follow-up reading is pending", async ({ state, actions, action, label, confirm, next }) => {
  const held = deferred<SetupDetail>(), after = deferred<SetupDetail>(); vi.mocked(setupProgressApi.act).mockReturnValueOnce(held.promise);
  const { user } = await recovery({ state, actions }); await user.click(screen.getByRole("button", { name: label }));
  if (action === "retry") expect(screen.getByText(/this retries its cleanup instead/)).toBeTruthy();
  vi.mocked(setupProgressApi.detail).mockReturnValueOnce(after.promise);
  const form = screen.getByRole("form"); act(() => { form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); });
  expect(setupProgressApi.act).toHaveBeenCalledExactlyOnceWith("store-a", id, action, expect.any(AbortSignal));
  expect((screen.getByRole("button", { name: confirm }) as HTMLButtonElement).disabled).toBe(true);
  expect((screen.getByRole("button", { name: "Refresh progress" }) as HTMLButtonElement).disabled).toBe(true);
  const reply = recoveryItem({ state: next, actions: [], steps: [{ ...detail.steps[0], note: "Authoritative action reply" }] });
  await act(async () => held.resolve(reply)); await screen.findByText("Authoritative action reply"); expect(screen.getByText(/server accepted the request/)).toBeTruthy();
  expect(screen.queryByText("Cancelled")).toBeNull(); expect(setupProgressApi.detail).toHaveBeenCalledTimes(2);
  await act(async () => after.resolve(reply)); expect(setupProgressApi.act).toHaveBeenCalledTimes(1);
});
it.each(["CatalogManager", "OrderManager", "Warehouse", "Support", "futureRole"])("never enables recovery for %s even when the server offers it", async role => {
  await recovery({}, role); expect(screen.queryByRole("button", { name: /Continue setup|Cancel setup|Retry setup/ })).toBeNull(); expect(setupProgressApi.act).not.toHaveBeenCalled();
});
it.each([
  { kind: "future" }, { state: "future" }, { actions: ["continue", "future"] }, { steps: [{ ...detail.steps[0], status: "future" }] },
  { state: "succeeded", actions: ["retry", "continue", "cancel"] }, { state: "cancelling", actions: ["cancel"] }, { state: "running", actions: ["retry", "continue"] },
])("does not invent recovery for unsupported or incompatible progress %j", async change => {
  await recovery(change); expect(screen.queryByRole("button", { name: /Continue setup|Cancel setup|Retry setup/ })).toBeNull();
});
it("blocks confirmation during a read, then requires a new review when progress changed", async () => {
  const { user } = await recovery(); await user.click(screen.getByRole("button", { name: "Continue setup" }));
  const held = deferred<SetupDetail>(); vi.mocked(setupProgressApi.detail).mockReturnValueOnce(held.promise);
  await user.click(screen.getByRole("button", { name: "Refresh progress" }));
  expect((screen.getByRole("button", { name: "Confirm continuation" }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => held.resolve(recoveryItem({ changedAt: "2026-10-24T08:00:00Z" })));
  expect(screen.getByText(/recorded progress or store details changed/)).toBeTruthy();
  expect((screen.getByRole("button", { name: "Confirm continuation" }) as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole("button", { name: "Close review" })); await user.click(screen.getByRole("button", { name: "Continue setup" }));
  expect((screen.getByRole("button", { name: "Confirm continuation" }) as HTMLButtonElement).disabled).toBe(false); expect(setupProgressApi.act).not.toHaveBeenCalled();
});
it("blocks stale data after a failed read until GET recovery succeeds", async () => {
  const { user } = await recovery(); vi.mocked(setupProgressApi.detail).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(screen.getByRole("button", { name: "Refresh progress" })); await screen.findByRole("alert");
  expect((screen.getByRole("button", { name: "Continue setup" }) as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect((screen.getByRole("button", { name: "Continue setup" }) as HTMLButtonElement).disabled).toBe(false));
  expect(setupProgressApi.act).not.toHaveBeenCalled();
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 200)])("requires current-read review after an unconfirmed action (%j), without replaying it", async failure => {
  const { user } = await recovery(); vi.mocked(setupProgressApi.act).mockRejectedValueOnce(failure);
  const held = deferred<SetupDetail>(); vi.mocked(setupProgressApi.detail).mockReturnValueOnce(held.promise);
  await user.click(screen.getByRole("button", { name: "Continue setup" })); await user.click(screen.getByRole("button", { name: "Confirm continuation" }));
  await screen.findByText(/request is unconfirmed/); const finish = screen.getByRole("button", { name: "Finish review of current progress" });
  expect((finish as HTMLButtonElement).disabled).toBe(true); expect(setupProgressApi.act).toHaveBeenCalledTimes(1);
  await act(async () => held.resolve(recoveryItem())); expect((finish as HTMLButtonElement).disabled).toBe(false);
  expect((screen.getByRole("button", { name: "Continue setup" }) as HTMLButtonElement).disabled).toBe(true);
  await user.click(finish); await user.click(screen.getByRole("button", { name: "Continue setup" }));
  expect(screen.getByRole("form", { name: "Review setup continuation" })).toBeTruthy(); expect(setupProgressApi.act).toHaveBeenCalledTimes(1);
});
it("retains the uncertain latch through failed recovery reads and never turns Try again into a write", async () => {
  const { user } = await recovery(); vi.mocked(setupProgressApi.act).mockRejectedValueOnce(new HttpError("network", null));
  vi.mocked(setupProgressApi.detail).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(screen.getByRole("button", { name: "Continue setup" })); await user.click(screen.getByRole("button", { name: "Confirm continuation" }));
  await screen.findByText(/request is unconfirmed/); expect((screen.getByRole("button", { name: "Finish review of current progress" }) as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect((screen.getByRole("button", { name: "Finish review of current progress" }) as HTMLButtonElement).disabled).toBe(false));
  expect(setupProgressApi.act).toHaveBeenCalledTimes(1); expect(setupProgressApi.detail).toHaveBeenCalledTimes(3);
});
it.each([403, 404, 409, 429])("refreshes after a %i action refusal without implying cancellation completed", async status => {
  const { user } = await recovery(); vi.mocked(setupProgressApi.act).mockRejectedValueOnce(new HttpError("http", status, { title: "Not now", detail: "A step is running <script>literal</script>" }));
  if (status === 403 || status === 404) vi.mocked(setupProgressApi.detail).mockRejectedValueOnce(new HttpError("http", status));
  await user.click(screen.getByRole("button", { name: "Cancel setup" })); await user.click(screen.getByRole("button", { name: "Confirm cancellation" }));
  await screen.findByText(/server refused the request/); expect(setupProgressApi.detail).toHaveBeenCalledTimes(2); expect(setupProgressApi.act).toHaveBeenCalledTimes(1);
  expect(screen.queryByText("Cancelled")).toBeNull(); expect(screen.queryByRole("button", { name: "Finish review of current progress" })).toBeNull();
  if (status === 403 || status === 404) expect(screen.queryByRole("button", { name: "Cancel setup" })).toBeNull();
  if (status === 409) { await user.click(screen.getByText("Server explanation")); expect(screen.getByText(/A step is running <script>/)).toBeTruthy(); expect(document.querySelector("script")).toBeNull(); }
});
it("keeps an accepted authoritative reply visible after a failed follow-up read", async () => {
  const { user } = await recovery(); vi.mocked(setupProgressApi.act).mockResolvedValueOnce(recoveryItem({ state: "cancelling", actions: [], steps: [{ ...detail.steps[0], note: "Cleanup requested" }] }));
  vi.mocked(setupProgressApi.detail).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(screen.getByRole("button", { name: "Cancel setup" })); await user.click(screen.getByRole("button", { name: "Confirm cancellation" }));
  await screen.findByText("Cleanup requested"); expect(screen.getByText("Cancellation in progress")).toBeTruthy(); expect(screen.getByText(/server accepted the request/)).toBeTruthy();
  expect(screen.getByText(/last loaded records/)).toBeTruthy(); expect(setupProgressApi.act).toHaveBeenCalledTimes(1);
});
it("treats a reply for a different immutable target as unconfirmed", async () => {
  const { user } = await recovery(); vi.mocked(setupProgressApi.act).mockResolvedValueOnce(recoveryItem({ subject: "other.cz" }));
  await user.click(screen.getByRole("button", { name: "Continue setup" })); await user.click(screen.getByRole("button", { name: "Confirm continuation" }));
  await screen.findByText(/request is unconfirmed/); expect(screen.queryByText(/server accepted the request/)).toBeNull(); expect(screen.queryByText("other.cz")).toBeNull();
});
it.each(["store", "role", "session", "record", "unmount"])("aborts a pending write and ignores its late reply on %s departure", async departure => {
  const { user, unmount } = await recovery(); const held = deferred<SetupDetail>(); vi.mocked(setupProgressApi.act).mockReturnValueOnce(held.promise);
  await user.click(screen.getByRole("button", { name: "Continue setup" })); await user.click(screen.getByRole("button", { name: "Confirm continuation" }));
  if (departure === "record") vi.mocked(setupProgressApi.detail).mockResolvedValueOnce(recoveryItem({ id: "22222222-2222-2222-2222-222222222222" }));
  if (departure === "unmount") unmount(); else await user.click(screen.getByRole("button", { name: departure === "store" ? "Other store" : departure === "role" ? "New role" : departure === "session" ? "New session" : "Other process" }));
  expect(vi.mocked(setupProgressApi.act).mock.calls[0][3]?.aborted).toBe(true);
  const reads = vi.mocked(setupProgressApi.detail).mock.calls.length;
  await act(async () => held.resolve(recoveryItem({ state: "succeeded", steps: [{ ...detail.steps[0], note: "Late reply" }] })));
  expect(screen.queryByText("Late reply")).toBeNull(); expect(screen.queryByText(/server accepted the request/)).toBeNull(); expect(setupProgressApi.detail).toHaveBeenCalledTimes(reads);
});
it("guards store metadata changes even while a confirmation is open", async () => {
  const { user } = await recovery(); await user.click(screen.getByRole("button", { name: "Continue setup" }));
  await user.click(screen.getByRole("button", { name: "Toggle store refresh" }));
  expect((screen.getByRole("button", { name: "Confirm continuation" }) as HTMLButtonElement).disabled).toBe(true); expect(screen.getByText(/Store details are not current/)).toBeTruthy();
  await user.click(screen.getByRole("button", { name: "Toggle store refresh" }));
  expect((screen.getByRole("button", { name: "Confirm continuation" }) as HTMLButtonElement).disabled).toBe(false); expect(setupProgressApi.act).not.toHaveBeenCalled();
});
