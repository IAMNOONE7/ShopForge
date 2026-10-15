// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore, type OrderReturn } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { StoreLayout } from "../layouts/StoreLayout";
import { SessionContext } from "../session";
import { returnOrder, returnRecord } from "../test/returnFixtures";
import { StoreReturnPage, StoreReturnsPage } from "./ReturnPages";

const store: AdminStore = { id: "store-a", name: "Store A", currency: "CZK", culture: "cs-CZ", status: "published", primaryHostName: null, logoUrl: null, company: null, returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 8 } };
const detailPath = `/stores/store-a/returns/${returnRecord.id}?order=${returnRecord.orderNumber}`;
let records: OrderReturn[];
function History() { const location = useLocation(); const navigate = useNavigate(); return <><output data-testid="location">{location.pathname}{location.search}</output><button onClick={() => navigate(-1)}>History back</button><Link to={`/stores/store-b/returns/${returnRecord.id}`}>Switch store</Link></>; }
function route(path = "/stores/store-a/returns", role = "Owner") {
  return render(<SessionContext value={{ user: { id: "owner", email: "owner@example.test", role, tenantId: "tenant" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[path]}><History /><Routes><Route element={<Outlet context={{ stores: { status: "ready", data: [store, { ...store, id: "store-b" }], refreshing: false, refreshError: null }, reloadStores: () => undefined }} />}>
      <Route path="/stores/:storeId" element={<StoreLayout />}><Route path="returns" element={<StoreReturnsPage />} /><Route path="returns/:returnId" element={<StoreReturnPage />} /></Route>
    </Route></Routes></MemoryRouter></SessionContext>);
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>((yes) => { resolve = yes; }); return { promise, resolve }; }
async function open(decision: "accept" | "refuse", user = userEvent.setup()) { await user.click(await screen.findByRole("button", { name: decision === "accept" ? "Accept" : "Refuse" })); return user; }
beforeAll(initializeI18n);
beforeEach(async () => {
  records = [structuredClone(returnRecord)]; await i18n.changeLanguage("en");
  vi.spyOn(api, "returns").mockImplementation(async (_store, _signal, query) => records.filter((item) => (!query?.status || item.status === query.status) && (!query?.refund || item.refund === query.refund)));
  vi.spyOn(api, "order").mockImplementation(async () => returnOrder(records));
  vi.spyOn(api, "decideReturn").mockImplementation(async (_store, id, decision) => {
    const item = records.find((record) => record.id === id)!; const changed = { ...item, status: decision === "accept" ? "Accepted" : "Refused" };
    records = records.map((record) => record.id === id ? changed : record); return changed;
  });
});
afterEach(async () => { sessionStorage.clear(); cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

describe("focused return collection", () => {
  it("reads only the current bounded collection, separates status labels and keeps currency on details", async () => {
    route(); const link = await screen.findByRole("link", { name: returnRecord.number });
    expect(api.returns).toHaveBeenCalledExactlyOnceWith("store-a", expect.any(AbortSignal), { status: "", refund: "" });
    expect(api.order).not.toHaveBeenCalled(); expect(link.getAttribute("href")).toBe(detailPath);
    expect(screen.getByText("Records shown: 1")).toBeTruthy(); expect(screen.getByRole("combobox", { name: "Return status" })).toBeTruthy(); expect(screen.getByRole("combobox", { name: "Refund status" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Mark as received" })).toBeNull(); expect(screen.queryByText("€0.00")).toBeNull();
    expect(screen.getByRole("link", { name: `Order ${returnRecord.orderNumber}` }).getAttribute("href")).toBe(`/stores/store-a/orders/${returnRecord.orderNumber}#order-return-${returnRecord.id}`);
  });
  it("applies server return/refund filters independently and restores them through Back", async () => {
    records = [{ ...returnRecord, status: "Received", refund: "Failed", refundedAmount: 25, refundProblem: "Refused by provider" }];
    route(); const user = userEvent.setup(); await screen.findByRole("link", { name: returnRecord.number });
    await user.selectOptions(screen.getByRole("combobox", { name: "Return status" }), "Received"); await user.selectOptions(screen.getByRole("combobox", { name: "Refund status" }), "Failed");
    await waitFor(() => expect(api.returns).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { status: "Received", refund: "Failed" }));
    const record = await screen.findByRole("link", { name: returnRecord.number }); expect(record.getAttribute("href")).toContain("status=Received&refund=Failed&order=");
    await user.selectOptions(screen.getByRole("combobox", { name: "Return status" }), "Requested"); await screen.findByRole("heading", { name: "No matching returns" });
    await user.click(screen.getByRole("button", { name: "History back" })); await screen.findByRole("link", { name: returnRecord.number }); expect(screen.getByRole("combobox", { name: "Refund status" })).toHaveProperty("value", "Failed");
  });
  it("describes the latest-200 limit without a fabricated total, page control or complete-history claim", async () => {
    records = Array.from({ length: 200 }, (_, index) => ({ ...returnRecord, id: `r-${index}`, number: `RET-${index}` }));
    route(); await screen.findByText(/The 200-record limit has been reached/); expect(screen.getByText("Records shown: 200")).toBeTruthy();
    expect(screen.getAllByRole("link", { name: /^RET-/ })).toHaveLength(200); expect(api.returns).toHaveBeenCalledTimes(1); expect(api.order).not.toHaveBeenCalled(); expect(screen.queryByRole("button", { name: "Next page" })).toBeNull();
  });
  it.each(["status=9&refund=Failed", "status=Requested&refund=null"])("normalizes only unsupported filters in %s", async (query) => {
    route(`/stores/store-a/returns?${query}`); await screen.findByText(/Some filters were invalid/);
    expect(api.returns).toHaveBeenCalledWith("store-a", expect.any(AbortSignal), query.includes("9") ? { status: "", refund: "Failed" } : { status: "Requested", refund: "" });
  });
  it("uses a read error and explicit read retry instead of showing an empty collection", async () => {
    vi.mocked(api.returns).mockRejectedValueOnce(new HttpError("http", 503)); route(); const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "Try again" })); await screen.findByRole("link", { name: returnRecord.number }); expect(screen.queryByRole("heading", { name: "No returns recorded" })).toBeNull(); expect(api.decideReturn).not.toHaveBeenCalled();
  });
  it("retains same-filter rows after a failed refresh", async () => {
    route(); const user = userEvent.setup(); await screen.findByRole("link", { name: returnRecord.number }); vi.mocked(api.returns).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Refresh" })); await screen.findByRole("button", { name: "Try again" }); expect(screen.getByRole("link", { name: returnRecord.number })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.queryByRole("button", { name: "Try again" })).toBeNull()); expect(api.decideReturn).not.toHaveBeenCalled();
  });
  it("ignores late responses belonging to a previous filter", async () => {
    const old = deferred<OrderReturn[]>(); vi.mocked(api.returns).mockReturnValueOnce(old.promise); route(); const user = userEvent.setup();
    await user.selectOptions(screen.getByRole("combobox", { name: "Refund status" }), "Failed"); await screen.findByRole("heading", { name: "No matching returns" });
    await act(async () => old.resolve([{ ...returnRecord, number: "OLD" }])); expect(screen.queryByRole("link", { name: "OLD" })).toBeNull();
  });
  it("preserves unknown independent states for review", async () => {
    records = [{ ...returnRecord, status: "FutureReturn", refund: "FutureMoney" }]; route(); await screen.findByText(/Unknown \(FutureReturn\)/); expect(screen.getByText(/\(FutureMoney\)/)).toBeTruthy();
  });
});

describe("focused return review", () => {
  it("resolves an ID using the unfiltered collection and saved order, with exact duplicate-name lines and original currency", async () => {
    records = [{ ...returnRecord, status: "Received", refundedAmount: 90, refund: "ClosedByHand", refundNote: "Checked the provider.\nPaid externally." }];
    route(`${detailPath}&status=Requested&refund=Failed`); await screen.findByText("€90.00");
    expect(api.returns).toHaveBeenCalledWith("store-a", expect.any(AbortSignal)); expect(api.order).toHaveBeenCalledWith("store-a", returnRecord.orderNumber, expect.any(AbortSignal));
    expect(screen.getAllByText("Saved product name")).toHaveLength(2); expect(screen.getByText("Quantity: 2")).toBeTruthy(); expect(screen.getByText("Quantity: 1")).toBeTruthy();
    expect(screen.getByText(/The size did not fit/).textContent).toBe(returnRecord.reason); expect(screen.getByText(/Manual refund note: Checked the provider/)).toBeTruthy();
    expect(screen.getByRole("link", { name: "Back to returns" }).getAttribute("href")).toBe("/stores/store-a/returns?status=Requested&refund=Failed");
    expect(screen.queryByRole("button", { name: "Mark as received" })).toBeNull();
  });
  it.each([null, "Pending", "Refunded", "Failed", "ByStore", "ClosedByHand", "FutureMoney"])("keeps received/credited goods separate from refund %s", async (refund) => {
    records = [{ ...returnRecord, status: "Received", refundedAmount: 50, refund, refundProblem: refund === "Failed" ? "Provider refused" : null }];
    route(detailPath); await screen.findByText("€50.00"); expect(screen.getByText(/Receipt of goods and the credit have been recorded/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Accept" })).toBeNull(); expect(screen.queryByRole("button", { name: "Refuse" })).toBeNull(); expect(screen.queryByRole("button", { name: "Request refund" })).toBeNull();
  });
  it("ignores a wrong order hint when the full record provides its real order", async () => {
    route(`/stores/store-a/returns/${returnRecord.id}?order=WRONG`); await screen.findByText("€0.00"); expect(api.order).toHaveBeenCalledWith("store-a", returnRecord.orderNumber, expect.any(AbortSignal));
  });
  it("shows an older summary from its known order without fabricating full lines, notes or decision eligibility", async () => {
    vi.mocked(api.returns).mockResolvedValue([]); vi.mocked(api.order).mockResolvedValue(returnOrder([{ ...returnRecord, refundedAmount: 12, refund: "Failed" }]));
    route(detailPath); await screen.findByText("€12.00"); await screen.findByText(/full record is outside the available collection/);
    expect(screen.queryByRole("heading", { name: "Items in this return" })).toBeNull(); expect(screen.queryByRole("button", { name: "Accept" })).toBeNull();
    expect(api.returns).toHaveBeenCalledTimes(1); expect(api.order).toHaveBeenCalledTimes(1);
  });
  it("honestly describes a missing ID without guessing another endpoint or calling it deleted", async () => {
    route("/stores/store-a/returns/unknown"); await screen.findByRole("heading", { name: "Return outside the available collection" });
    expect(api.order).not.toHaveBeenCalled(); expect(screen.queryByRole("button", { name: "Accept" })).toBeNull();
  });
  it("does not create a nonexistent order anchor when the known order has no such return", async () => {
    vi.mocked(api.returns).mockResolvedValue([]); vi.mocked(api.order).mockResolvedValue(returnOrder([])); route(detailPath);
    await screen.findByRole("heading", { name: "Return outside the available collection" });
    expect(screen.getByRole("link", { name: `Order ${returnRecord.orderNumber}` }).getAttribute("href")).toBe(`/stores/store-a/orders/${returnRecord.orderNumber}`);
  });
  it("blocks review when the full record and order summary do not identify the same return", async () => {
    vi.mocked(api.order).mockResolvedValue({ ...returnOrder(), returns: [{ ...returnOrder().returns[0], number: "OTHER" }] }); route(detailPath);
    await screen.findByRole("button", { name: "Try again" }); expect(screen.queryByRole("button", { name: "Accept" })).toBeNull(); expect(screen.queryByText("€0.00")).toBeNull();
  });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("preserves reads and gates decisions for %s", async (role) => {
    route(detailPath, role); await screen.findByText("€0.00"); expect(screen.queryByRole("button", { name: "Accept" }) !== null).toBe(["Owner", "Admin"].includes(role));
  });
  it("confirms acceptance, restores Escape focus, locks both decisions and submits once", async () => {
    const waiting = deferred<OrderReturn>(); vi.mocked(api.decideReturn).mockReturnValue(waiting.promise); route(detailPath); const user = await open("accept");
    expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Accept RET-1?" })); await user.keyboard("{Escape}");
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Accept" })));
    await open("accept", user); await user.dblClick(screen.getByRole("button", { name: "Confirm acceptance" })); expect(api.decideReturn).toHaveBeenCalledExactlyOnceWith("store-a", returnRecord.id, "accept");
    expect(screen.getByRole("button", { name: "Refuse" })).toHaveProperty("disabled", true);
    records = [{ ...returnRecord, status: "Accepted" }]; await act(async () => waiting.resolve(records[0])); await screen.findByText(/The return was accepted. Receipt of goods/);
    expect(document.activeElement?.textContent).toContain("The return was accepted"); expect(screen.queryByRole("button", { name: "Accept" })).toBeNull();
  });
  it.each(["Requested", "Accepted"])("allows confirmed refusal of %s goods without a made-up reason field", async (status) => {
    records = [{ ...returnRecord, status }]; route(detailPath); const user = await open("refuse"); expect(screen.queryByRole("textbox")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Confirm refusal" })); await screen.findByText(/The return was refused. This decision did not issue/); expect(api.decideReturn).toHaveBeenCalledExactlyOnceWith("store-a", returnRecord.id, "refuse");
  });
  it("retains a refused decision draft after 409 and requires review of a fresh record", async () => {
    vi.mocked(api.decideReturn).mockRejectedValueOnce(new HttpError("http", 409, { title: "This return has already been dealt with" }));
    route(detailPath); const user = await open("refuse"); await user.click(screen.getByRole("button", { name: "Confirm refusal" })); await screen.findByRole("button", { name: "Review current record" });
    expect(screen.getByRole("group", { name: "Refuse RET-1?" })).toBeTruthy(); expect(screen.getByRole("button", { name: "Confirm refusal" })).toHaveProperty("disabled", true);
    expect(api.decideReturn).toHaveBeenCalledTimes(1); await user.click(screen.getByRole("button", { name: "Review current record" })); await user.click(screen.getByRole("button", { name: "Confirm refusal" }));
    await screen.findByText(/The return was refused/); expect(api.decideReturn).toHaveBeenCalledTimes(2);
  });
  it("retains the next refusal draft after a previous successful acceptance", async () => {
    route(detailPath); const user = await open("accept"); await user.click(screen.getByRole("button", { name: "Confirm acceptance" }));
    await screen.findByText(/The return was accepted/); await open("refuse", user);
    vi.mocked(api.decideReturn).mockRejectedValueOnce(new HttpError("http", 409, { title: "This return has already been dealt with" }));
    await user.click(screen.getByRole("button", { name: "Confirm refusal" })); await screen.findByRole("button", { name: "Review current record" });
    expect(screen.getByRole("group", { name: "Refuse RET-1?" })).toBeTruthy(); expect(api.decideReturn).toHaveBeenCalledTimes(2);
  });
  it("does not repeat an already-settled race even if the original decision had no answer", async () => {
    vi.mocked(api.decideReturn).mockImplementationOnce(async () => { records = [{ ...returnRecord, status: "Received", refund: "Pending" }]; throw new HttpError("network", null); });
    route(detailPath); const user = await open("accept"); await user.click(screen.getByRole("button", { name: "Confirm acceptance" })); await screen.findByText(/A refund was requested without a confirmed outcome/);
    expect(screen.getByRole("button", { name: "Confirm acceptance" })).toHaveProperty("disabled", true); expect(screen.queryByRole("button", { name: "Review current record" })).toBeNull(); expect(api.decideReturn).toHaveBeenCalledTimes(1);
  });
  it("retains an uncertain draft and blocks decisions through failed read recovery", async () => {
    vi.mocked(api.decideReturn).mockRejectedValueOnce(new HttpError("network", null)); route(detailPath); const user = await open("refuse");
    vi.mocked(api.returns).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(screen.getByRole("button", { name: "Confirm refusal" }));
    await screen.findByRole("button", { name: "Try again" }); expect(screen.getByRole("button", { name: "Confirm refusal" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByRole("button", { name: "Review current record" }); expect(api.decideReturn).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("group", { name: "Refuse RET-1?" })).toBeTruthy();
  });
  it("blocks new decisions after accepted-write→failed-read and retries only the read", async () => {
    route(detailPath); const user = await open("accept"); vi.mocked(api.returns).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Confirm acceptance" })); await screen.findByRole("button", { name: "Try again" }); expect(screen.getByRole("button", { name: "Refuse" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Refuse" })).toHaveProperty("disabled", false)); expect(api.decideReturn).toHaveBeenCalledTimes(1);
  });
  it("requires renewed review when a confirmation's authoritative read changes", async () => {
    route(detailPath); const user = await open("accept"); await user.click(screen.getByRole("button", { name: "Refresh" })); await screen.findByRole("button", { name: "Review current record" });
    await user.click(screen.getByRole("button", { name: "Confirm acceptance" })); expect(api.decideReturn).not.toHaveBeenCalled();
  });
  it("does not report success for an answer referring to a different order", async () => {
    vi.mocked(api.decideReturn).mockResolvedValue({ ...returnRecord, status: "Accepted", orderNumber: "OTHER" }); route(detailPath); const user = await open("accept");
    await user.click(screen.getByRole("button", { name: "Confirm acceptance" })); await screen.findByRole("button", { name: "Review current record" }); expect(screen.queryByText(/The return was accepted/)).toBeNull();
  });
  it("ignores late original-order reads after departure to a different store", async () => {
    const old = deferred<ReturnType<typeof returnOrder>>(); vi.mocked(api.order).mockReturnValueOnce(old.promise); route(detailPath); const user = userEvent.setup();
    await waitFor(() => expect(api.order).toHaveBeenCalledTimes(1)); await user.click(screen.getByRole("link", { name: "Switch store" })); await screen.findByText("€0.00");
    await act(async () => old.resolve({ ...returnOrder(), email: "old-store@example.test" })); expect(screen.queryByText("old-store@example.test")).toBeNull();
  });
  it("ignores a departed store's pending decision result", async () => {
    const old = deferred<OrderReturn>(); vi.mocked(api.decideReturn).mockReturnValueOnce(old.promise); route(detailPath); const user = await open("accept");
    await user.click(screen.getByRole("button", { name: "Confirm acceptance" })); await user.click(screen.getByRole("link", { name: "Switch store" })); await screen.findByText("€0.00");
    await act(async () => old.resolve({ ...returnRecord, status: "Accepted" })); expect(api.returns).toHaveBeenCalledTimes(2); expect(screen.queryByText(/The return was accepted/)).toBeNull();
  });
  it("provides the actual direct application route and Czech controls without the old workbench", async () => {
    await i18n.changeLanguage("cs"); vi.spyOn(api, "me").mockResolvedValue({ id: "owner", email: "owner@example.test", role: "Support", tenantId: "tenant" }); vi.spyOn(api, "stores").mockResolvedValue([store]);
    window.history.replaceState(null, "", detailPath); render(<App />); await screen.findByRole("heading", { name: "RET-1", level: 1 });
    await screen.findByRole("heading", { name: "Zboží a peníze" }); expect(screen.getByRole("link", { name: "Zpět na vrácení zboží" })).toBeTruthy(); expect(screen.queryByRole("button", { name: "Accept" })).toBeNull();
    expect(within(screen.getByRole("main")).getByText(/Vrácení můžete prohlížet/)).toBeTruthy();
  });
});
