// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router";
import type { Customer } from "../account";
import { HttpError } from "../api/http";
import type { Order } from "../cart";
import { CustomerContext, type CustomerState } from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { AccountOrderPage } from "./AccountOrderPage";
import { AccountPage } from "./AccountPage";

const mocks = vi.hoisted(() => ({
  getOrders: vi.fn(),
  getAccountOrder: vi.fn(),
  updateProfile: vi.fn(),
  signOut: vi.fn(),
  downloadAccountDocument: vi.fn(),
}));

vi.mock("../account", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../account")>()),
  getOrders: mocks.getOrders,
  getAccountOrder: mocks.getAccountOrder,
  updateProfile: mocks.updateProfile,
  signOut: mocks.signOut,
}));

vi.mock("../api/documents", () => ({
  downloadAccountDocument: mocks.downloadAccountDocument,
}));

const store: Store = {
  id: "store-a",
  name: "Wooden Home",
  currency: "CZK",
  culture: "en-IE",
  logoUrl: null,
  theme: {
    primaryColor: "#234567",
    secondaryColor: "#f3eee8",
    borderRadius: 8,
  },
};

const customer: Customer = {
  email: "ada@example.test",
  firstName: "Ada",
  lastName: "Lovelace",
  phone: "+420 123 456",
};

const apply = vi.fn();
const retry = vi.fn();

function customerState(
  state: Partial<CustomerState> & Pick<CustomerState, "status">,
): CustomerState {
  return {
    customer: null,
    error: null,
    apply,
    retry,
    ...state,
  };
}

function renderAccount(state: CustomerState, path = "/account") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <StoreContext value={store}>
        <CustomerContext value={state}>
          <Routes>
            <Route path="/" element={<h1>Catalog</h1>} />
            <Route path="/account" element={<AccountPage />} />
            <Route
              path="/account/orders/:number"
              element={<AccountOrderPage />}
            />
          </Routes>
        </CustomerContext>
      </StoreContext>
    </MemoryRouter>,
  );
}

function makeOrder(): Order {
  return {
    number: "2026-00023",
    placedAt: "2026-09-28T10:00:00Z",
    status: "Paid",
    email: customer.email,
    currency: "EUR",
    paymentMethod: "Bank transfer",
    shippingMethod: "Personal pickup",
    shippingPrice: 0,
    itemsTotal: 100,
    vatTotal: 20,
    grandTotal: 100,
    discount: null,
    pickupPoint: "Prague showroom",
    shipment: null,
    documents: [
      {
        number: "INV-2026-00012",
        kind: "Invoice",
        issuedAt: "2026-09-28T10:02:00Z",
      },
    ],
    lines: [
      {
        productName: "Beech side table",
        unitPrice: 100,
        vatRate: 20,
        quantity: 1,
        lineTotal: 100,
      },
    ],
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((accept, decline) => {
    resolve = accept;
    reject = decline;
  });
  return { promise, resolve, reject };
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});

beforeEach(() => {
  apply.mockReset();
  retry.mockReset();
  Object.values(mocks).forEach((mock) => mock.mockReset());
});

afterEach(cleanup);
afterAll(() => i18n.changeLanguage("en"));

