// @vitest-environment jsdom
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { HttpError } from "../../api/http";
import type { Order } from "../../cart";
import { i18n, initializeI18n } from "../../i18n";
import type { Store } from "../../store";
import { OrderReceipt } from "./OrderReceipt";

const store: Store = { id: "store", name: "Shop", culture: "en-IE", currency: "CZK", logoUrl: null, providerKeys: [],
  theme: { primaryColor: "#234567", secondaryColor: "#ffffff", borderRadius: 6 } };
const invoice = { number: "INV-1", kind: "Invoice", issuedAt: "2026-10-07T10:00:00Z" };
const creditNote = { number: "CN-1", kind: "CreditNote", issuedAt: "2026-10-07T11:00:00Z" };
const order: Order = { number: "2026-1", placedAt: "2026-10-07T09:30:00Z", status: "Refunded", email: "shopper@example.test",
  currency: "EUR", paymentMethod: "Bank transfer", shippingMethod: "Pickup", shippingPrice: 10, itemsTotal: 108, vatTotal: 18,
  grandTotal: 118, discount: { code: "SAVE", name: "Savings", amount: 12 }, pickupPoint: null, shipment: null, paidAt: null, refunds: [],
  documents: [invoice], lines: [{ productName: "Chair", quantity: 1, unitPrice: 120, lineTotal: 108, vatRate: 20 }] };
function receipt(value = order, download = vi.fn().mockResolvedValue(undefined)) {
  return <MemoryRouter><OrderReceipt order={value} store={store} heading="Order 2026-1" introduction={<p>Saved order</p>}
    refresh={{ waiting: true, refreshing: false, error: null, timedOut: false, onRefresh: vi.fn() }} downloadDocument={download} /></MemoryRouter>;
}
beforeAll(initializeI18n);
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });

