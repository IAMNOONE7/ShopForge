// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router";
import { HttpError } from "../api/http";
import type { Order } from "../cart";
import { rememberCheckout } from "../checkoutRecovery";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { OrderPage } from "./OrderPage";

const mocks = vi.hoisted(() => ({
  getOrder: vi.fn(),
  downloadGuestDocument: vi.fn(),
}));
vi.mock("../cart", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../cart")>()),
  getOrder: mocks.getOrder,
}));
vi.mock("../api/documents", () => ({
  downloadGuestDocument: mocks.downloadGuestDocument,
}));

const token = "01a0ddab-3a87-70e9-8b84-6513eff79718";
const store: Store = {
  id: "store",
  name: "Store",
  currency: "CZK",
  culture: "en-IE",
  logoUrl: null,
  providerKeys: [],
  theme: { primaryColor: "#000000", secondaryColor: "#ffffff", borderRadius: 4 },
};

function makeOrder(overrides: Partial<Order> = {}): Order {
  return {
    number: "2026-1",
    placedAt: "2026-09-26T10:00:00Z",
    status: "AwaitingPayment",
    email: "customer@example.test",
    currency: "EUR",
    paymentMethod: "Bank transfer",
    shippingMethod: "Courier",
    shippingPrice: 10,
    itemsTotal: 110,
    vatTotal: 20,
    grandTotal: 120,
    discount: null,
    pickupPoint: null,
    shipment: null,
    documents: [],
    lines: [
      {
        productName: "Oak chair",
        unitPrice: 120,
        vatRate: 20,
        quantity: 1,
        lineTotal: 110,
      },
    ],
    ...overrides,
  };
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  mocks.getOrder.mockReset();
  mocks.downloadGuestDocument.mockReset();
  window.sessionStorage.clear();
});
afterAll(() => i18n.changeLanguage("en"));

function renderPage(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <StoreContext value={store}>
        <Routes>
          <Route path="/order/:number" element={<OrderPage />} />
        </Routes>
      </StoreContext>
    </MemoryRouter>,
  );
}

describe("OrderPage", () => {
  it("shows local guidance without a request for a malformed guest token", () => {
    renderPage("/order/2026-1?token=not-a-token");

    expect(
      screen.getByRole("heading", { name: "The order link is incomplete" }),
    ).toBeTruthy();
    expect(screen.getByRole("link", { name: "Browse all products" })).toBeTruthy();
    expect(mocks.getOrder).not.toHaveBeenCalled();
  });

  it("supports a direct awaiting-payment link and clears matching recovery after loading", async () => {
    rememberCheckout({
      number: "2026-1",
      token,
      paymentInstructions: "",
      redirectUrl: null,
    });
    mocks.getOrder.mockResolvedValue(makeOrder());
    renderPage(`/order/2026-1?token=${token}`);

    expect(
      await screen.findByText(
        "The order exists and is waiting for payment confirmation.",
      ),
    ).toBeTruthy();
    expect(screen.queryByText("Payment instructions")).toBeNull();
    await waitFor(() =>
      expect(window.sessionStorage.getItem("shopforge.checkout.recovery")).toBeNull(),
    );
  });

  it("uses immutable response totals and only links safe tracking URLs", async () => {
    const user = userEvent.setup();
    const invoice = {
      number: "INV-1",
      kind: "Invoice",
      issuedAt: "2026-09-26T10:05:00Z",
    };
    mocks.getOrder.mockResolvedValue(
      makeOrder({
        status: "Shipped",
        shipment: {
          carrier: "Parcel Express",
          trackingNumber: "TRACK-1",
          trackingUrl: "javascript:alert(1)",
        },
        documents: [invoice],
      }),
    );
    mocks.downloadGuestDocument.mockResolvedValue(undefined);
    renderPage(`/order/2026-1?token=${token}`);

    expect(await screen.findByText("The order has been shipped.")).toBeTruthy();
    expect(screen.getAllByText("€110.00").length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: /Track shipment/ })).toBeNull();
    expect(screen.getByText(/TRACK-1/)).toBeTruthy();

    await user.click(
      screen.getByRole("button", { name: "Download Invoice INV-1 (PDF)" }),
    );
    expect(mocks.downloadGuestDocument).toHaveBeenCalledWith(
      "2026-1",
      "INV-1",
      token,
    );
  });

  it("treats a wrong token or store as an unavailable order", async () => {
    mocks.getOrder.mockRejectedValue(new HttpError("http", 404));
    renderPage(`/order/2026-1?token=${token}`);

    expect(
      await screen.findByRole("heading", { name: "Order not found" }),
    ).toBeTruthy();
    expect(
      screen.getByText(
        "This order is unavailable here. Check the complete link from your confirmation e-mail.",
      ),
    ).toBeTruthy();
  });
});
