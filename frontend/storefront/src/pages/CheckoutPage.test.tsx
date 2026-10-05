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
  providerKeys: [{ provider: "packeta", key: "widget-key" }],
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
      optionValues: [],
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
      pickupPointChoice: "list",
    },
    {
      code: "pickup",
      name: "Pickup",
      price: 4,
      requiresPickupPoint: true,
      pickupPointChoice: "list",
    },
    {
      code: "z-box",
      name: "Z-BOX",
      price: 6,
      requiresPickupPoint: true,
      pickupPointChoice: "carrier-map",
    },
  ],
};
const customer: Customer = {
  email: "ada@example.com",
  firstName: "Ada",
  lastName: "Lovelace",
  phone: "+420 777 888 999",
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
  await user.type(screen.getByRole("textbox", { name: "Telephone number" }), "+420123456789");
  await user.type(screen.getByRole("textbox", { name: "Full name" }), "Guest Buyer");
  await user.type(screen.getByRole("textbox", { name: "Street and number" }), "1 Main Street");
  await user.type(screen.getByRole("textbox", { name: "City" }), "Dublin");
  await user.type(screen.getByRole("textbox", { name: "Postal code" }), "D01");
  await user.type(screen.getByRole("textbox", { name: /^Country/ }), "IE");
}

describe("CheckoutPage", () => {
  it("replays an unchanged checkout after a lost response with the same key and body", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    mocks.placeOrder
      .mockRejectedValueOnce(new HttpError("network", null))
      .mockResolvedValueOnce({
        number: "2026-0002", token: "11111111-1111-1111-1111-111111111111",
        paymentInstructions: "Pay on delivery.", redirectUrl: null,
      });
    renderCheckout();
    await screen.findByRole("heading", { name: "Checkout" });
    await fillGuestAddress(user);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    expect(await screen.findByText(/Submitting these unchanged details again uses the same request key/)).toBeTruthy();
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    const [firstBody, firstKey] = mocks.placeOrder.mock.calls[0];
    expect(firstKey).toMatch(/^sf-[a-f0-9]{32}$/);

    await user.click(screen.getByRole("button", { name: "Place order" }));
    expect(await screen.findByText("Order destination")).toBeTruthy();
    expect(mocks.placeOrder.mock.calls[1][0]).toBe(firstBody);
    expect(mocks.placeOrder.mock.calls[1][1]).toBe(firstKey);
  });

  it("requires explicit review before a changed draft starts a new key", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    mocks.placeOrder
      .mockRejectedValueOnce(new HttpError("network", null))
      .mockRejectedValueOnce(new HttpError("http", 400));
    renderCheckout();
    await screen.findByRole("heading", { name: "Checkout" });
    await fillGuestAddress(user);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await screen.findByText(/same request key/);
    const firstKey = mocks.placeOrder.mock.calls[0][1];

    const phone = screen.getByRole("textbox", { name: "Telephone number" });
    await user.clear(phone);
    await user.type(phone, "+420987654321");
    expect(screen.getByText("The previous order may have been placed")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Place order" }));
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Start a new attempt" }));
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][1]).not.toBe(firstKey);
    expect(mocks.placeOrder.mock.calls[1][0].phone).toBe("+420987654321");
  });

  it("keeps the same request for an in-progress 409 and reviews a mismatched 422", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    mocks.placeOrder
      .mockRejectedValueOnce(new HttpError("http", 409, { title: "A request with this key is still in progress" }))
      .mockRejectedValueOnce(new HttpError("http", 422))
      .mockRejectedValueOnce(new HttpError("http", 400));
    renderCheckout();
    await screen.findByRole("heading", { name: "Checkout" });
    await fillGuestAddress(user);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    expect(await screen.findByText("Your request is still processing")).toBeTruthy();
    expect(mocks.getCart).not.toHaveBeenCalled();
    const firstKey = mocks.placeOrder.mock.calls[0][1];

    await user.click(screen.getByRole("button", { name: "I checked; allow the same retry" }));
    await user.click(screen.getByRole("button", { name: "Place order" }));
    expect(await screen.findByText("Review this order before retrying")).toBeTruthy();
    expect(mocks.placeOrder.mock.calls[1][1]).toBe(firstKey);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    expect(mocks.placeOrder).toHaveBeenCalledTimes(2);
    await user.click(screen.getByRole("button", { name: "Start a new attempt" }));
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(3));
    expect(mocks.placeOrder.mock.calls[2][1]).not.toBe(firstKey);
  });

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
        "No order was retried automatically. Submitting these unchanged details again uses the same request key.",
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
    expect(mocks.placeOrder.mock.calls[1][1]).not.toBe(mocks.placeOrder.mock.calls[0][1]);
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

  // The carrier needs somebody to ring, so the order cannot be placed without one (D-144). A shopper who has
  // already told the shop their number should not have to type it again.
  it("requires a telephone number and offers the one on the account", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    renderCheckout();

    const phone = await screen.findByRole("textbox", {
      name: "Telephone number",
    });
    await fillGuestAddress(user);
    await user.clear(phone);
    await user.type(phone, "call the office");
    await user.click(screen.getByRole("button", { name: "Place order" }));

    await screen.findByRole("button", {
      name: "Enter a telephone number the carrier can call.",
    });
    expect(mocks.placeOrder).not.toHaveBeenCalled();
  });

  it("carries the telephone number of a signed-in shopper", async () => {
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    renderCheckout({ authenticated: true });

    const phone = await screen.findByRole("textbox", {
      name: "Telephone number",
    });
    expect(phone).toHaveProperty("value", "+420 777 888 999");
  });

  // The map is the carrier's, so the page has no list to check the answer against: it only insists that one
  // was given, and the server asks the carrier whether it is real.
  it("will not order a map method until a point has been chosen", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    renderCheckout();

    await screen.findByRole("button", { name: "Place order" });
    await fillGuestAddress(user);
    await user.click(screen.getByRole("radio", { name: /Z-BOX/ }));
    await user.click(screen.getByRole("button", { name: "Place order" }));

    await screen.findByRole("button", { name: "Choose an available pickup point." });
    expect(mocks.placeOrder).not.toHaveBeenCalled();
  });

  it("sends the point the carrier's map gave back, and nothing else about it", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods.mockResolvedValue(methods);
    mocks.placeOrder.mockResolvedValue({ number: "2026-00001", token: "t", paymentInstructions: null, redirectUrl: null });
    (window as { Packeta?: unknown }).Packeta = {
      Widget: {
        pick: (_key: string, callback: (point: { id: string; name: string }) => void) =>
          callback({ id: "4321", name: "Z-BOX Hlavní nádraží" }),
      },
    };

    try {
      renderCheckout();

      await screen.findByRole("button", { name: "Place order" });
      await fillGuestAddress(user);
      await user.click(screen.getByRole("radio", { name: /Z-BOX/ }));
      await user.click(screen.getByRole("button", { name: "Choose a pickup point" }));
      await user.click(screen.getByRole("button", { name: "Place order" }));

      await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(1));
      expect(mocks.placeOrder.mock.calls[0][0]).toMatchObject({
        shippingMethodCode: "z-box",
        pickupPointCode: "4321",
      });
    } finally {
      delete (window as { Packeta?: unknown }).Packeta;
    }
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
