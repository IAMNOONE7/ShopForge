// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminOrderDetail } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { orderDetail, orderParcels } from "../test/orderFixtures";
import { OrderDetailsSection } from "./OrderDetailsSection";
import { OrderParcels } from "./OrderParcels";

beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); sessionStorage.clear(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });
function show(order = { ...orderDetail, parcels: orderParcels }) {
  return render(<OrderParcels order={order} />);
}
function workspace(canManage = false, hash = "") {
  return render(<MemoryRouter initialEntries={["/stores/store-a/orders/" + orderDetail.number + hash]}><OrderDetailsSection userId="owner" storeId="store-a" number={orderDetail.number} canManage={canManage} /></MemoryRouter>);
}

describe("saved parcel presentation", () => {
  it.each(["en", "cs"])("shows the saved packing facts with localized units and clear estimate/discount meaning in %s", async language => {
    await i18n.changeLanguage(language);
    show();
    const region = screen.getByRole("region");
    expect(within(region).getAllByRole("heading", { level: 3 }).map(node => node.textContent)).toEqual(language === "en" ? ["Parcel 1", "Parcel 2"] : ["Balík 1", "Balík 2"]);
    expect(within(region).getByText(language === "en" ? "7,100 g" : "7 100 g", { exact: false })).toBeTruthy();
    expect(within(region).getByText(language === "en" ? "10,000 g" : "10 000 g", { exact: false })).toBeTruthy();
    expect(within(region).getByText(language === "en" ? "Ships alone" : "Odeslat samostatně")).toBeTruthy();
    expect(within(region).getByText(language === "en" ? "Standard packing" : "Běžné balení")).toBeTruthy();
    expect(within(region).getByText(language === "en" ? "Quantity: 8" : "Počet: 8")).toBeTruthy();
    expect(within(region).getByText(language === "en" ? /suggestion for packing, not a parcel booking/ : /návrh pro balení, nikoli o objednání přepravy/)).toBeTruthy();
    expect(within(region).getByText(language === "en" ? /before any delivery discount/ : /před případnou slevou na dopravu/)).toBeTruthy();
    expect(within(region).queryByRole("button")).toBeNull();
  });

  it("uses saved parcel numbers, preserves repeated SKUs and exposes only actual variant IDs on demand", async () => {
    const user = userEvent.setup();
    show({ ...orderDetail, parcels: [{ ...orderParcels[0], number: 4 }, { ...orderParcels[1], number: 7,
      contents: [{ ...orderParcels[1].contents[0], sku: "CHAIR-OAK" }] }] });
    expect(screen.getByRole("heading", { name: "Parcel 4" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Parcel 7" })).toBeTruthy();
    expect(screen.getAllByText("CHAIR-OAK")).toHaveLength(2);
    const details = document.querySelectorAll("details");
    expect([...details].every(node => !node.open)).toBe(true);
    await user.click(screen.getAllByText("Variant ID")[0]);
    expect(details[0].open).toBe(true);
    expect(details[0].textContent).toContain(orderParcels[0].contents[0].variantId);
    expect(details[1].open).toBe(false);
  });

  it.each([[undefined, "Parcel details were not supplied in this response. Refresh the order to check."],
    [[], "No parcel estimate was recorded for this order."],
    [null, "The parcel details could not be checked."]])("distinguishes parcel state %j without inventing shipment state", (parcels, message) => {
    render(<OrderParcels order={{ ...orderDetail, parcels: parcels as AdminOrderDetail["parcels"], status: "Shipped" }} />);
    expect(screen.getByText(message as string, { exact: false })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Parcel 1" })).toBeNull();
    expect(screen.queryByText("No shipment recorded.")).toBeNull();
  });

  it.each([["JPY", 125, "JP¥125"], ["KWD", 1.234, "KWD 1.234"], ["EUR", 0, "€0.00"]])("uses the recorded %s currency without rounding to store defaults", (currency, price, formatted) => {
    show({ ...orderDetail, currency, parcels: [{ ...orderParcels[0], price }] });
    expect(screen.getByText(formatted)).toBeTruthy();
  });

  it("renders long SKU markup as literal text without a link or current catalog lookup", () => {
    const read = vi.spyOn(api, "products");
    show({ ...orderDetail, parcels: [{ ...orderParcels[0], contents: [{ ...orderParcels[0].contents[0], sku: "<img src=x onerror=alert(1)>" }] }] });
    expect(screen.getByText("<img src=x onerror=alert(1)>")).toBeTruthy();
    expect(document.querySelector("img, a")).toBeNull();
    expect(read).not.toHaveBeenCalled();
  });
});

describe("parcel facts in the order workspace", () => {
  it("focuses the section from a direct hash and retains payment, shipment and order totals separately", async () => {
    vi.spyOn(api, "order").mockResolvedValue({ ...orderDetail, parcels: orderParcels, shippingPrice: 0, grandTotal: 90, discount: { code: "FREE", name: "Free delivery", amount: 20 } });
    workspace(false, "#order-parcels-title");
    const heading = await screen.findByRole("heading", { name: "Saved parcel estimate" });
    await waitFor(() => expect(document.activeElement).toBe(heading));
    expect(screen.getByRole("link", { name: "Saved parcel estimate" }).getAttribute("href")).toBe("#order-parcels-title");
    expect(within(screen.getByRole("region", { name: "Saved parcel estimate" })).getByText("€12.00")).toBeTruthy();
    expect(screen.getByText("€0.00")).toBeTruthy();
    expect(screen.getByText("−€20.00")).toBeTruthy();
    expect(screen.getByRole("region", { name: "Recorded payment" })).toBeTruthy();
    expect(screen.getByRole("region", { name: "Delivery and contact" })).toBeTruthy();
  });

  it("labels retained parcels stale during failed refresh, then replaces them with newly checked facts", async () => {
    const user = userEvent.setup();
    const read = vi.spyOn(api, "order").mockResolvedValueOnce({ ...orderDetail, parcels: orderParcels }).mockRejectedValueOnce(new HttpError("http", 503));
    workspace();
    await screen.findByRole("heading", { name: "Parcel 1" });
    await user.click(screen.getByRole("button", { name: "Refresh" }));
    await screen.findByText("Order details may be out of date. Changes are paused until fresh data is available.");
    expect(screen.getByText("CHAIR-OAK")).toBeTruthy();
    read.mockResolvedValueOnce({ ...orderDetail, parcels: null });
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await screen.findByText(/The parcel details could not be checked/);
    expect(screen.queryByText("CHAIR-OAK")).toBeNull();
    expect(screen.getByText("Awaiting payment")).toBeTruthy();
    read.mockResolvedValueOnce({ ...orderDetail, parcels: [] });
    await user.click(screen.getByRole("button", { name: "Refresh" }));
    await screen.findByText("No parcel estimate was recorded for this order.");
    expect(screen.queryByText(/The parcel details could not be checked/)).toBeNull();
  });

  it("refreshes the saved parcel view after a successful management action without replaying that command", async () => {
    const user = userEvent.setup();
    const paid = { ...orderDetail, status: "Paid", parcels: orderParcels };
    const read = vi.spyOn(api, "order").mockResolvedValueOnce({ ...orderDetail, parcels: [] }).mockResolvedValue(paid);
    const write = vi.spyOn(api, "confirmOrderPayment").mockResolvedValue(paid);
    workspace(true);
    await user.click(await screen.findByRole("button", { name: "Mark as paid" }));
    await user.click(screen.getByRole("button", { name: "Confirm payment" }));
    await screen.findByText("CHAIR-OAK");
    expect(read).toHaveBeenCalledTimes(2);
    expect(write).toHaveBeenCalledTimes(1);
    expect(screen.getByText("Paid")).toBeTruthy();
    expect(screen.queryByText("No parcel estimate was recorded for this order.")).toBeNull();
  });

  it("aborts an old order read and ignores its parcel answer after switching stores/orders", async () => {
    let resolve!: (value: AdminOrderDetail) => void;
    const old = new Promise<AdminOrderDetail>(yes => { resolve = yes; });
    const read = vi.spyOn(api, "order").mockReturnValueOnce(old).mockResolvedValue({ ...orderDetail, number: "OTHER", parcels: [] });
    const view = workspace();
    await waitFor(() => expect(read).toHaveBeenCalledTimes(1));
    const signal = read.mock.calls[0][2]!;
    view.rerender(<MemoryRouter><OrderDetailsSection userId="other-user" storeId="store-b" number="OTHER" canManage={false} /></MemoryRouter>);
    await screen.findByText("No parcel estimate was recorded for this order.");
    expect(signal.aborted).toBe(true);
    await act(async () => resolve({ ...orderDetail, parcels: orderParcels }));
    expect(screen.queryByText("CHAIR-OAK")).toBeNull();
    expect(screen.getByRole("heading", { level: 1 }).textContent).toContain("OTHER");
  });
});
