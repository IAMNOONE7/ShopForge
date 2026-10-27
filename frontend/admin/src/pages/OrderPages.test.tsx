// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { StoreLayout } from "../layouts/StoreLayout";
import { SessionContext } from "../session";
import { order, orderDetail, orderPage, orderParcels } from "../test/orderFixtures";
import { maxOrderPage } from "../utils/orderListQuery";
import { StoreOrderPage, StoreOrdersPage } from "./OrderPages";

const store: AdminStore = { id: "store-a", name: "Store A", currency: "CZK", culture: "cs-CZ", status: "published", primaryHostName: "shop.localhost", logoUrl: null, company: null, returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 8 } };
function History() {
  const location = useLocation(); const navigate = useNavigate();
  return <><output data-testid="location">{location.pathname}{location.search}</output><button onClick={() => navigate(-1)}>History back</button><Link to="/stores/store-b/orders">Switch store</Link></>;
}
function route(path = "/stores/store-a/orders", role = "Owner") {
  return render(<SessionContext value={{ user: { id: "owner", email: "owner@example.test", role, tenantId: "tenant" }, logout: async () => undefined, logoutPending: false, logoutError: null }}>
    <MemoryRouter initialEntries={[path]}><History /><Routes>
      <Route element={<Outlet context={{ stores: { status: "ready", data: [store, { ...store, id: "store-b", name: "Store B" }], refreshing: false, refreshError: null }, reloadStores: () => undefined }} />}>
        <Route path="/stores/:storeId" element={<StoreLayout />}><Route path="orders" element={<StoreOrdersPage />} /><Route path="orders/:number" element={<StoreOrderPage />} /></Route>
      </Route>
    </Routes></MemoryRouter>
  </SessionContext>);
}
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((yes) => { resolve = yes; }); return { promise, resolve };
}
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  vi.spyOn(api, "orders").mockImplementation(async (_store, _signal, query) => ({ ...orderPage, page: query?.page ?? 1 }));
  vi.spyOn(api, "order").mockResolvedValue(orderDetail);
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

