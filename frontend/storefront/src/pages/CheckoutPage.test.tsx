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
import { useState, type ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router";
import type { Customer } from "../account";
import { HttpError } from "../api/http";
import type { Cart, CheckoutMethods, PickupPoint } from "../cart";
import { CartContext, type CartState } from "../cartContext";
import {
  CustomerContext,
  type CustomerState,
} from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { CheckoutPage } from "./CheckoutPage";

const mocks = vi.hoisted(() => ({
  getCart: vi.fn(),
  getCheckoutMethods: vi.fn(),
  getPickupPoints: vi.fn(),
  placeOrder: vi.fn(),
  reloadCart: vi.fn(),
}));

vi.mock("../cart", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../cart")>()),
  getCart: mocks.getCart,
  getCheckoutMethods: mocks.getCheckoutMethods,
  getPickupPoints: mocks.getPickupPoints,
  placeOrder: mocks.placeOrder,
}));

const store: Store = {
  id: "store",
  name: "Store",
  currency: "EUR",
  culture: "en-IE",
  logoUrl: null,
  theme: {
    primaryColor: "#000000",
    secondaryColor: "#ffffff",
    borderRadius: 4,
  },
};
const cart: Cart = {
  items: [
    {
      storeProductId: "oak",
      variantId: "oak-variant",
      name: "Oak chair",
      slug: "oak-chair",
      unitPrice: 120,
      quantity: 1,
      lineTotal: 120,
      available: 8,
      imageUrl: null,
    },
  ],
  count: 1,
  itemsTotal: 120,
  vatTotal: 20,
  changed: false,
  discount: null,
  discountProblem: null,
};
const methods: CheckoutMethods = {
  paymentMethods: [{ code: "cash", name: "Cash" }],
  shippingMethods: [
    {
      code: "courier",
      name: "Courier",
      price: 10,
      requiresPickupPoint: false,
    },
    {
      code: "pickup",
      name: "Pickup",
      price: 4,
      requiresPickupPoint: true,
    },
  ],
};
const customer: Customer = {
  email: "ada@example.com",
  firstName: "Ada",
  lastName: "Lovelace",
  phone: null,
};

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});

afterEach(() => {
  cleanup();
  window.sessionStorage.clear();
  for (const mock of Object.values(mocks)) mock.mockReset();
});

afterAll(() => i18n.changeLanguage("en"));

function Contexts({
  children,
  authenticated = false,
}: {
  children: ReactNode;
  authenticated?: boolean;
}) {
  const [currentCart, setCurrentCart] = useState(cart);
  const [adjusted, setAdjusted] = useState(false);
  const cartValue: CartState = {
    status: "ready",
    cart: currentCart,
    error: null,
    pending: false,
    adjusted,
    acknowledgeAdjustment: () => setAdjusted(false),
    apply: setCurrentCart,
    reload: mocks.reloadCart,
    mutate: vi.fn(),
  };
  const customerValue: CustomerState = {
    status: authenticated ? "authenticated" : "guest",
    customer: authenticated ? customer : null,
    error: null,
    apply: vi.fn(),
    retry: vi.fn(),
  };

  return (
    <StoreContext value={store}>
      <CustomerContext value={customerValue}>
        <CartContext value={cartValue}>{children}</CartContext>
      </CustomerContext>
    </StoreContext>
  );
}

function renderCheckout(options: { authenticated?: boolean } = {}) {
  return render(
    <MemoryRouter initialEntries={["/checkout"]}>
      <Contexts authenticated={options.authenticated}>
        <Routes>
          <Route path="/checkout" element={<CheckoutPage />} />
          <Route path="/order/:number" element={<p>Order destination</p>} />
        </Routes>
      </Contexts>
    </MemoryRouter>,
  );
}

async function fillGuestAddress(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole("textbox", { name: "E-mail" }), "guest@example.com");
  await user.type(screen.getByRole("textbox", { name: "Full name" }), "Guest Buyer");
  await user.type(screen.getByRole("textbox", { name: "Street and number" }), "1 Main Street");
  await user.type(screen.getByRole("textbox", { name: "City" }), "Dublin");
  await user.type(screen.getByRole("textbox", { name: "Postal code" }), "D01");
  await user.type(screen.getByRole("textbox", { name: /^Country/ }), "IE");
}

