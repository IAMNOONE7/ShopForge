// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import {
  afterAll,
  afterEach,
  beforeAll,
  describe,
  expect,
  it,
  vi,
} from "vitest";
import { api, type AdminOrder, type AdminOrderDetail } from "../api";
import { i18n, initializeI18n } from "../i18n";
import { OrdersSection } from "./OrdersSection";

const order: AdminOrder = {
  number: "ORD-1001",
  placedAt: "2026-09-25T12:00:00Z",
  status: "AwaitingPayment",
  email: "customer@example.com",
  hasAccount: true,
  grandTotal: 149,
  items: 1,
};

const address = {
  fullName: "Alex Buyer",
  line1: "Wilsonova 8",
  line2: null,
  city: "Praha",
  postalCode: "110 00",
  country: "CZ",
};

const detail: AdminOrderDetail = {
  number: "ORD-1001",
  placedAt: "2026-09-25T12:00:00Z",
  status: "AwaitingPayment",
  email: "customer@example.com",
  carrier: "packeta",
  phone: "+420 777 123 456",
  currency: "CZK",
  paymentMethod: "Bank transfer",
  shippingMethod: "Z-BOX",
  shippingPrice: 59,
  itemsTotal: 90,
  vatTotal: 25.86,
  grandTotal: 149,
  billingAddress: address,
  shippingAddress: address,
  discount: null,
  pickupPoint: "Z-BOX Hlavní nádraží, Wilsonova 8, Praha",
  shipment: null,
  documents: [],
  lines: [],
};

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});
afterAll(() => i18n.changeLanguage("en"));

describe("OrdersSection permissions", () => {
  it("keeps order details readable while hiding management actions", async () => {
    vi.spyOn(api, "orders").mockResolvedValue([order]);

    render(<OrdersSection storeId="store-a" canManage={false} />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Details" })).toBeTruthy(),
    );
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Cancel" })).toBeNull();
  });

  // Whoever packs the parcel should not have to work out the carrier from the method's name.
  it("names the carrier and the box the parcel is going to", async () => {
    vi.spyOn(api, "orders").mockResolvedValue([order]);
    vi.spyOn(api, "order").mockResolvedValue(detail);

    render(<OrdersSection storeId="store-a" canManage={false} />);
    (await screen.findByRole("button", { name: "Details" })).click();

    expect(await screen.findByText(/Carried by packeta/)).toBeTruthy();
    expect(screen.getByText(/Collection at Z-BOX Hlavní nádraží/)).toBeTruthy();
    expect(screen.getByText(/Telephone \+420 777 123 456/)).toBeTruthy();
  });

  it("sends a doorstep order to the address instead of a box", async () => {
    vi.spyOn(api, "orders").mockResolvedValue([order]);
    vi.spyOn(api, "order").mockResolvedValue({ ...detail, pickupPoint: null, shippingMethod: "Home delivery" });

    render(<OrdersSection storeId="store-a" canManage={false} />);
    (await screen.findByRole("button", { name: "Details" })).click();

    expect(await screen.findByText(/To the delivery address below/)).toBeTruthy();
    expect(screen.queryByText(/Collection at/)).toBeNull();
  });

  it("shows management actions to an allowed role", async () => {
    vi.spyOn(api, "orders").mockResolvedValue([order]);

    render(<OrdersSection storeId="store-a" canManage />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Mark as paid" })).toBeTruthy(),
    );
    expect(screen.getByRole("button", { name: "Cancel" })).toBeTruthy();
  });
});
