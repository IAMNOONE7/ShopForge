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
import { api, type AdminOrder } from "../api";
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

  it("shows management actions to an allowed role", async () => {
    vi.spyOn(api, "orders").mockResolvedValue([order]);

    render(<OrdersSection storeId="store-a" canManage />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Mark as paid" })).toBeTruthy(),
    );
    expect(screen.getByRole("button", { name: "Cancel" })).toBeTruthy();
  });
});