describe("focused order list", () => {
  it("reads only a server page, offers supported sorts and keeps money/actions on details", async () => {
    route(); await screen.findByRole("link", { name: order.number });
    expect(api.orders).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, sort: "-placed", q: "" });
    expect(api.order).not.toHaveBeenCalled();
    expect(screen.getByText("Showing 1–1 of 1")).toBeTruthy();
    expect(screen.queryByText("149")).toBeNull();
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
    expect(screen.getAllByRole("option").map((option) => option.getAttribute("value"))).toEqual(["-placed", "placed", "status", "-status"]);
  });

  it("gets a second server page and searches beyond loaded rows without fetching all orders", async () => {
    const user = userEvent.setup();
    vi.mocked(api.orders).mockImplementation(async (_store, _signal, query) => {
      if (query?.q) return { ...orderPage, items: [{ ...order, number: "FOUND-901" }] };
      return { ...orderPage, page: query?.page ?? 1, totalCount: 26, hasMore: query?.page !== 2, items: [{ ...order, number: query?.page === 2 ? "ORD-1051" : order.number }] };
    });
    route(); await user.click(await screen.findByRole("button", { name: "Next page" }));
    await screen.findByRole("link", { name: "ORD-1051" });
    expect(api.orders).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 2, sort: "-placed", q: "" });
    expect(document.activeElement?.textContent).toBe("Showing 26–26 of 26");
    await user.type(screen.getByRole("searchbox", { name: "Search orders" }), "  Other buyer  ");
    expect(api.orders).toHaveBeenCalledTimes(2);
    await user.click(screen.getByRole("button", { name: "Search" }));
    await screen.findByRole("link", { name: "FOUND-901" });
    expect(api.orders).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, sort: "-placed", q: "Other buyer" });
    expect(screen.queryByRole("link", { name: "ORD-1051" })).toBeNull();
  });

  it("resets the page on every supported sort and preserves applied search terms", async () => {
    const user = userEvent.setup(); route("/stores/store-a/orders?page=3&q=buyer");
    await screen.findByRole("link", { name: order.number });
    for (const sort of ["placed", "status", "-status", "-placed"]) {
      await user.selectOptions(screen.getByRole("combobox", { name: "Sort orders" }), sort);
      await waitFor(() => expect(api.orders).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, q: "buyer", sort }));
    }
  });

  it("uses the returned number for a direct detail link and preserves the list query when returning", async () => {
    const user = userEvent.setup(); const number = "RETURNED number/1";
    vi.mocked(api.orders).mockResolvedValue({ ...orderPage, page: 2, totalCount: 26, items: [{ ...order, number }] });
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, number });
    route("/stores/store-a/orders?page=2&q=50%25+%26+buyer&sort=-status");
    const link = await screen.findByRole("link", { name: number });
    expect(link.getAttribute("href")).toBe("/stores/store-a/orders/RETURNED%20number%2F1?page=2&q=50%25+%26+buyer&sort=-status");
    await user.click(link); await screen.findAllByText("€149.00");
    expect(api.order).toHaveBeenCalledWith("store-a", number, expect.any(AbortSignal));
    expect(api.orders).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("link", { name: "Back to orders" }));
    expect(await screen.findByRole("searchbox", { name: "Search orders" })).toHaveProperty("value", "50% & buyer");
    await waitFor(() => expect(api.orders).toHaveBeenCalledTimes(2));
    expect(screen.getByTestId("location").textContent).toBe("/stores/store-a/orders?page=2&q=50%25+%26+buyer&sort=-status");
  });

  it("rejects one-character drafts, clears them locally and restores applied terms on browser Back", async () => {
    const user = userEvent.setup(); route(); await screen.findByRole("link", { name: order.number });
    const input = screen.getByRole("searchbox", { name: "Search orders" });
    await user.type(input, "x{Enter}");
    expect(document.activeElement).toBe(input); expect(api.orders).toHaveBeenCalledTimes(1);
    expect(screen.getByText(/Enter at least 2 characters, or clear/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Clear search" }));
    expect(input).toHaveProperty("value", ""); expect(api.orders).toHaveBeenCalledTimes(1);
    await user.type(input, "buyer{Enter}");
    await waitFor(() => expect(api.orders).toHaveBeenCalledTimes(2));
    await user.click(screen.getByRole("button", { name: "Clear search" }));
    await waitFor(() => expect(api.orders).toHaveBeenCalledTimes(3));
    expect(document.activeElement).toBe(screen.getByRole("searchbox", { name: "Search orders" }));
    await user.click(screen.getByRole("button", { name: "History back" }));
    await waitFor(() => expect(screen.getByRole("searchbox", { name: "Search orders" })).toHaveProperty("value", "buyer"));
    expect(api.orders).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, q: "buyer", sort: "-placed" });
  });

  it.each([
    ["/stores/store-a/orders", 0, "No orders yet"],
    ["/stores/store-a/orders?q=missing", 0, "No matching orders"],
    ["/stores/store-a/orders?page=4&q=buyer&sort=status", 2, "No orders on this page"],
  ])("distinguishes empty states for %s", async (path, totalCount, title) => {
    const user = userEvent.setup();
    vi.mocked(api.orders).mockImplementation(async (_store, _signal, query) => ({ ...orderPage, items: [], totalCount, page: query!.page }));
    route(path); await screen.findByRole("heading", { name: title });
    if (path.includes("page=4")) {
      await user.click(screen.getByRole("button", { name: "Go to first page" }));
      await waitFor(() => expect(api.orders).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, q: "buyer", sort: "status" }));
    }
  });

  it("keeps same-query rows during refresh failure and retries only reads", async () => {
    const user = userEvent.setup(); route(); await screen.findByRole("link", { name: order.number });
    vi.mocked(api.orders).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Refresh" })); await screen.findByRole("alert");
    expect(screen.getByRole("link", { name: order.number })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    expect(api.orders).toHaveBeenCalledTimes(3); expect(api.order).not.toHaveBeenCalled();
  });

  it("aborts an old query and ignores its late response after a search", async () => {
    const user = userEvent.setup(); const late = deferred<typeof orderPage>();
    vi.mocked(api.orders).mockReturnValueOnce(late.promise).mockResolvedValue({ ...orderPage, items: [{ ...order, number: "NEW-RESULT" }] });
    route(); await screen.findByText("Loading orders…");
    await user.type(screen.getByRole("searchbox", { name: "Search orders" }), "new{Enter}");
    await screen.findByRole("link", { name: "NEW-RESULT" });
    expect(vi.mocked(api.orders).mock.calls[0][1]?.aborted).toBe(true);
    await act(async () => late.resolve(orderPage));
    expect(screen.queryByRole("link", { name: order.number })).toBeNull();
  });

  it("clears scoped drafts/results when changing store and ignores a late old-store read", async () => {
    const user = userEvent.setup(); const late = deferred<typeof orderPage>();
    vi.mocked(api.orders).mockReturnValueOnce(late.promise).mockResolvedValue({ ...orderPage, items: [{ ...order, number: "STORE-B" }] });
    route(); await user.type(screen.getByRole("searchbox", { name: "Search orders" }), "draft");
    await user.click(screen.getByRole("link", { name: "Switch store" }));
    await screen.findByRole("link", { name: "STORE-B" });
    expect(screen.getByRole("searchbox", { name: "Search orders" })).toHaveProperty("value", "");
    await act(async () => late.resolve(orderPage));
    expect(screen.queryByRole("link", { name: order.number })).toBeNull();
  });

  it.each(["0", "-2", "1.5", "1e3", String(maxOrderPage + 1), "99999999999999999999"])("normalizes invalid page %s without unsafe server offsets or unsupported controls", async (page) => {
    route(`/stores/store-a/orders?page=${page}&sort=total&q=x`); await screen.findByRole("link", { name: order.number });
    expect(api.orders).toHaveBeenLastCalledWith("store-a", expect.any(AbortSignal), { page: 1, q: "", sort: "-placed" });
    expect(screen.getByRole("link", { name: "Reset list options" })).toBeTruthy();
    expect(screen.getByRole("combobox", { name: "Sort orders" })).toHaveProperty("value", "-placed");
  });

  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("keeps direct detail reads available and gates retained actions for %s", async (role) => {
    route(`/stores/store-a/orders/${order.number}?page=2&q=buyer`, role);
    await screen.findAllByText("€149.00"); expect(api.orders).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Mark as paid" }) !== null).toBe(["Owner", "Admin"].includes(role));
    expect(screen.getByRole("link", { name: "Back to orders" }).getAttribute("href")).toBe("/stores/store-a/orders?page=2&q=buyer");
  });

  it("localizes list controls, counts, unknown statuses and detail navigation in Czech", async () => {
    await i18n.changeLanguage("cs");
    vi.mocked(api.orders).mockResolvedValue({ ...orderPage, items: [{ ...order, status: "NewServerStatus" }] });
    route(); await screen.findByRole("heading", { name: "Objednávky" });
    await screen.findByText("Zobrazeno 1–1 z celkového počtu 1");
    expect(screen.getByRole("searchbox", { name: "Hledat objednávky" })).toBeTruthy();
    expect(screen.getByText("Neznámý stav")).toBeTruthy();
  });

  it("registers the actual direct detail route without a preceding list fetch", async () => {
    vi.spyOn(api, "me").mockResolvedValue({ id: "owner", email: "owner@example.test", role: "Support", tenantId: "tenant" });
    vi.spyOn(api, "stores").mockResolvedValue([store]);
    window.history.replaceState(null, "", `/stores/store-a/orders/${order.number}?sort=status`);
    render(<App />); await screen.findAllByText("€149.00");
    expect(api.orders).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
  });
});


describe("saved parcels on the actual order route", () => {
  beforeEach(() => { HTMLElement.prototype.scrollIntoView = vi.fn(); });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support"])("lets %s read the saved packing estimate without gaining management controls", async role => {
    const user = userEvent.setup();
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, parcels: orderParcels });
    route("/stores/store-a/orders/" + orderDetail.number + "#order-parcels-title", role);
    expect(await screen.findByRole("heading", { name: "Saved parcel estimate" })).toBeTruthy();
    expect(screen.getByText("CHAIR-OAK")).toBeTruthy();
    await user.click(screen.getAllByText("Variant ID")[0]);
    expect(document.querySelector(".order-parcel-contents details")).toHaveProperty("open", true);
    expect(screen.queryByRole("button", { name: "Mark as paid" }) !== null).toBe(["Owner", "Admin"].includes(role));
    expect(api.orders).not.toHaveBeenCalled();
    expect(api.order).toHaveBeenCalledTimes(1);
  });
});
