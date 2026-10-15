// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminStore, type OrderReturn } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { StoreLayout } from "../layouts/StoreLayout";
import { SessionContext } from "../session";
import { returnOrder, returnRecord } from "../test/returnFixtures";
import { makeRefundAttempt, refundStorageKey } from "../utils/refundWorkflow";
import { StoreOrderPage } from "./OrderPages";
import { StoreReturnPage } from "./ReturnPages";
const store: AdminStore = { id: "store-a", name: "Store A", currency: "CZK", culture: "cs-CZ", status: "published", primaryHostName: null, logoUrl: null, company: null, returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 8 } };
const key = refundStorageKey("owner", "store-a");
let records: OrderReturn[]; let orderStatus: string;
function currentOrder() { return { ...returnOrder(records), status: orderStatus }; }
function saved() { return JSON.parse(sessionStorage.getItem(key)!); }
function route(target: "return" | "order" = "return", role = "Owner") {
  const path = target === "return" ? `returns/${returnRecord.id}?order=${returnRecord.orderNumber}` : `orders/${returnRecord.orderNumber}`;
  return render(<SessionContext value={{ user: { id: "owner", email: "owner@example.test", role, tenantId: "tenant" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[`/stores/store-a/${path}`]}><Link to={`/stores/store-b/${path}`}>Switch store</Link><Routes>
      <Route element={<Outlet context={{ stores: { status: "ready", data: [store, { ...store, id: "store-b" }], refreshing: false, refreshError: null }, reloadStores: () => undefined }} />}>
        <Route path="/stores/:storeId" element={<StoreLayout />}><Route path="returns/:returnId" element={<StoreReturnPage />} /><Route path="orders/:number" element={<StoreOrderPage />} /></Route>
      </Route></Routes></MemoryRouter></SessionContext>);
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>((yes) => { resolve = yes; }); return { promise, resolve }; }
async function open(action: "receive" | "retry" | "close" | "order-refund", user = userEvent.setup()) {
  const labels = { receive: "Record received goods", retry: "Retry refused refund", close: "Record manual refund resolution", "order-refund": "Refund remaining order" };
  await user.click(await screen.findByRole("button", { name: labels[action] })); return user;
}
async function receive(user = userEvent.setup()) {
  await open("receive", user); await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm receipt" })); return user;
}
function received(refund = "Failed") { records = [{ ...returnRecord, status: "Received", refundedAmount: 50, refund, refundProblem: refund === "Failed" ? "Provider refused" : null }]; }
beforeAll(initializeI18n);
beforeEach(async () => {
  sessionStorage.clear(); records = [{ ...returnRecord, status: "Accepted" }]; orderStatus = "Paid"; await i18n.changeLanguage("en");
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
  vi.spyOn(api, "returns").mockImplementation(async () => records);
  vi.spyOn(api, "order").mockImplementation(async () => currentOrder());
  vi.spyOn(api, "changeReturnMoney").mockImplementation(async (_store, id, action, _key, note) => {
    const row = { ...records.find((item) => item.id === id)!, status: "Received", refundedAmount: 50, refund: action === "close" ? "ClosedByHand" : "Refunded", refundNote: note ?? null };
    records = records.map((item) => item.id === id ? row : item); return row;
  });
  vi.spyOn(api, "refundOrder").mockImplementation(async () => {
    records = [...records, { ...returnRecord, id: "new-return", number: "RET-NEW", status: "Received", refund: "Failed", refundedAmount: 99, refundProblem: "Provider refused" }]; orderStatus = "Refunded"; return currentOrder();
  });
});
afterEach(async () => { cleanup(); sessionStorage.clear(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });

describe("return financial actions", () => {
  it("requires goods verification, restores Escape focus, shares the decision lock and submits one keyed receipt", async () => {
    const waiting = deferred<OrderReturn>(); vi.mocked(api.changeReturnMoney).mockReturnValueOnce(waiting.promise); route(); const user = await open("receive");
    await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Record received goods" })));
    await open("receive", user); await user.click(screen.getByRole("button", { name: "Confirm receipt" })); expect(document.activeElement).toBe(screen.getByRole("checkbox")); expect(api.changeReturnMoney).not.toHaveBeenCalled();
    await user.click(screen.getByRole("checkbox")); await user.dblClick(screen.getByRole("button", { name: "Confirm receipt" }));
    expect(api.changeReturnMoney).toHaveBeenCalledExactlyOnceWith("store-a", returnRecord.id, "receive", saved().key, undefined); expect(screen.getByRole("button", { name: "Refuse" })).toHaveProperty("disabled", true);
    received(); await act(async () => waiting.resolve(records[0])); await screen.findByText(/Receipt and credit were recorded/); expect(document.activeElement?.textContent).toContain("Receipt and credit were recorded");
    expect(screen.queryByRole("button", { name: "Record received goods" })).toBeNull(); expect(screen.getByRole("button", { name: "Retry refused refund" })).toBeTruthy(); expect(sessionStorage.getItem(key)).toBeNull();
  });
  it.each(["Pending", "ByStore", "Failed"])("a successful receipt with %s money state does not claim payment was completed", async (refund) => {
    vi.mocked(api.changeReturnMoney).mockImplementationOnce(async () => { received(refund); return records[0]; }); route(); await receive(); await screen.findByText(/Receipt and credit were recorded/);
    const result = document.querySelector('.refund-outcome')!; expect(within(result as HTMLElement).getByText(/payment outcome is shown separately/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Record received goods" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Retry refused refund" }) !== null).toBe(refund === "Failed");
    expect(screen.queryByRole("button", { name: "Record manual refund resolution" }) !== null).toBe(refund !== "ByStore");
  });
  it("retries only the refused payment, using the saved currency and a fresh key", async () => {
    received(); route(); const user = await open("retry"); await user.click(screen.getByRole("button", { name: "Confirm refund retry" }));
    await screen.findByText(/The refund retry was acknowledged/); expect(api.changeReturnMoney).toHaveBeenCalledExactlyOnceWith("store-a", returnRecord.id, "retry", expect.stringMatching(/^sf-/), undefined);
    expect(screen.queryByRole("button", { name: "Retry refused refund" })).toBeNull(); expect(screen.getAllByText(/€50.00/).length).toBeGreaterThan(0);
  });
  it("keeps a provider refusal authoritative, retains the intent and requires renewed review", async () => {
    received(); vi.mocked(api.changeReturnMoney).mockRejectedValueOnce(new HttpError("http", 409, { title: "The refund was refused" })); route(); const user = await open("retry");
    await user.click(screen.getByRole("button", { name: "Confirm refund retry" })); await screen.findByRole("button", { name: "Review current record" });
    expect(screen.getByRole("button", { name: "Confirm refund retry" })).toHaveProperty("disabled", true); expect(sessionStorage.getItem(key)).toBeNull(); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
  });
  it("never resends a 502→Pending refund, then allows verified manual resolution", async () => {
    received(); vi.mocked(api.changeReturnMoney).mockImplementationOnce(async () => { received("Pending"); throw new HttpError("http", 502, { title: "The refund was not confirmed" }); });
    route(); const user = await open("retry"); await user.click(screen.getByRole("button", { name: "Confirm refund retry" })); await screen.findByRole("button", { name: "Finish review and refresh" });
    expect(screen.queryByRole("button", { name: "Review same financial request" })).toBeNull(); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Finish review and refresh" })); await open("close", user);
    await user.click(screen.getByRole("button", { name: "Confirm manual resolution" })); expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "How was the money returned?" }));
    await user.type(screen.getByRole("textbox"), "  Checked provider; paid externally.  "); await user.click(screen.getByRole("button", { name: "Confirm manual resolution" })); expect(document.activeElement).toBe(screen.getByRole("checkbox"));
    await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm manual resolution" })); await screen.findByText(/Manual resolution was recorded. This action did not send money/);
    expect(api.changeReturnMoney).toHaveBeenLastCalledWith("store-a", returnRecord.id, "close", expect.any(String), "Checked provider; paid externally.");
  });
  it("does not receive stock or credit twice after a lost committed receipt answer", async () => {
    vi.mocked(api.changeReturnMoney).mockImplementationOnce(async () => { received("Pending"); throw new HttpError("network", null); }); route(); await receive();
    await screen.findByRole("button", { name: "Finish review and refresh" }); expect(orderStatus).toBe("Paid"); expect(screen.queryByRole("button", { name: "Review same financial request" })).toBeNull(); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
  });
  it("recovers an unchanged receipt explicitly with the exact key after remount", async () => {
    vi.mocked(api.changeReturnMoney).mockRejectedValueOnce(new HttpError("network", null)); const first = route(); const user = await receive(); await screen.findByRole("button", { name: "Review same financial request" }); const original = saved();
    first.unmount(); route(); await user.click(await screen.findByRole("button", { name: "Review same financial request" })); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm receipt" })); await screen.findByText(/Receipt and credit were recorded/);
    expect(api.changeReturnMoney).toHaveBeenNthCalledWith(2, "store-a", returnRecord.id, "receive", original.key, undefined);
  });
  it("recovers manual closure with an unchanged note and key, and requires verification again", async () => {
    received("Pending"); vi.mocked(api.changeReturnMoney).mockRejectedValueOnce(new HttpError("network", null)); const first = route(); const user = await open("close");
    await user.type(screen.getByRole("textbox"), "  Exact saved\nnote  "); await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm manual resolution" }));
    await screen.findByRole("button", { name: "Review same financial request" }); const original = saved(); first.unmount(); route();
    await user.click(await screen.findByRole("button", { name: "Review same financial request" })); expect(screen.getByRole("textbox")).toHaveProperty("readOnly", true); expect(screen.getByRole("textbox")).toHaveProperty("value", original.note);
    await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm manual resolution" })); await screen.findByText(/Manual resolution was recorded/);
    expect(api.changeReturnMoney).toHaveBeenNthCalledWith(2, "store-a", returnRecord.id, "close", original.key, original.note);
  });
  it("retains an editable close-note draft after definite validation failure", async () => {
    received(); vi.mocked(api.changeReturnMoney).mockRejectedValueOnce(new HttpError("http", 400, { errors: { note: ["Invalid"] } })); route(); const user = await open("close");
    await user.type(screen.getByRole("textbox"), "Checked provider"); await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm manual resolution" }));
    await screen.findByRole("button", { name: "Review current record" }); expect(screen.getByRole("textbox")).toHaveProperty("value", "Checked provider"); expect(sessionStorage.getItem(key)).toBeNull();
    await user.click(screen.getByRole("button", { name: "Review current record" })); expect(screen.getByRole("checkbox")).toHaveProperty("checked", false);
  });
  it("does not replace or resend a mismatched key after 422, including remount", async () => {
    received(); vi.mocked(api.changeReturnMoney).mockRejectedValueOnce(new HttpError("http", 422, { title: "This idempotency key was used for a different request" }));
    const first = route(); const user = await open("retry"); await user.click(screen.getByRole("button", { name: "Confirm refund retry" })); await screen.findByRole("button", { name: "Finish review and refresh" });
    expect(saved().reviewOnly).toBe(true); first.unmount(); route(); await screen.findByRole("button", { name: "Finish review and refresh" });
    expect(screen.queryByRole("button", { name: "Review same financial request" })).toBeNull(); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
  });
  it("keeps an in-flight key and blocks recovery when the authoritative read fails", async () => {
    received(); vi.mocked(api.changeReturnMoney).mockRejectedValueOnce(new HttpError("http", 409, { title: "A request with this key is still in progress" })); route(); const user = await open("retry");
    vi.mocked(api.order).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(screen.getByRole("button", { name: "Confirm refund retry" }));
    await screen.findByRole("button", { name: "Try again" }); expect(screen.queryByRole("button", { name: "Review same financial request" })).toBeNull(); expect(sessionStorage.getItem(key)).not.toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByRole("button", { name: "Review same financial request" }); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
  });
  it("honors an already-received 409 race without another receipt", async () => {
    vi.mocked(api.changeReturnMoney).mockImplementationOnce(async () => { received("Failed"); throw new HttpError("http", 409, { title: "Only an accepted return of a paid order can be received" }); }); route(); await receive();
    await screen.findByText(/This return or order no longer permits receipt/); expect(screen.getByRole("button", { name: "Confirm receipt" })).toHaveProperty("disabled", true); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
  });
  it("keeps acknowledged receipt facts but blocks further financial actions after failed refresh", async () => {
    route(); const user = await open("receive"); vi.mocked(api.returns).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm receipt" }));
    await screen.findByRole("button", { name: "Try again" }); expect(screen.getByRole("button", { name: "Record received goods" })).toHaveProperty("disabled", true); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByText(/No receipt, retry or manual resolution action is available/); expect(api.changeReturnMoney).toHaveBeenCalledTimes(1);
  });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("gates receipt/refund writes for %s", async (role) => {
    route("return", role); await screen.findByRole("heading", { name: "Goods and money" }); expect(screen.queryByRole("button", { name: "Record received goods" }) !== null).toBe(["Owner", "Admin"].includes(role));
  });
  it("does not overwrite the new store when the original financial answer arrives late", async () => {
    const old = deferred<OrderReturn>(); vi.mocked(api.changeReturnMoney).mockReturnValueOnce(old.promise); route(); const user = await receive();
    await user.click(screen.getByRole("link", { name: "Switch store" })); await waitFor(() => expect(api.order).toHaveBeenLastCalledWith("store-b", returnRecord.orderNumber, expect.any(AbortSignal)));
    await act(async () => old.resolve({ ...returnRecord, status: "Received", refund: "Refunded", refundedAmount: 50 })); expect(screen.queryByText(/Receipt and credit were recorded/)).toBeNull(); expect(sessionStorage.getItem(key)).not.toBeNull();
  });
  it("blocks writes when saved recovery metadata or session storage cannot be used", async () => {
    sessionStorage.setItem(key, "broken"); const first = route(); await screen.findByText(/This browser tab could not safely save or read/); await screen.findByRole("button", { name: "Record received goods" });
    expect(screen.getByRole("button", { name: "Record received goods" })).toHaveProperty("disabled", true); first.unmount(); sessionStorage.clear(); route();
    const user = await open("receive"); await user.click(screen.getByRole("checkbox")); vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Denied"); });
    await user.click(screen.getByRole("button", { name: "Confirm receipt" })); await screen.findByText(/This browser tab could not safely save or read/); expect(api.changeReturnMoney).not.toHaveBeenCalled();
  });
});

