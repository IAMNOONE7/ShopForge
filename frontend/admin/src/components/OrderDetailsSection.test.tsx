// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminOrderDetail } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { orderDetail, returnedOrder, unappliedPayment } from "../test/orderFixtures";
import { OrderDetailsSection } from "./OrderDetailsSection";

beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  vi.spyOn(api, "order").mockResolvedValue(orderDetail);
  HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });
function show(canManage = false, hash = "") { return render(<MemoryRouter initialEntries={[`/stores/store-a/orders/${orderDetail.number}${hash}`]}><OrderDetailsSection storeId="store-a" number={orderDetail.number} canManage={canManage} /></MemoryRouter>); }

describe("routed order details", () => {
  it("retains carrier, pickup, phone and saved currency without reading the list", async () => {
    const list = vi.spyOn(api, "orders"); show();
    expect(await screen.findByText("packeta")).toBeTruthy();
    expect(screen.getByText(/Z-BOX Hlavní nádraží/)).toBeTruthy();
    expect(screen.getByText("+420 777 123 456")).toBeTruthy();
    expect(screen.getAllByText("€149.00")[0]).toBeTruthy();
    expect(list).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
  });

  it("uses the saved line total and keeps duplicate product names separate", async () => {
    const warn = vi.spyOn(console, "error").mockImplementation(() => undefined);
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, pickupPoint: null, lines: [
      { ...orderDetail.lines[0], lineTotal: 89.99 }, { ...orderDetail.lines[0], quantity: 1, lineTotal: 45 },
    ] });
    show(); await screen.findByText("Delivery address below");
    expect(screen.getAllByText("Chair")).toHaveLength(2);
    expect(screen.getByText("€89.99")).toBeTruthy();
    expect(warn).not.toHaveBeenCalled();
  });

  it.each(["Started", "Pending", "Authorized", "Unrecognised"])("does not offer manual confirmation for an open %s gateway attempt", async (status) => {
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, paymentAttempts: [{ ...orderDetail.paymentAttempts[0], reference: "gateway-ref", status }] });
    show(true); await screen.findByText(/Manual payment confirmation is unavailable/);
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
    expect(screen.getByRole("button", { name: "Cancel order" })).toBeTruthy();
  });

  it("serializes existing management actions and waits for a fresh read after an uncertain write", async () => {
    const user = userEvent.setup();
    let reject!: (reason: unknown) => void;
    const write = new Promise<AdminOrderDetail>((_, no) => { reject = no; });
    vi.spyOn(api, "confirmOrderPayment").mockReturnValue(write);
    show(true); await screen.findByRole("button", { name: "Mark as paid" });
    vi.mocked(api.order).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Mark as paid" }));
    await user.dblClick(screen.getByRole("button", { name: "Confirm payment" }));
    expect(api.confirmOrderPayment).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Cancel order" })).toHaveProperty("disabled", true);
    await act(async () => reject(new HttpError("network", null)));
    await screen.findByRole("button", { name: "Try again" });
    expect(screen.getByRole("button", { name: "Mark as paid" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Mark as paid" })).toHaveProperty("disabled", false));
    expect(api.confirmOrderPayment).toHaveBeenCalledTimes(1);
  });

  it("recovers a missing order and rejects a response for a different number", async () => {
    const user = userEvent.setup();
    vi.mocked(api.order).mockRejectedValueOnce(new HttpError("http", 404)); show();
    await screen.findByRole("heading", { name: "Order not found" });
    vi.mocked(api.order).mockResolvedValueOnce({ ...orderDetail, number: "OTHER" });
    await user.click(screen.getAllByRole("button", { name: "Refresh" })[0]);
    await screen.findByRole("alert");
    expect(screen.queryAllByText("€149.00")[0] ?? null).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect((await screen.findAllByText("€149.00"))[0]).toBeTruthy();
  });

  it("preserves document downloads and their order identity", async () => {
    const user = userEvent.setup();
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, documents: [{ number: "INV-1", kind: "Invoice", issuedAt: "2026-10-15T00:00:00Z" }] });
    vi.spyOn(api, "downloadDocument").mockResolvedValue(); show();
    await user.click(await screen.findByRole("button", { name: "Invoice INV-1" }));
    expect(api.downloadDocument).toHaveBeenCalledWith("store-a", orderDetail.number, "INV-1");
  });

  it("keeps shipment input after failure and retains the existing refund action on details", async () => {
    const user = userEvent.setup();
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, status: "Paid" });
    vi.spyOn(api, "createShipment").mockRejectedValue(new HttpError("network", null));
    vi.spyOn(api, "refundOrder").mockResolvedValue({ ...orderDetail, status: "Refunded" });
    show(true);
    const tracking = await screen.findByRole("textbox", { name: "Tracking number" });
    await user.type(tracking, "  TRACK-123  "); await user.click(screen.getByRole("button", { name: "Ship" }));
    await user.click(screen.getByRole("button", { name: "Confirm shipment" }));
    await screen.findByRole("alert");
    await waitFor(() => expect(tracking).toHaveProperty("disabled", false));
    expect(tracking).toHaveProperty("value", "  TRACK-123  ");
    expect(api.createShipment).toHaveBeenCalledWith("store-a", orderDetail.number, "TRACK-123");
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, status: "Refunded" });
    await user.click(screen.getByRole("button", { name: "Refund" }));
    await user.click(screen.getByRole("button", { name: "Request refund" }));
    await screen.findByText("Credited");
    expect(api.refundOrder).toHaveBeenCalledWith("store-a", orderDetail.number);
    expect(screen.queryByRole("button", { name: "Ship" })).toBeNull();
  });

  it("separates recorded payment from attempts, authorization and Test/Live environments", async () => {
    const states = ["Started", "Pending", "Authorized", "Unrecognised", "Paid", "Failed"];
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, paymentReference: "order-reference", paidAt: null,
      paymentAttempts: states.map((status, index) => ({ ...orderDetail.paymentAttempts[0], reference: `gateway-${index}`, status, currency: index === 0 ? "USD" : "EUR", environment: index === 0 ? "test" : index === 1 ? "live" : null })) });
    show();
    const payment = within(await screen.findByRole("region", { name: "Recorded payment" }));
    expect(payment.getByText("order-reference")).toBeTruthy();
    expect(payment.getByText("Not recorded")).toBeTruthy();
    const attempts = within(screen.getByRole("region", { name: "Payment attempts" }));
    expect(attempts.getAllByRole("listitem")).toHaveLength(6);
    expect(attempts.getByText(/Authorization does not confirm that the order is paid/)).toBeTruthy();
    expect(attempts.getByText(/It is not evidence of live takings/)).toBeTruthy();
    expect(attempts.getByText("Live environment")).toBeTruthy();
    expect(attempts.getByText("US$149.00")).toBeTruthy();
    expect(screen.getByText("Awaiting payment")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
  });

  it("links real related records, uses their currencies and focuses a record after a direct hash load", async () => {
    const user = userEvent.setup();
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, unappliedPayments: [unappliedPayment], returns: [returnedOrder] });
    show(false, "#order-return-return-a");
    const returned = await screen.findByRole("heading", { name: "RET-1" });
    await waitFor(() => expect(document.activeElement).toBe(returned));
    expect(screen.getByRole("link", { name: "RET-1" }).getAttribute("href")).toBe("#order-return-return-a");
    expect(screen.getByRole("link", { name: "comgate · extra-payment" }).getAttribute("href")).toBe("#order-unapplied-money-a");
    expect(screen.getByText("US$175.00")).toBeTruthy(); expect(screen.getByText("Refund refused")).toBeTruthy();
    expect(screen.getByText(/do not send another refund/)).toBeTruthy();
    expect(screen.getByRole("link", { name: "Open store returns" }).getAttribute("href")).toBe("/stores/store-a/returns");
    expect(screen.queryByRole("button", { name: "Request refund" })).toBeNull();
    vi.mocked(api.order).mockResolvedValueOnce({ ...orderDetail, unappliedPayments: [unappliedPayment], returns: [returnedOrder] });
    const refresh = screen.getByRole("button", { name: "Refresh" });
    await user.click(refresh);
    await waitFor(() => expect(api.order).toHaveBeenCalledTimes(2));
    expect(document.activeElement).toBe(refresh);
  });

  it.each(["Pending", "ByStore", "ClosedByHand"])("keeps a received return and its %s refund distinct", async (refund) => {
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, status: "Refunded", returns: [{ ...returnedOrder, refund, refundProblem: null }] });
    show(); await screen.findByText("Goods received");
    expect(screen.getByText("Booked credit")).toBeTruthy();
    expect(screen.getByText(/Receipt of goods and a booked credit do not establish/)).toBeTruthy();
    const copy = refund === "Pending" ? /The refund has no confirmed outcome/ : refund === "ByStore" ? /The store is responsible for returning this money/ : /Someone closed this record manually/;
    expect(screen.getByText(copy)).toBeTruthy();
  });

  it("requires confirmation, restores Escape focus and prevents confirmation of a refreshed changed order", async () => {
    const user = userEvent.setup(); const cancel = vi.spyOn(api, "cancelOrder"); show(true);
    const button = await screen.findByRole("button", { name: "Cancel order" });
    await user.click(button);
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Confirm cancellation" }));
    expect(cancel).not.toHaveBeenCalled(); await user.keyboard("{Escape}");
    await waitFor(() => expect(document.activeElement).toBe(button));
    await user.click(button);
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, status: "Paid" });
    await user.click(screen.getByRole("button", { name: "Refresh" }));
    await screen.findByText(/The order changed while you were reviewing/);
    expect(screen.getByRole("button", { name: "Confirm cancellation" })).toHaveProperty("disabled", true);
    expect(cancel).not.toHaveBeenCalled();
  });

  it("reconciles a gateway 409 and never offers a write retry as a read recovery", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "confirmOrderPayment").mockRejectedValue(new HttpError("http", 409, { title: "This order's payment is confirmed by its gateway" }));
    show(true); await user.click(await screen.findByRole("button", { name: "Mark as paid" }));
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, paymentAttempts: [{ ...orderDetail.paymentAttempts[0], reference: "held", status: "Unrecognised" }] });
    await user.click(screen.getByRole("button", { name: "Confirm payment" }));
    await screen.findByText(/The server refused manual confirmation/);
    await screen.findByText("Unrecognised result");
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
    expect(api.confirmOrderPayment).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("link", { name: "Review payment attempts" }).getAttribute("href")).toBe("#order-payment-attempts");
  });

  it("requires manual tracking but allows a carrier to provide its own tracking details", async () => {
    const user = userEvent.setup();
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, status: "Paid", carrier: "manual" });
    const shipment = vi.spyOn(api, "createShipment").mockResolvedValue({ ...orderDetail, status: "Shipped" });
    show(true); await user.click(await screen.findByRole("button", { name: "Ship" }));
    expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Tracking number" }));
    expect(shipment).not.toHaveBeenCalled();
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, status: "Paid", carrier: "packeta" });
    await user.click(screen.getByRole("button", { name: "Refresh" }));
    await screen.findByText(/Optional for this carrier/);
    await user.click(screen.getByRole("button", { name: "Ship" }));
    await user.click(screen.getByRole("button", { name: "Confirm shipment" }));
    expect(shipment).toHaveBeenCalledWith("store-a", orderDetail.number, "");
  });

  it("keeps empty/unknown records truthful and does not mislabel a new document kind as an invoice", async () => {
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, paymentAttempts: [{ ...orderDetail.paymentAttempts[0], status: "Future", environment: "staging" }],
      documents: [{ number: "NEW-1", kind: "FutureDocument", issuedAt: orderDetail.placedAt }] });
    show(); await screen.findByText("Unknown attempt status");
    expect(screen.getByText("Unknown environment")).toBeTruthy();
    expect(screen.queryByText("Live environment")).toBeNull();
    expect(screen.getByText("No unapplied payments recorded.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Document NEW-1" })).toBeTruthy();
  });

  it("ignores a write completion after moving to another store/order and resets confirmation state", async () => {
    const user = userEvent.setup(); let resolve!: (value: AdminOrderDetail) => void;
    const write = new Promise<AdminOrderDetail>((yes) => { resolve = yes; });
    vi.spyOn(api, "confirmOrderPayment").mockReturnValue(write);
    const view = show(true); await user.click(await screen.findByRole("button", { name: "Mark as paid" }));
    await user.click(screen.getByRole("button", { name: "Confirm payment" }));
    const next = { ...orderDetail, number: "OTHER", email: "other@example.test" };
    vi.mocked(api.order).mockResolvedValue(next);
    view.rerender(<MemoryRouter><OrderDetailsSection storeId="store-b" number="OTHER" canManage /></MemoryRouter>);
    await screen.findByText("other@example.test"); await act(async () => resolve({ ...orderDetail, status: "Paid" }));
    expect(api.order).toHaveBeenCalledTimes(2);
    expect(screen.queryByText(/Payment confirmation recorded/)).toBeNull();
    expect(screen.queryByRole("group", { name: /Confirm payment/ })).toBeNull();
  });
});