describe("account profile and history", () => {
  it("does not start private reads while the session is unresolved or anonymous", () => {
    renderAccount(customerState({ status: "checking" }));
    expect(screen.getByText("Loading your account…")).toBeTruthy();
    expect(mocks.getOrders).not.toHaveBeenCalled();

    cleanup();
    renderAccount(customerState({ status: "guest" }), "/account/orders/2026-00023");
    expect(screen.getByRole("heading", { name: "Sign in to view this order" })).toBeTruthy();
    expect(
      screen.getByRole("link", { name: "Sign in" }).getAttribute("href"),
    ).toContain("returnTo=%2Faccount%2Forders%2F2026-00023");
    expect(mocks.getAccountOrder).not.toHaveBeenCalled();
  });

  it("shows all summary facts without assigning the store currency to history", async () => {
    mocks.getOrders.mockResolvedValue([
      {
        number: "2026-00023",
        placedAt: "2026-09-28T10:00:00Z",
        status: "Refunded",
        grandTotal: 98765.43,
        items: 2,
      },
    ]);
    renderAccount(
      customerState({
        status: "authenticated",
        customer,
      }),
    );

    expect(await screen.findByText("Refunded")).toBeTruthy();
    expect(screen.getByText("2 items")).toBeTruthy();
    expect(
      screen.getByRole("link", { name: "View order 2026-00023" }),
    ).toBeTruthy();
    expect(document.body.textContent).not.toContain("98,765");
    expect(document.body.textContent).not.toContain("Kč");
    expect(screen.queryByText("Your wishlist")).toBeNull();
  });

  it("distinguishes a failed history load from an empty history and retries", async () => {
    mocks.getOrders
      .mockRejectedValueOnce(new HttpError("network", null))
      .mockResolvedValueOnce([]);
    const user = userEvent.setup();
    renderAccount(
      customerState({
        status: "authenticated",
        customer,
      }),
    );

    expect(
      await screen.findByText(
        "The server could not be reached. Check your connection and try again.",
      ),
    ).toBeTruthy();
    expect(screen.queryByText("You have no orders with this store yet.")).toBeNull();

    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(
      await screen.findByText("You have no orders with this store yet."),
    ).toBeTruthy();
    expect(screen.getByRole("link", { name: "All products" })).toBeTruthy();
  });

  it("preserves the profile draft and keeps e-mail read-only after failure", async () => {
    mocks.getOrders.mockResolvedValue([]);
    mocks.updateProfile.mockRejectedValue(new HttpError("network", null));
    const user = userEvent.setup();
    renderAccount(
      customerState({
        status: "authenticated",
        customer,
      }),
    );

    const firstName = await screen.findByRole("textbox", {
      name: "First name",
    });
    const phone = screen.getByRole("textbox", { name: "Phone (optional)" });
    const email = screen.getByRole("textbox", { name: "E-mail" });
    await user.clear(firstName);
    await user.type(firstName, "  Augusta  ");
    await user.clear(phone);
    await user.type(phone, "  +44 20 1234  ");
    await user.click(screen.getByRole("button", { name: "Save profile" }));

    expect(await screen.findByText(/server could not be reached/i)).toBeTruthy();
    expect(firstName).toHaveProperty("value", "  Augusta  ");
    expect(phone).toHaveProperty("value", "  +44 20 1234  ");
    expect(email).toHaveProperty("readOnly", true);
    expect(mocks.updateProfile).toHaveBeenCalledWith({
      firstName: "Augusta",
      lastName: "Lovelace",
      phone: "+44 20 1234",
    });
  });

  it("focuses client validation and serializes a successful profile save", async () => {
    mocks.getOrders.mockResolvedValue([]);
    const pending = deferred<Customer>();
    mocks.updateProfile.mockReturnValue(pending.promise);
    const user = userEvent.setup();
    renderAccount(
      customerState({
        status: "authenticated",
        customer,
      }),
    );

    const firstName = await screen.findByRole("textbox", {
      name: "First name",
    });
    await user.clear(firstName);
    await user.click(screen.getByRole("button", { name: "Save profile" }));
    expect(document.activeElement).toBe(screen.getByRole("alert"));
    expect(mocks.updateProfile).not.toHaveBeenCalled();

    await user.type(firstName, "Augusta");
    const save = screen.getByRole("button", { name: "Save profile" });
    fireEvent.click(save);
    fireEvent.click(save);
    expect(mocks.updateProfile).toHaveBeenCalledTimes(1);

    const updated = { ...customer, firstName: "Augusta" };
    pending.resolve(updated);
    expect(await screen.findByText("Your profile was saved.")).toBeTruthy();
    expect(apply).toHaveBeenCalledWith(updated);
    expect(document.activeElement).toBe(firstName);
  });

  it("clears the account view only after sign-out succeeds", async () => {
    mocks.getOrders.mockResolvedValue([]);
    mocks.signOut.mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderAccount(
      customerState({
        status: "authenticated",
        customer,
      }),
    );

    await screen.findByText("You have no orders with this store yet.");
    await user.click(screen.getByRole("button", { name: "Sign out" }));
    expect(await screen.findByRole("heading", { name: "Catalog" })).toBeTruthy();
    expect(apply).toHaveBeenCalledWith(null);
    expect(screen.queryByText("ada@example.test")).toBeNull();
  });
});

describe("authenticated order detail", () => {
  it("reloads without a guest token and downloads through the account endpoint", async () => {
    mocks.getAccountOrder.mockResolvedValue(makeOrder());
    mocks.downloadAccountDocument.mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderAccount(
      customerState({
        status: "authenticated",
        customer,
      }),
      "/account/orders/2026-00023",
    );

    expect(
      await screen.findByRole("heading", { name: "Order 2026-00023" }),
    ).toBeTruthy();
    expect(mocks.getAccountOrder).toHaveBeenCalledWith(
      "2026-00023",
      expect.any(AbortSignal),
    );
    await user.click(
      screen.getByRole("button", {
        name: "Download Invoice INV-2026-00012 (PDF)",
      }),
    );
    expect(mocks.downloadAccountDocument).toHaveBeenCalledWith(
      "2026-00023",
      "INV-2026-00012",
    );
    expect(screen.queryByText(/send something back/i)).toBeNull();
  });

  it("keeps a foreign or missing order private", async () => {
    mocks.getAccountOrder.mockRejectedValue(new HttpError("http", 404));
    renderAccount(
      customerState({
        status: "authenticated",
        customer,
      }),
      "/account/orders/other-store-order",
    );

    expect(
      await screen.findByRole("heading", { name: "Order not found" }),
    ).toBeTruthy();
    expect(
      screen.getByText("This order is not one of yours."),
    ).toBeTruthy();
  });
});
