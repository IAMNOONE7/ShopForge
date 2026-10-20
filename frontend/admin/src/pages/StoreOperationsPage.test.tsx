// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api, type AdminStore, type FailedMessage } from "../api";
import { HttpError } from "../api/http";
import type { SuppressedAddress } from "../api/operations";
import { initializeI18n, i18n } from "../i18n";
import { SessionContext } from "../session";
import { StoreOperationsPage } from "./StoreOperationsPage";

const message: FailedMessage = { id: "11111111-1111-1111-1111-111111111111", type: "email.requested", attempts: 6, createdAt: "2026-10-20T07:00:00Z", error: "Delivery failed" };
const address: SuppressedAddress = { id: "22222222-2222-2222-2222-222222222222", email: "shopper@example.test", reason: "Complained", detail: "Recipient reported spam", suppressedAt: "2026-10-19T08:00:00Z" };
const store: AdminStore = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "published", logoUrl: null, company: null, primaryHostName: "shop.localhost", returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#eef4ff", borderRadius: 6 } };
let messages: FailedMessage[], addresses: SuppressedAddress[];
type Options = { role?: string; tenant?: string; refreshing?: boolean; refreshError?: unknown };
function Shell({ role = "Owner", tenant = "tenant-a", refreshing = false, refreshError = null }: Options) {
  const [selected, setSelected] = useState(store); const navigate = useNavigate();
  return <SessionContext value={{ user: { id: "staff", email: "staff@example.test", role, tenantId: tenant }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => { setSelected({ ...store, id: "store-b", name: "Other shop" }); navigate("/stores/store-b/operations"); }}>Other store</button>
    <Outlet context={{ store: selected, storeRefreshing: refreshing, storeRefreshError: refreshError, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function app(options: Options = {}) { return <MemoryRouter initialEntries={["/stores/store-a/operations"]}><Routes><Route element={<Shell {...options} />}><Route path="/stores/:storeId/operations" element={<StoreOperationsPage />} /></Route></Routes></MemoryRouter>; }
const panel = (kind: "messages" | "suppressed" = "messages") => within(screen.getByRole("region", { name: i18n.t(`operations:${kind}.title`) }));
async function ready() { await screen.findByRole("heading", { name: address.email }); await screen.findByRole("heading", { name: message.type }); }
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (error: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); messages = [{ ...message }]; addresses = [{ ...address }];
  vi.spyOn(api, "failedMessages").mockImplementation(async id => id === "store-a" ? [...messages] : []);
  vi.spyOn(api, "suppressedAddresses").mockImplementation(async id => id === "store-a" ? [...addresses] : []);
  vi.spyOn(api, "requeueMessage").mockImplementation(async () => { messages = []; });
  vi.spyOn(api, "allowSuppressedAddress").mockImplementation(async () => { addresses = []; });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("loads both bounded lists independently and renders returned facts and inert diagnostics", async () => {
  messages = [{ ...message, error: '<script>throw "unsafe"</script>\n' + "long-detail".repeat(80) }];
  addresses = [{ ...address, reason: "FutureReason", detail: "<img src=x onerror=alert(1)>" }];
  const user = userEvent.setup(); render(app()); await ready();
  expect(screen.getByText(/100 newest failed messages/)).toBeTruthy(); expect(screen.getByText(/200 newest suppressed addresses/)).toBeTruthy();
  expect(screen.getByText("Other provider reason: FutureReason")).toBeTruthy();
  await user.click(panel().getByText("Last technical error")); expect(panel().getByText(/<script>/)).toBeTruthy();
  expect(document.querySelector("script, img, [style]")).toBeNull(); expect(document.querySelector(`time[datetime="${message.createdAt}"]`)).toBeTruthy();
  expect(api.failedMessages).toHaveBeenCalledOnce(); expect(api.suppressedAddresses).toHaveBeenCalledOnce();
});
it("does not label a pending or failed read as an empty list and retries only that read", async () => {
  const pending = deferred<FailedMessage[]>(); vi.mocked(api.failedMessages).mockReturnValueOnce(pending.promise); addresses = [];
  const user = userEvent.setup(); render(app());
  expect(await panel("suppressed").findByText("No suppressed addresses in the latest read.")).toBeTruthy();
  expect(panel().queryByText("No failed messages in the latest read.")).toBeNull();
  await act(async () => pending.reject(new HttpError("http", 503)));
  expect(await panel().findByRole("alert")).toBeTruthy(); expect(panel().queryByText("No failed messages in the latest read.")).toBeNull();
  await user.click(panel().getByRole("button", { name: "Try again" })); await screen.findByRole("heading", { name: message.type });
  expect(api.failedMessages).toHaveBeenCalledTimes(2); expect(api.suppressedAddresses).toHaveBeenCalledOnce(); expect(api.requeueMessage).not.toHaveBeenCalled();
});
it("retains a failed refresh and blocks only its panel while the other list remains actionable", async () => {
  const user = userEvent.setup(); render(app()); await ready();
  vi.mocked(api.failedMessages).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(panel().getByRole("button", { name: "Refresh failed messages" })); await panel().findByRole("alert");
  expect(panel().getByRole("heading", { name: message.type })).toBeTruthy();
  expect(panel().getByRole("button", { name: "Queue again" })).toHaveProperty("disabled", true);
  expect(panel("suppressed").getByRole("button", { name: "Remove suppression" })).toHaveProperty("disabled", false);
  expect(api.suppressedAddresses).toHaveBeenCalledOnce();
});
it.each(["messages", "suppressed"] as const)("confirms the exact %s target and restores focus on Escape without writing", async kind => {
  const user = userEvent.setup(); render(app()); await ready();
  const action = kind === "messages" ? "Queue again" : "Remove suppression", opener = panel(kind).getByRole("button", { name: action });
  await user.click(opener); const form = panel(kind).getByRole("form");
  expect(within(form).getByText(kind === "messages" ? message.id : address.id)).toBeTruthy();
  expect(document.activeElement).toBe(within(form).getByRole("heading"));
  await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(opener));
  expect(api.requeueMessage).not.toHaveBeenCalled(); expect(api.allowSuppressedAddress).not.toHaveBeenCalled();
});
it.each(["messages", "suppressed"] as const)("applies one %s write and distinguishes acknowledgment from email delivery", async kind => {
  const user = userEvent.setup(); render(app()); await ready();
  const action = kind === "messages" ? "Queue again" : "Remove suppression";
  await user.click(panel(kind).getByRole("button", { name: action })); await user.click(within(panel(kind).getByRole("form")).getByRole("button", { name: action }));
  expect(await panel(kind).findByText(i18n.t(`operations:${kind}.success`))).toBeTruthy();
  expect(kind === "messages" ? api.requeueMessage : api.allowSuppressedAddress).toHaveBeenCalledExactlyOnceWith("store-a", kind === "messages" ? message.id : address.id);
  expect(await panel(kind).findByText(i18n.t(`operations:${kind}.empty`))).toBeTruthy();
  expect(kind === "messages" ? api.suppressedAddresses : api.failedMessages).toHaveBeenCalledOnce();
});
it("prevents duplicate submits while a write is pending", async () => {
  const pending = deferred<void>(); vi.mocked(api.requeueMessage).mockReturnValue(pending.promise);
  const user = userEvent.setup(); render(app()); await ready(); await user.click(panel().getByRole("button", { name: "Queue again" }));
  const form = panel().getByRole("form"); fireEvent.submit(form); fireEvent.submit(form);
  expect(api.requeueMessage).toHaveBeenCalledOnce();
  expect(within(form).getByRole("button", { name: "Cancel" })).toHaveProperty("disabled", true);
  await act(async () => pending.resolve());
});
it("keeps an acknowledged action after a failed refresh and retries only the read", async () => {
  const user = userEvent.setup(); render(app()); await ready();
  vi.mocked(api.failedMessages).mockRejectedValueOnce(new HttpError("http", 503));
  await user.click(panel().getByRole("button", { name: "Queue again" })); await user.click(within(panel().getByRole("form")).getByRole("button", { name: "Queue again" }));
  await panel().findByRole("alert"); expect(panel().getByText(i18n.t("operations:messages.success"))).toBeTruthy();
  expect(panel().getByRole("button", { name: "Queue again" })).toHaveProperty("disabled", true);
  await user.click(panel().getByRole("button", { name: "Try again" })); await panel().findByText(i18n.t("operations:messages.empty"));
  expect(api.requeueMessage).toHaveBeenCalledOnce(); expect(api.failedMessages).toHaveBeenCalledTimes(3);
});
it.each([404, 429, 503])("refreshes after an unconfirmed action (%i) without treating absence as delivery", async status => {
  vi.mocked(api.requeueMessage).mockImplementation(async () => { messages = []; throw new HttpError("http", status); });
  const user = userEvent.setup(); render(app()); await ready(); await user.click(panel().getByRole("button", { name: "Queue again" }));
  await user.click(within(panel().getByRole("form")).getByRole("button", { name: "Queue again" }));
  expect(await panel().findByText(i18n.t("operations:absent"))).toBeTruthy();
  expect(panel().queryByText(i18n.t("operations:messages.success"))).toBeNull(); expect(panel().queryByRole("form")).toBeNull();
  expect(api.requeueMessage).toHaveBeenCalledOnce(); expect(api.failedMessages).toHaveBeenCalledTimes(2);
});
it("requires a new confirmation after a lost answer and never replays an unchanged target automatically", async () => {
  vi.mocked(api.allowSuppressedAddress).mockRejectedValue(new HttpError("network", null));
  const user = userEvent.setup(); render(app()); await ready(); await user.click(panel("suppressed").getByRole("button", { name: "Remove suppression" }));
  await user.click(within(panel("suppressed").getByRole("form")).getByRole("button", { name: "Remove suppression" }));
  await panel("suppressed").findByText(i18n.t("operations:uncertain")); expect(panel("suppressed").queryByRole("form")).toBeNull();
  await user.click(panel("suppressed").getByRole("button", { name: "Refresh suppressed addresses" }));
  expect(api.allowSuppressedAddress).toHaveBeenCalledOnce();
  await user.click(panel("suppressed").getByRole("button", { name: "Remove suppression" }));
  expect(panel("suppressed").getByRole("form")).toBeTruthy(); expect(api.allowSuppressedAddress).toHaveBeenCalledOnce();
});
it.each(["messages", "suppressed"] as const)("invalidates an open %s confirmation when its current row changes", async kind => {
  const user = userEvent.setup(); render(app()); await ready();
  await user.click(panel(kind).getByRole("button", { name: kind === "messages" ? "Queue again" : "Remove suppression" }));
  if (kind === "messages") messages = [{ ...message, attempts: 7 }]; else addresses = [];
  await user.click(panel(kind).getByRole("button", { name: kind === "messages" ? "Refresh failed messages" : "Refresh suppressed addresses" }));
  expect(await panel(kind).findByText(i18n.t("operations:changed"))).toBeTruthy();
  expect(within(panel(kind).getByRole("form")).getByRole("button", { name: kind === "messages" ? "Queue again" : "Remove suppression" })).toHaveProperty("disabled", true);
  expect(api.requeueMessage).not.toHaveBeenCalled(); expect(api.allowSuppressedAddress).not.toHaveBeenCalled();
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("uses the current store-management permissions for %s", async role => {
  render(app({ role })); await ready(); const allowed = role === "Owner" || role === "Admin";
  expect(panel().queryByRole("button", { name: "Queue again" }) !== null).toBe(allowed);
  expect(panel("suppressed").queryByRole("button", { name: "Remove suppression" }) !== null).toBe(allowed);
  expect(panel().getByRole("button", { name: "Refresh failed messages" })).toHaveProperty("disabled", false);
});
it.each([{ refreshing: true }, { refreshError: new HttpError("http", 503) }])("blocks mutations for stale store details (%j)", async options => {
  render(app(options)); await ready();
  expect(panel().getByRole("button", { name: "Queue again" })).toHaveProperty("disabled", true);
  expect(panel("suppressed").getByRole("button", { name: "Remove suppression" })).toHaveProperty("disabled", true);
});
it("aborts old reads and discards their rows after switching stores", async () => {
  const pending = deferred<FailedMessage[]>(); vi.mocked(api.failedMessages).mockReturnValueOnce(pending.promise);
  const user = userEvent.setup(); render(app()); await screen.findByRole("heading", { name: address.email });
  const signal = vi.mocked(api.failedMessages).mock.calls[0][1]!;
  await user.click(screen.getByRole("button", { name: "Other store" })); await panel().findByText(i18n.t("operations:messages.empty"));
  expect(signal.aborted).toBe(true); await act(async () => pending.resolve([message]));
  expect(screen.queryByRole("heading", { name: message.type })).toBeNull(); expect(screen.queryByRole("heading", { name: address.email })).toBeNull();
});
it("does not refresh or show an old write result in a newly selected store", async () => {
  const pending = deferred<void>(); vi.mocked(api.requeueMessage).mockReturnValue(pending.promise);
  const user = userEvent.setup(); render(app()); await ready(); await user.click(panel().getByRole("button", { name: "Queue again" }));
  await user.click(within(panel().getByRole("form")).getByRole("button", { name: "Queue again" }));
  await user.click(screen.getByRole("button", { name: "Other store" })); await panel().findByText(i18n.t("operations:messages.empty"));
  await act(async () => pending.resolve()); expect(api.failedMessages).toHaveBeenCalledTimes(2);
  expect(panel().queryByText(i18n.t("operations:messages.success"))).toBeNull();
});
it.each([{ role: "Support" }, { tenant: "tenant-b" }])("discards confirmations on a session scope change (%j)", async scope => {
  const user = userEvent.setup(); const view = render(app()); await ready(); await user.click(panel().getByRole("button", { name: "Queue again" }));
  view.rerender(app(scope)); await ready(); expect(panel().queryByRole("form")).toBeNull();
  expect(api.requeueMessage).not.toHaveBeenCalled(); expect(api.failedMessages).toHaveBeenCalledTimes(2);
});
it.each(["cs", "en"])("localizes both panels and keeps admin markup free of inline styles (%s)", async language => {
  await i18n.changeLanguage(language); render(app()); await ready();
  expect(screen.getByRole("heading", { level: 1, name: i18n.t("operations:title") })).toBeTruthy();
  expect(panel("suppressed").getByText(i18n.t("operations:reasons.complained"), { selector: "dd" })).toBeTruthy();
  expect(document.querySelector("[style], style, script")).toBeNull();
});
