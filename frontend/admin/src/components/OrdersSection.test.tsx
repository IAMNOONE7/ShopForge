// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
import { HttpError } from "../api/http";
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
const page = { items: [order], totalCount: 1, page: 1, pageSize: 50, hasMore: false };

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
afterEach(async () => {
  cleanup();
  vi.restoreAllMocks();
  await i18n.changeLanguage("en");
});
afterAll(() => i18n.changeLanguage("en"));

describe("OrdersSection permissions", () => {
  it.each(["en", "cs"])("renders the first page and its bound in %s", async (language) => {
    await i18n.changeLanguage(language);
    vi.spyOn(api, "orders").mockResolvedValue({ ...page, totalCount: 51, hasMore: true });
    render(<OrdersSection storeId="store-a" canManage={false} />);
    expect(await screen.findByText("ORD-1001")).toBeTruthy();
    expect(screen.getByText(language === "en"
      ? "Showing 1 of 51 orders (first page, up to 50)."
      : "Zobrazeno 1 z 51 objednávek (první stránka, nejvýše 50)." )).toBeTruthy();
    expect(screen.getByText(language === "en"
      ? "More orders exist. This view currently shows only the first page."
      : "Existují další objednávky. Toto zobrazení zatím ukazuje pouze první stránku." )).toBeTruthy();
  });

  it("shows a genuine empty page without a table or extra-orders message", async () => {
    vi.spyOn(api, "orders").mockResolvedValue({ ...page, items: [], totalCount: 0 });
    render(<OrdersSection storeId="store-a" canManage={false} />);
    expect(await screen.findByText("No orders yet.")).toBeTruthy();
    expect(screen.getByText("Showing 0 of 0 orders (first page, up to 50).")).toBeTruthy();
    expect(screen.queryByRole("table")).toBeNull();
    expect(screen.queryByText(/More orders exist/)).toBeNull();
  });

  it("does not describe an empty page with existing orders as an empty store", async () => {
    vi.spyOn(api, "orders").mockResolvedValue({ ...page, items: [], totalCount: 1 });
    render(<OrdersSection storeId="store-a" canManage={false} />);
    expect(await screen.findByText("No orders on this page. Refresh to check for updates.")).toBeTruthy();
    expect(screen.queryByText("No orders yet.")).toBeNull();
  });

  it("keeps rows and the page bound visible after a failed refresh", async () => {
    const user = userEvent.setup();
    const read = vi.spyOn(api, "orders").mockResolvedValueOnce(page).mockRejectedValue(new HttpError("http", 503));
    vi.spyOn(api, "confirmOrderPayment").mockResolvedValue(detail);
    render(<OrdersSection storeId="store-a" canManage />);
    await user.click(await screen.findByRole("button", { name: "Mark as paid" }));
    await screen.findByRole("alert");
    expect(read).toHaveBeenCalledTimes(2);
    expect(screen.getByText("ORD-1001")).toBeTruthy();
    expect(screen.getByText("Showing 1 of 1 orders (first page, up to 50).")).toBeTruthy();
    expect(screen.queryByText("No orders yet.")).toBeNull();
  });

  it("keeps order details readable while hiding management actions", async () => {
    vi.spyOn(api, "orders").mockResolvedValue(page);

    render(<OrdersSection storeId="store-a" canManage={false} />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Details" })).toBeTruthy(),
    );
    expect(screen.queryByRole("button", { name: "Mark as paid" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Cancel" })).toBeNull();
  });

  // Whoever packs the parcel should not have to work out the carrier from the method's name.
  it("names the carrier and the box the parcel is going to", async () => {
    vi.spyOn(api, "orders").mockResolvedValue(page);
    vi.spyOn(api, "order").mockResolvedValue(detail);

    render(<OrdersSection storeId="store-a" canManage={false} />);
    (await screen.findByRole("button", { name: "Details" })).click();

    expect(await screen.findByText(/Carried by packeta/)).toBeTruthy();
    expect(screen.getByText(/Collection at Z-BOX Hlavní nádraží/)).toBeTruthy();
    expect(screen.getByText(/Telephone \+420 777 123 456/)).toBeTruthy();
  });

  it("sends a doorstep order to the address instead of a box", async () => {
    vi.spyOn(api, "orders").mockResolvedValue(page);
    vi.spyOn(api, "order").mockResolvedValue({ ...detail, pickupPoint: null, shippingMethod: "Home delivery" });

    render(<OrdersSection storeId="store-a" canManage={false} />);
    (await screen.findByRole("button", { name: "Details" })).click();

    expect(await screen.findByText(/To the delivery address below/)).toBeTruthy();
    expect(screen.queryByText(/Collection at/)).toBeNull();
  });

  it("shows management actions to an allowed role", async () => {
    vi.spyOn(api, "orders").mockResolvedValue(page);

    render(<OrdersSection storeId="store-a" canManage />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Mark as paid" })).toBeTruthy(),
    );
    expect(screen.getByRole("button", { name: "Cancel" })).toBeTruthy();
  });
});
