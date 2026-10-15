// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore, type AdminUnappliedPayment } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { StoreLayout } from "../layouts/StoreLayout";
import { SessionContext } from "../session";
import { orderDetail, unappliedPayment } from "../test/orderFixtures";
import { attemptStorageKey, type UnappliedAttempt } from "../utils/unappliedAttempt";
import { maxUnappliedPage } from "../utils/unappliedQuery";
import { UnappliedPaymentsPage } from "./UnappliedPaymentsPage";
const payment: AdminUnappliedPayment = { ...unappliedPayment, status: "Open", canRefund: true };
const store: AdminStore = { id: "store-a", name: "Store A", currency: "CZK", culture: "cs-CZ", status: "published", primaryHostName: null, logoUrl: null, company: null, returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 8 } };
const page = { items: [payment], page: 1, pageSize: 25, totalCount: 1, hasMore: false };
const storageKey = attemptStorageKey("owner", "store-a");
function History() { const location = useLocation(); const navigate = useNavigate(); return <><output data-testid="location">{location.pathname}{location.search}</output><button onClick={() => navigate(-1)}>History back</button><Link to="/stores/store-b/unapplied-payments">Switch store</Link><Link to="/stores/store-a/unapplied-payments?status=">Other view</Link></>; }
function route(path = "/stores/store-a/unapplied-payments", role = "Owner") {
  return render(<SessionContext value={{ user: { id: "owner", email: "owner@example.test", role, tenantId: "tenant" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[path]}><History /><Routes><Route element={<Outlet context={{ stores: { status: "ready", data: [store, { ...store, id: "store-b" }], refreshing: false, refreshError: null }, reloadStores: () => undefined }} />}>
      <Route path="/stores/:storeId" element={<StoreLayout />}><Route path="unapplied-payments" element={<UnappliedPaymentsPage />} /></Route>
    </Route></Routes></MemoryRouter></SessionContext>);
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>((yes) => { resolve = yes; }); return { promise, resolve }; }
async function open(action: "refund" | "close", user = userEvent.setup()) {
  await user.click(await screen.findByRole("button", { name: action === "refund" ? "Review provider refund" : "Review manual closure" })); return user;
}
function savedAttempt(action: "refund" | "close" = "refund"): UnappliedAttempt { return { id: payment.id, orderNumber: payment.orderNumber, action, key: "sf-original-key", note: action === "close" ? "Checked with provider" : null }; }
function orderWith(item: AdminUnappliedPayment) { return { ...orderDetail, number: item.orderNumber, unappliedPayments: [item] }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  sessionStorage.clear(); await i18n.changeLanguage("en");
  vi.spyOn(api, "unappliedPayments").mockImplementation(async (_store, query) => ({ ...page, page: query.page }));
  vi.spyOn(api, "changeUnappliedPayment").mockResolvedValue({ ...payment, status: "Refunded" });
  vi.spyOn(api, "order").mockResolvedValue(orderWith(payment));
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); sessionStorage.clear(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });
describe("unapplied payments", () => {
  it("uses server paging, URL status, browser Back and correct currency/order links", async () => {
    const user = userEvent.setup(); vi.mocked(api.unappliedPayments).mockImplementation(async (_store, query) => ({ ...page, page: query.page, totalCount: 26, hasMore: query.page === 1 }));
    route(); const link = await screen.findByRole("link", { name: `Order ${payment.orderNumber}` });
    expect(link.getAttribute("href")).toBe(`/stores/store-a/orders/${encodeURIComponent(payment.orderNumber)}#order-unapplied-${payment.id}`);
    expect(screen.getByText("US$175.00")).toBeTruthy(); expect(api.order).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Next page" }));
    await waitFor(() => expect(api.unappliedPayments).toHaveBeenLastCalledWith("store-a", { page: 2, status: "Open" }, expect.any(AbortSignal)));
    await waitFor(() => expect(document.activeElement?.textContent).toBe("Showing 26–26 of 26"));
    await user.selectOptions(screen.getByRole("combobox", { name: "Processing status" }), "");
    await waitFor(() => expect(api.unappliedPayments).toHaveBeenLastCalledWith("store-a", { page: 1, status: "" }, expect.any(AbortSignal)));
    expect(screen.getByTestId("location").textContent).toBe("/stores/store-a/unapplied-payments?status=");
    await user.click(screen.getByRole("button", { name: "History back" }));
    await waitFor(() => expect(api.unappliedPayments).toHaveBeenLastCalledWith("store-a", { page: 2, status: "Open" }, expect.any(AbortSignal)));
  });
  it.each(["Refunding", "Refunded", "ClosedByHand"])("loads direct status %s", async (status) => {
    route(`/stores/store-a/unapplied-payments?status=${status}`); await screen.findByRole("link", { name: `Order ${payment.orderNumber}` });
    expect(api.unappliedPayments).toHaveBeenCalledWith("store-a", { page: 1, status }, expect.any(AbortSignal)); expect(api.order).not.toHaveBeenCalled();
  });
  it.each([`page=${maxUnappliedPage + 1}`, "page=-1", "page=1.5", "page=0", "status=3"])("recovers invalid query %s", async (query) => {
    route(`/stores/store-a/unapplied-payments?${query}`); await screen.findByRole("link", { name: "Reset list options" });
    expect(api.unappliedPayments).toHaveBeenCalledWith("store-a", { page: 1, status: "Open" }, expect.any(AbortSignal));
  });
  it("distinguishes an empty filter from an out-of-range page", async () => {
    const user = userEvent.setup(); vi.mocked(api.unappliedPayments).mockImplementation(async (_store, query) => ({ ...page, items: [], page: query.page }));
    route("/stores/store-a/unapplied-payments?page=4&status=Refunding"); await screen.findByRole("heading", { name: "No payments on this page" });
    await user.click(screen.getByRole("button", { name: "Go to first page" })); await screen.findByRole("heading", { name: "No payments in this view" });
    expect(api.unappliedPayments).toHaveBeenLastCalledWith("store-a", { page: 1, status: "Refunding" }, expect.any(AbortSignal));
  });
  it("retains failed refresh rows and blocks actions until read recovery", async () => {
    const user = userEvent.setup(); route(); await screen.findByText("US$175.00"); vi.mocked(api.unappliedPayments).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Refresh" })); await screen.findByRole("button", { name: "Try again" });
    expect(screen.getByText("US$175.00")).toBeTruthy(); expect(screen.getByRole("button", { name: "Review provider refund" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Review provider refund" })).toHaveProperty("disabled", false));
    expect(api.changeUnappliedPayment).not.toHaveBeenCalled();
  });
  it("requires confirmation, restores Escape focus and sends one persisted keyed refund", async () => {
    const waiting = deferred<AdminUnappliedPayment>(); vi.mocked(api.changeUnappliedPayment).mockReturnValue(waiting.promise);
    route(); const user = await open("refund"); expect(document.activeElement).toBe(screen.getByRole("heading", { name: `Refund payment for ${payment.orderNumber}?` }));
    await user.keyboard("{Escape}"); expect(screen.queryByRole("group")).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Review provider refund" })));
    await open("refund", user); await user.dblClick(screen.getByRole("button", { name: "Confirm provider refund" }));
    expect(api.changeUnappliedPayment).toHaveBeenCalledTimes(1); const saved = JSON.parse(sessionStorage.getItem(storageKey)!);
    expect(api.changeUnappliedPayment).toHaveBeenCalledWith("store-a", payment.id, "refund", saved.key, undefined); expect(saved.key).toMatch(/^sf-/);
    expect(screen.getByRole("combobox")).toHaveProperty("disabled", true);
    await act(async () => waiting.resolve({ ...payment, status: "Refunded" })); await screen.findByText(/The provider refund request was accepted/); expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(document.activeElement?.textContent).toContain("The provider refund request was accepted");
  });
  it.each(["This payment cannot be refunded from here", "The refund was refused", "This payment has already been settled", "This payment changed while it was being closed"])("honors conflict %s", async (title) => {
    vi.mocked(api.changeUnappliedPayment).mockRejectedValue(new HttpError("http", 409, { title })); route(); const user = await open("refund");
    await user.click(screen.getByRole("button", { name: "Confirm provider refund" })); await waitFor(() => expect(api.unappliedPayments).toHaveBeenCalledTimes(2));
    expect(api.changeUnappliedPayment).toHaveBeenCalledTimes(1); expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(screen.getByRole("button", { name: "Confirm provider refund" })).toHaveProperty("disabled", true); expect(screen.getByRole("button", { name: "Review current record" })).toBeTruthy();
  });
  it("blocks uncertain 502→Refunding resends, then permits manual review", async () => {
    vi.mocked(api.changeUnappliedPayment).mockRejectedValue(new HttpError("http", 502, { title: "The refund was not confirmed" }));
    const unresolved = { ...payment, status: "Refunding", canRefund: false }; vi.mocked(api.order).mockResolvedValue(orderWith(unresolved));
    route(); const user = await open("refund"); vi.mocked(api.unappliedPayments).mockResolvedValue({ ...page, items: [unresolved] });
    await user.click(screen.getByRole("button", { name: "Confirm provider refund" })); await screen.findByRole("button", { name: "Finish review and refresh list" });
    expect(screen.queryByRole("button", { name: "Review same request" })).toBeNull(); expect(screen.queryByRole("button", { name: "Confirm provider refund" })).toBeNull(); expect(api.changeUnappliedPayment).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Finish review and refresh list" })); await waitFor(() => expect(screen.getByRole("button", { name: "Review manual closure" })).toHaveProperty("disabled", false)); expect(sessionStorage.getItem(storageKey)).toBeNull();
  });
  it("recovers a lost answer with the same key after remount without automatic writes", async () => {
    const attempt = savedAttempt(); sessionStorage.setItem(storageKey, JSON.stringify(attempt)); const first = route("/stores/store-a/unapplied-payments?status=ClosedByHand");
    await screen.findByRole("button", { name: "Review same request" }); expect(api.changeUnappliedPayment).not.toHaveBeenCalled(); first.unmount();
    route(); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: "Review same request" })); await user.click(screen.getByRole("button", { name: "Confirm provider refund" }));
    await waitFor(() => expect(api.changeUnappliedPayment).toHaveBeenCalledExactlyOnceWith("store-a", payment.id, "refund", attempt.key, undefined));
  });
  it("checks order recovery when a settled record leaves its filter", async () => {
    sessionStorage.setItem(storageKey, JSON.stringify(savedAttempt())); vi.mocked(api.unappliedPayments).mockResolvedValue({ ...page, items: [], totalCount: 0 });
    vi.mocked(api.order).mockResolvedValue(orderWith({ ...payment, status: "Refunded", canRefund: false })); route(); await screen.findByRole("button", { name: "Finish review and refresh list" });
    expect(api.order).toHaveBeenCalledWith("store-a", payment.orderNumber, expect.any(AbortSignal)); expect(screen.queryByRole("button", { name: "Review same request" })).toBeNull(); expect(api.changeUnappliedPayment).not.toHaveBeenCalled();
  });
  it("keeps an in-flight key and blocks recovery after failed authoritative read", async () => {
    vi.mocked(api.changeUnappliedPayment).mockRejectedValue(new HttpError("http", 409, { title: "A request with this key is still in progress" })); vi.mocked(api.order).mockRejectedValueOnce(new HttpError("http", 503));
    route(); const user = await open("refund"); await user.click(screen.getByRole("button", { name: "Confirm provider refund" })); await screen.findByRole("button", { name: "Try again" });
    const key = JSON.parse(sessionStorage.getItem(storageKey)!).key; expect(screen.queryByRole("button", { name: "Review same request" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByRole("button", { name: "Review same request" });
    expect(JSON.parse(sessionStorage.getItem(storageKey)!).key).toBe(key); expect(api.changeUnappliedPayment).toHaveBeenCalledTimes(1);
  });
  it("requires trimmed note and verification, retaining drafts after server validation", async () => {
    route(); const user = await open("close"); const confirm = screen.getByRole("button", { name: "Confirm manual closure" }); await user.click(confirm);
    expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "How was this payment resolved?" })); expect(api.changeUnappliedPayment).not.toHaveBeenCalled();
    await user.type(screen.getByRole("textbox"), "  Checked provider portal  "); await user.click(confirm); expect(document.activeElement).toBe(screen.getByRole("checkbox")); expect(api.changeUnappliedPayment).not.toHaveBeenCalled();
    await user.click(screen.getByRole("checkbox")); vi.mocked(api.changeUnappliedPayment).mockRejectedValueOnce(new HttpError("http", 400, { errors: { note: ["Invalid"] } }));
    await user.click(confirm); await screen.findByRole("button", { name: "Review current record" }); expect(screen.getByRole("textbox")).toHaveProperty("value", "  Checked provider portal  ");
    expect(api.changeUnappliedPayment).toHaveBeenCalledWith("store-a", payment.id, "close", expect.any(String), "Checked provider portal"); expect(sessionStorage.getItem(storageKey)).toBeNull();
    await user.click(screen.getByRole("button", { name: "Review current record" })); expect(screen.getByRole("checkbox")).toHaveProperty("checked", false);
  });
  it("recovers exact close note and key and describes manual closure accurately", async () => {
    const attempt = savedAttempt("close"); sessionStorage.setItem(storageKey, JSON.stringify(attempt)); vi.mocked(api.order).mockResolvedValue(orderWith({ ...payment, status: "Refunding", canRefund: false }));
    vi.mocked(api.changeUnappliedPayment).mockResolvedValue({ ...payment, status: "ClosedByHand", canRefund: false, note: attempt.note });
    route(); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: "Review same request" }));
    expect(screen.getByRole("textbox")).toHaveProperty("readOnly", true); expect(screen.getByRole("textbox")).toHaveProperty("value", attempt.note);
    await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm manual closure" })); await screen.findByText(/Manual closure was recorded. This is not provider confirmation/);
    expect(api.changeUnappliedPayment).toHaveBeenCalledExactlyOnceWith("store-a", payment.id, "close", attempt.key, attempt.note);
  });
  it("blocks further actions after accepted-write→failed-read and retries only reads", async () => {
    route(); const user = await open("refund"); vi.mocked(api.unappliedPayments).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Confirm provider refund" })); await screen.findByRole("button", { name: "Try again" }); expect(screen.getByRole("button", { name: "Review provider refund" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Review provider refund" })).toHaveProperty("disabled", false)); expect(api.changeUnappliedPayment).toHaveBeenCalledTimes(1);
  });
  it("never refunds missing references, Refunding or unknown states despite a capability flag", async () => {
    vi.mocked(api.unappliedPayments).mockResolvedValue({ ...page, totalCount: 3, items: [{ ...payment, reference: null }, { ...payment, id: "b", status: "Refunding" }, { ...payment, id: "c", status: "Future", reason: "FutureReason" }] });
    route("/stores/store-a/unapplied-payments?status="); await screen.findByText("Unknown processing status (Future)"); expect(screen.queryByRole("button", { name: "Review provider refund" })).toBeNull();
    expect(screen.getAllByRole("button", { name: "Review manual closure" })).toHaveLength(2); expect(screen.getByText("Unknown reason (FutureReason)")).toBeTruthy();
  });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("allows reads and authorized writes for %s", async (role) => {
    route(undefined, role); await screen.findByText("US$175.00"); expect(screen.queryByRole("button", { name: "Review provider refund" }) !== null).toBe(["Owner", "Admin"].includes(role));
  });
  it("ignores departed-store write effects and retains its recovery key", async () => {
    const waiting = deferred<AdminUnappliedPayment>(); vi.mocked(api.changeUnappliedPayment).mockReturnValue(waiting.promise); route(); const user = await open("refund");
    await user.click(screen.getByRole("button", { name: "Confirm provider refund" })); await user.click(screen.getByRole("link", { name: "Switch store" })); await waitFor(() => expect(api.unappliedPayments).toHaveBeenLastCalledWith("store-b", { page: 1, status: "Open" }, expect.any(AbortSignal)));
    await act(async () => waiting.resolve({ ...payment, status: "Refunded" })); expect(api.unappliedPayments).toHaveBeenCalledTimes(2); expect(screen.queryByText(/The provider refund request was accepted/)).toBeNull(); expect(sessionStorage.getItem(storageKey)).not.toBeNull();
  });
  it("blocks financial actions when storage is malformed or cannot save", async () => {
    sessionStorage.setItem(storageKey, "bad-json"); const first = route(); await screen.findByText(/The payment request could not be saved safely/); await screen.findByText("US$175.00");
    expect(screen.getByRole("button", { name: "Review provider refund" })).toHaveProperty("disabled", true); first.unmount(); sessionStorage.clear(); route(); const user = await open("refund");
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage denied"); }); await user.click(screen.getByRole("button", { name: "Confirm provider refund" })); await screen.findByText(/The payment request could not be saved safely/); expect(api.changeUnappliedPayment).not.toHaveBeenCalled();
  });
  it("keeps the same key after an actual network failure and sends it only on explicit recovery", async () => {
    vi.mocked(api.changeUnappliedPayment).mockRejectedValueOnce(new HttpError("network", null));
    route(); const user = await open("refund"); await user.click(screen.getByRole("button", { name: "Confirm provider refund" }));
    await screen.findByRole("button", { name: "Review same request" }); const key = JSON.parse(sessionStorage.getItem(storageKey)!).key;
    expect(api.changeUnappliedPayment).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Review same request" })); await user.click(screen.getByRole("button", { name: "Confirm provider refund" }));
    await waitFor(() => expect(api.changeUnappliedPayment).toHaveBeenNthCalledWith(2, "store-a", payment.id, "refund", key, undefined));
  });
  it("ignores late list responses after a status change", async () => {
    const old = deferred<typeof page>(); vi.mocked(api.unappliedPayments).mockReturnValueOnce(old.promise);
    route(); const user = userEvent.setup(); await user.selectOptions(screen.getByRole("combobox"), "Refunding");
    await screen.findByText("US$175.00"); await act(async () => old.resolve({ ...page, items: [{ ...payment, reference: "stale reference" }] }));
    expect(screen.queryByText("stale reference")).toBeNull(); expect(screen.getByRole("combobox")).toHaveProperty("value", "Refunding");
  });
  it("requires a new review after a confirmation's read snapshot changes", async () => {
    route(); const user = await open("refund"); await user.click(screen.getByRole("button", { name: "Refresh" }));
    await screen.findByRole("button", { name: "Review current record" });
    await user.click(screen.getByRole("button", { name: "Confirm provider refund" })); expect(api.changeUnappliedPayment).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Review current record" }));
    await user.click(screen.getByRole("button", { name: "Confirm provider refund" })); expect(api.changeUnappliedPayment).toHaveBeenCalledTimes(1);
  });
  it("does not let an absent recovery record enable any new write", async () => {
    sessionStorage.setItem(storageKey, JSON.stringify(savedAttempt())); vi.mocked(api.order).mockResolvedValue({ ...orderDetail, unappliedPayments: [] });
    route(); await screen.findByRole("button", { name: "Try again" });
    expect(screen.queryByRole("button", { name: "Review same request" })).toBeNull();
    expect(screen.getByRole("button", { name: "Review provider refund" })).toHaveProperty("disabled", true); expect(api.changeUnappliedPayment).not.toHaveBeenCalled();
  });
  it("refreshes and locks the current query when browser navigation changes it during a write", async () => {
    const waiting = deferred<AdminUnappliedPayment>(); vi.mocked(api.changeUnappliedPayment).mockReturnValue(waiting.promise);
    route(); const user = await open("refund"); await user.click(screen.getByRole("button", { name: "Confirm provider refund" }));
    await user.click(screen.getByRole("link", { name: "Other view" }));
    await waitFor(() => expect(api.unappliedPayments).toHaveBeenLastCalledWith("store-a", { page: 1, status: "" }, expect.any(AbortSignal)));
    const freshRead = deferred<typeof page>(); vi.mocked(api.unappliedPayments).mockReturnValueOnce(freshRead.promise);
    await act(async () => waiting.resolve({ ...payment, status: "Refunded" }));
    await waitFor(() => expect(api.unappliedPayments).toHaveBeenCalledTimes(3));
    expect(screen.getByRole("button", { name: "Review provider refund" })).toHaveProperty("disabled", true);
    await act(async () => freshRead.resolve(page));
    await waitFor(() => expect(screen.getByRole("button", { name: "Review provider refund" })).toHaveProperty("disabled", false));
  });
  it("registers the actual direct route with Czech navigation and controls", async () => {
    await i18n.changeLanguage("cs"); vi.spyOn(api, "me").mockResolvedValue({ id: "owner", email: "owner@example.test", role: "Owner", tenantId: "tenant" }); vi.spyOn(api, "stores").mockResolvedValue([store]);
    window.history.replaceState(null, "", "/stores/store-a/unapplied-payments?status=Refunding"); render(<App />); await screen.findByRole("heading", { name: "Nepřiřazené platby", level: 1 }); await screen.findByText("Poskytovatel");
    expect(screen.getByRole("combobox", { name: "Stav zpracování" })).toHaveProperty("value", "Refunding"); expect(screen.getAllByRole("link", { name: "Nepřiřazené platby" }).length).toBeGreaterThan(0);
  });
});