describe("order receipt", () => {
  it.each(["en", "cs"])("shows each public refund separately from saved totals in %s", async language => {
    await i18n.changeLanguage(language);
    const settledAt = "2026-10-08T11:00:00Z", paidAt = "2026-10-07T09:40:00Z";
    render(receipt({ ...order, paidAt, refunds: [
      { returnNumber: "RET-1", credited: 14.5, state: "Settled", settledAt },
      { returnNumber: "RET-2", credited: 0, state: "InProgress", settledAt },
      { returnNumber: "RET-3", credited: 25, state: "ByStore", settledAt },
      { returnNumber: "RET-4", credited: 5, state: "FutureState", settledAt },
    ] }));
    const panel = screen.getByRole("region", { name: language === "en" ? "Refunds" : "Vrácení peněz" });
    expect(screen.getByText(language === "en" ? "The order has been credited. Repayment is shown separately under Refunds." : "Objednávka byla dobropisována. Stav vrácení peněz najdete v samostatném přehledu níže.")).toBeTruthy();
    expect(within(panel).getAllByRole("listitem")).toHaveLength(4);
    expect(within(panel).getByText("€14.50")).toBeTruthy();
    expect(within(panel).getByText("€0.00")).toBeTruthy();
    expect(within(panel).getByText(language === "en" ? "Refund settled" : "Vrácení peněz potvrzeno")).toBeTruthy();
    expect(within(panel).getByText(language === "en" ? "Refund in progress" : "Vrácení peněz probíhá")).toBeTruthy();
    expect(within(panel).getByText(language === "en" ? "Repayment handled by the store" : "Peníze vrací obchod")).toBeTruthy();
    expect(within(panel).getByText(language === "en" ? "Refund status unavailable" : "Stav vrácení peněz není dostupný")).toBeTruthy();
    expect(panel.querySelectorAll("time")).toHaveLength(1);
    expect(panel.querySelector("time")?.dateTime).toBe(settledAt);
    expect(document.querySelector(`.order-details time[datetime="${paidAt}"]`)).not.toBeNull();
    expect(document.querySelector(".order-grand-total dd")?.textContent).toBe("€118.00");
    expect(document.body.textContent).not.toContain("Kč");
    expect(document.body.textContent).not.toContain("FutureState");
    expect(screen.getByRole("link", { name: language === "en" ? "Refunds" : "Vrácení peněz" }).getAttribute("href")).toBe("#order-refunds-heading");
  });

  it("does not invent recorded payment or settlement times", () => {
    render(receipt({ ...order, paidAt: "invalid", refunds: [{ returnNumber: "RET-1", credited: 0, state: "Settled", settledAt: null }] }));
    expect(screen.getByText("Not recorded")).toBeTruthy();
    expect(screen.getByText("The settlement time is not recorded.")).toBeTruthy();
    expect(document.querySelector<HTMLTimeElement>(".order-details time")?.dateTime).toBe(order.placedAt);
    expect(document.querySelectorAll(".order-refunds time")).toHaveLength(0);
  });

  it("shows a partial refund while keeping the original paid order and total", () => {
    render(receipt({ ...order, status: "Paid", refunds: [{ returnNumber: "RET-PART", credited: 14.5, state: "InProgress", settledAt: null }] }));
    expect(document.querySelector(".order-status.paid")?.textContent).toBe("Paid");
    expect(screen.getByText("Refund in progress")).toBeTruthy();
    expect(screen.getByText("€14.50")).toBeTruthy();
    expect(document.querySelector(".order-grand-total dd")?.textContent).toBe("€118.00");
    expect(screen.queryByText("Refund settled")).toBeNull();
  });

  it.each(["en", "cs"])("describes a credited order without claiming refund settlement in %s", async (language) => {
    await i18n.changeLanguage(language);
    const view = render(receipt());
    const notice = language === "en"
      ? "The order has been credited. This status does not confirm that the refund has been paid."
      : "Objednávka byla dobropisována. Tento stav nepotvrzuje, že peníze byly vráceny.";
    expect(screen.getByText(language === "en" ? "Credited" : "Dobropisováno")).toBeTruthy();
    expect(screen.getByText(notice)).toBeTruthy();
    view.rerender(receipt({ ...order, documents: [invoice, creditNote] }));
    expect(screen.getByText(notice)).toBeTruthy();
    expect(screen.queryByText("This order was refunded.")).toBeNull();
    expect(screen.queryByText("Platba za tuto objednávku byla vrácena.")).toBeNull();
  });

  it("keeps an issued invoice usable while the expected credit note is preparing, then removes that guidance", async () => {
    const user = userEvent.setup();
    const download = vi.fn().mockResolvedValue(undefined);
    const view = render(receipt(order, download));
    expect(screen.getByText("The credit note is being prepared.")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Download Invoice INV-1 (PDF)" }));
    expect(download).toHaveBeenCalledWith(invoice);
    view.rerender(receipt({ ...order, documents: [invoice, creditNote] }, download));
    expect(screen.queryByText("The credit note is being prepared.")).toBeNull();
    expect(screen.getByRole("button", { name: "Download Credit note CN-1 (PDF)" })).toBeTruthy();
  });

  it("shows the actual missing invoice even when another document already exists", () => {
    render(receipt({ ...order, status: "Paid", documents: [creditNote] }));
    expect(screen.getByText("The invoice is being prepared.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Download Credit note CN-1 (PDF)" })).toBeTruthy();
  });

  it("uses saved net, shipping, VAT and order currency without deducting the discount again", async () => {
    await i18n.changeLanguage("cs");
    render(receipt({ ...order, discount: { code: "SHIP", name: "Shipping code", amount: 0 } }));
    const details = screen.getByRole("region", { name: "Podrobnosti objednávky" });
    expect(within(details).getByText("€18.00")).toBeTruthy();
    expect(document.querySelector(".order-grand-total dd")?.textContent).toBe("€118.00");
    expect(document.body.textContent).not.toContain("Kč");
    expect(screen.getByText(/Shipping code \(SHIP\) byl použit/)).toBeTruthy();
    expect(document.body.textContent).not.toContain("€0.00");
  });

  it("serializes document downloads, exposes pending state and keeps the issued document after a failure", async () => {
    const user = userEvent.setup();
    let reject!: (error: unknown) => void;
    const download = vi.fn().mockImplementationOnce(() => new Promise<void>((_, fail) => { reject = fail; })).mockResolvedValue(undefined);
    render(receipt(order, download));
    const button = screen.getByRole("button", { name: "Download Invoice INV-1 (PDF)" });
    await user.click(button);
    button.click();
    expect(download).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Preparing download…" })).toHaveProperty("disabled", true);
    reject(new HttpError("network", null));
    await waitFor(() => expect(button).toHaveProperty("disabled", false));
    expect(screen.getByText(/server could not be reached/i)).toBeTruthy();
    expect(screen.getByText("INV-1")).toBeTruthy();
    await user.click(button);
    expect(download).toHaveBeenCalledTimes(2);
    expect(screen.queryByText(/server could not be reached/i)).toBeNull();
  });
});
