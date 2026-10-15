// @vitest-environment jsdom
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminOrderDetail } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { orderDetail } from "../test/orderFixtures";
import { OrderDetailsSection } from "./OrderDetailsSection";

beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  vi.spyOn(api, "order").mockResolvedValue(orderDetail);
  vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });
function show(canManage = false) { return render(<OrderDetailsSection storeId="store-a" number={orderDetail.number} canManage={canManage} />); }

describe("routed order details", () => {
  it("retains carrier, pickup, phone and saved currency without reading the list", async () => {
    const list = vi.spyOn(api, "orders"); show();
    expect(await screen.findByText(/Carried by packeta/)).toBeTruthy();
    expect(screen.getByText(/Collection at Z-BOX Hlavní nádraží/)).toBeTruthy();
    expect(screen.getByText(/Telephone \+420 777 123 456/)).toBeTruthy();
    expect(screen.getByText("€149.00")).toBeTruthy();
    expect(list).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
  });

  it("uses the saved line total and keeps duplicate product names separate", async () => {
    const warn = vi.spyOn(console, "error").mockImplementation(() => undefined);
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, pickupPoint: null, lines: [
      { ...orderDetail.lines[0], lineTotal: 89.99 }, { ...orderDetail.lines[0], quantity: 1, lineTotal: 45 },
    ] });
    show(); await screen.findByText(/To the delivery address below/);
    expect(screen.getAllByText("Chair")).toHaveLength(2);
    expect(screen.getByText("€89.99")).toBeTruthy();
    expect(warn).not.toHaveBeenCalled();
  });

  it.each(["Started", "Pending", "Authorized", "Unrecognised"])("does not offer manual confirmation for an open %s gateway attempt", async (status) => {
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, paymentAttempts: [{ reference: "gateway-ref", status }] });
    show(true); await screen.findByText(/Manual payment confirmation is unavailable/);
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeTruthy();
  });

  it("serializes existing management actions and waits for a fresh read after an uncertain write", async () => {
    const user = userEvent.setup();
    let reject!: (reason: unknown) => void;
    const write = new Promise<AdminOrderDetail>((_, no) => { reject = no; });
    vi.spyOn(api, "confirmOrderPayment").mockReturnValue(write);
    show(true); await screen.findByRole("button", { name: "Mark as paid" });
    vi.mocked(api.order).mockRejectedValueOnce(new HttpError("http", 503));
    await user.dblClick(screen.getByRole("button", { name: "Mark as paid" }));
    expect(api.confirmOrderPayment).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Cancel" })).toHaveProperty("disabled", true);
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
    expect(screen.queryByText("€149.00")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("€149.00")).toBeTruthy();
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
    await screen.findByRole("alert");
    await waitFor(() => expect(tracking).toHaveProperty("disabled", false));
    expect(tracking).toHaveProperty("value", "  TRACK-123  ");
    expect(api.createShipment).toHaveBeenCalledWith("store-a", orderDetail.number, "TRACK-123");
    vi.mocked(api.order).mockResolvedValue({ ...orderDetail, status: "Refunded" });
    await user.click(screen.getByRole("button", { name: "Refund" }));
    await screen.findByText("Credited");
    expect(api.refundOrder).toHaveBeenCalledWith("store-a", orderDetail.number);
    expect(screen.queryByRole("button", { name: "Ship" })).toBeNull();
  });
});