describe("CheckoutPage", () => {
  it("keeps a failed guest draft and serializes rapid order attempts", async () => {
    const user = userEvent.setup();
    const request = deferred<never>();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    mocks.placeOrder.mockReturnValue(request.promise);
    renderCheckout();

    await screen.findByRole("heading", { name: "Checkout" });
    await fillGuestAddress(user);
    const submit = screen.getByRole("button", { name: "Place order" });
    await user.click(submit);
    submit.click();
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);

    request.reject(new HttpError("http", 503));
    expect(
      await screen.findByText(
        "No order was retried automatically. Review the form and place it again when you are ready.",
      ),
    ).toBeTruthy();
    expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveProperty(
      "value",
      "guest@example.com",
    );
    expect(screen.getByRole("textbox", { name: "Street and number" })).toHaveProperty(
      "value",
      "1 Main Street",
    );
  });

  it("discards stale pickup responses and clears the previous choice", async () => {
    const user = userEvent.setup();
    const oldRequest = deferred<PickupPoint[]>();
    const newRequest = deferred<PickupPoint[]>();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    mocks.getPickupPoints
      .mockReturnValueOnce(oldRequest.promise)
      .mockReturnValueOnce(newRequest.promise)
      .mockResolvedValue([
        {
          code: "new",
          name: "New point",
          line1: "2 Main Street",
          city: "Dublin",
          postalCode: "D02",
          country: "IE",
        },
      ]);
    renderCheckout();

    await screen.findByRole("heading", { name: "Checkout" });
    await user.click(screen.getByRole("radio", { name: /Pickup/ }));
    expect(await screen.findByText("Loading pickup points…")).toBeTruthy();
    await user.click(screen.getByRole("radio", { name: /Courier/ }));
    await user.click(screen.getByRole("radio", { name: /Pickup/ }));

    newRequest.resolve([
      {
        code: "new",
        name: "New point",
        line1: "2 Main Street",
        city: "Dublin",
        postalCode: "D02",
        country: "IE",
      },
    ]);
    const select = await screen.findByRole("combobox", { name: "Pickup point" });
    await user.selectOptions(select, "new");
    expect(select).toHaveProperty("value", "new");

    oldRequest.resolve([
      {
        code: "old",
        name: "Old point",
        line1: "1 Old Road",
        city: "Cork",
        postalCode: "C01",
        country: "IE",
      },
    ]);
    await waitFor(() =>
      expect(screen.queryByRole("option", { name: /Old point/ })).toBeNull(),
    );

    await user.click(screen.getByRole("radio", { name: /Courier/ }));
    await user.click(screen.getByRole("radio", { name: /Pickup/ }));
    const freshSelect = await screen.findByRole("combobox", {
      name: "Pickup point",
    });
    await waitFor(() => expect(freshSelect).toHaveProperty("value", ""));
  });

  it("requires review after a conflict and saves recovery before navigation", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    mocks.getCart.mockResolvedValue({
      ...cart,
      itemsTotal: 125,
      items: [{ ...cart.items[0], lineTotal: 125 }],
    });
    mocks.placeOrder
      .mockRejectedValueOnce(new HttpError("http", 409))
      .mockResolvedValueOnce({
        number: "2026-0001",
        token: "11111111-1111-1111-1111-111111111111",
        paymentInstructions: "Pay on delivery.",
        redirectUrl: null,
      });
    renderCheckout();

    await screen.findByRole("heading", { name: "Checkout" });
    await fillGuestAddress(user);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    expect(
      await screen.findByText("Review the updated order"),
    ).toBeTruthy();
    await waitFor(() => expect(mocks.getCart).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveProperty(
      "value",
      "guest@example.com",
    );

    const placeAgain = screen.getByRole("button", { name: "Place order" });
    await user.click(placeAgain);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);

    const review = screen.getByRole("button", {
      name: "I reviewed these changes",
    });
    await waitFor(() => expect(review).toHaveProperty("disabled", false));
    await user.click(review);
    await user.click(screen.getByRole("button", { name: "Place order" }));

    expect(await screen.findByText("Order destination")).toBeTruthy();
    expect(mocks.placeOrder).toHaveBeenCalledTimes(2);
    expect(mocks.reloadCart).toHaveBeenCalledTimes(1);
    expect(
      JSON.parse(
        window.sessionStorage.getItem("shopforge.checkout.recovery") ?? "{}",
      ),
    ).toEqual({
      orderPath:
        "/order/2026-0001?token=11111111-1111-1111-1111-111111111111",
    });
  });

  it("blocks checkout with a clear explanation when no methods exist", async () => {
    mocks.getCheckoutMethods.mockResolvedValue({
      paymentMethods: [],
      shippingMethods: [],
    });
    renderCheckout();

    expect(
      await screen.findByText(
        "This store is not taking orders at the moment.",
      ),
    ).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Place order" })).toBeNull();
  });

  it("prefills a signed-in customer and exposes a separate shipping address", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    renderCheckout({ authenticated: true });

    const email = await screen.findByRole("textbox", { name: "E-mail" });
    expect(email).toHaveProperty("value", "ada@example.com");
    expect(email).toHaveProperty("readOnly", true);
    expect(screen.getByRole("textbox", { name: "Full name" })).toHaveProperty(
      "value",
      "Ada Lovelace",
    );

    await user.click(
      screen.getByRole("checkbox", { name: "Ship to a different address" }),
    );
    expect(screen.getAllByRole("textbox", { name: "Full name" })).toHaveLength(2);
  });

  it("focuses a linked validation summary and blocks an incomplete order", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    renderCheckout();

    const submit = await screen.findByRole("button", { name: "Place order" });
    expect(submit.getAttribute("aria-describedby")).toBe(
      "checkout-submit-hint",
    );
    await user.click(submit);

    const summary = await screen.findByRole("alert");
    await waitFor(() => expect(document.activeElement).toBe(summary));
    const emailIssue = screen.getByRole("button", {
      name: "Enter a valid e-mail address.",
    });
    await user.click(emailIssue);
    expect(document.activeElement).toBe(
      screen.getByRole("textbox", { name: "E-mail" }),
    );
    expect(mocks.placeOrder).not.toHaveBeenCalled();
  });

  it("shows a distinct empty pickup state and keeps checkout blocked", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    mocks.getPickupPoints.mockResolvedValue([]);
    renderCheckout();

    await screen.findByRole("heading", { name: "Checkout" });
    await user.click(screen.getByRole("radio", { name: /Pickup/ }));

    expect(
      await screen.findByText("No pickup point is currently available for this method. Choose another shipping method."),
    ).toBeTruthy();
    expect(screen.queryByRole("combobox", { name: "Pickup point" })).toBeNull();
    expect(
      screen
        .getByRole("button", { name: "Place order" })
        .getAttribute("aria-disabled"),
    ).toBe("true");
  });
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((accept, fail) => {
    resolve = accept;
    reject = fail;
  });
  return { promise, resolve, reject };
}