describe("remaining-order refund orchestration", () => {
  it("records a keyed remaining-order refund, inspects its returned record and does not claim cash was paid", async () => {
    records = []; route("order"); const user = await open("order-refund"); await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm order refund" }));
    await screen.findByText(/The remaining return and credit were recorded/); expect(api.refundOrder).toHaveBeenCalledExactlyOnceWith("store-a", returnRecord.orderNumber, expect.stringMatching(/^sf-/));
    expect(within(document.querySelector(".refund-outcome") as HTMLElement).getByRole("link", { name: "RET-NEW" })).toBeTruthy(); expect(screen.getAllByText(/Provider refused/).length).toBeGreaterThan(0); expect(sessionStorage.getItem(key)).toBeNull();
  });
  it("cannot replay a lost order refund after a new partial credit while the order stays Paid", async () => {
    vi.mocked(api.refundOrder).mockImplementationOnce(async () => { received("Pending"); records.push({ ...records[0], id: "new-credit", number: "RET-NEW" }); throw new HttpError("network", null); });
    route("order"); const user = await open("order-refund"); await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm order refund" }));
    await screen.findByRole("button", { name: "Finish review and refresh" }); expect(orderStatus).toBe("Paid"); expect(saved().previousReturnIds).toEqual([returnRecord.id]);
    expect(screen.queryByRole("button", { name: "Review same financial request" })).toBeNull(); expect(api.refundOrder).toHaveBeenCalledTimes(1);
  });
  it("explicitly recovers an unchanged order request with its original key and return baseline", async () => {
    const attempt = makeRefundAttempt("order-refund", currentOrder()); sessionStorage.setItem(key, JSON.stringify(attempt)); route("order"); const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Review same financial request" })); expect(api.refundOrder).not.toHaveBeenCalled(); await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm order refund" }));
    await screen.findByText(/The remaining return and credit were recorded/); expect(api.refundOrder).toHaveBeenCalledExactlyOnceWith("store-a", returnRecord.orderNumber, attempt.key);
  });
  it("does not acknowledge an order response without a new received return", async () => {
    vi.mocked(api.refundOrder).mockImplementationOnce(async () => currentOrder()); route("order"); const user = await open("order-refund"); await user.click(screen.getByRole("checkbox")); await user.click(screen.getByRole("button", { name: "Confirm order refund" }));
    await screen.findByRole("button", { name: "Review same financial request" }); expect(screen.queryByText(/The remaining return and credit were recorded/)).toBeNull(); expect(sessionStorage.getItem(key)).not.toBeNull();
  });
  it("cannot recover from a missing/mismatched return even when the order itself is readable", async () => {
    const attempt = makeRefundAttempt("receive", currentOrder(), records[0]); sessionStorage.setItem(key, JSON.stringify(attempt)); vi.mocked(api.order).mockImplementation(async () => ({ ...currentOrder(), returns: [] }));
    route("order"); await screen.findByRole("button", { name: "Try again" }); expect(screen.queryByRole("button", { name: "Review same financial request" })).toBeNull(); expect(api.changeReturnMoney).not.toHaveBeenCalled();
  });
});
