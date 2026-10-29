// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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
  destinationCountry: null,
  paymentMethods: [{ code: "cash", name: "Cash" }],
  shippingMethods: [
    {
      code: "courier",
      name: "Courier",
      price: 10,
      requiresPickupPoint: false,
      pickupPointChoice: "none",
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
    {
      code: "home-delivery",
      name: "Home delivery",
      price: 8,
      requiresPickupPoint: false,
      pickupPointChoice: "none",
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
  const [currentStore, setCurrentStore] = useState(store);
  const [adjusted, setAdjusted] = useState(false);
  const [busy, setBusy] = useState(false);
  const cartValue: CartState = {
    status: "ready",
    cart: currentCart,
    error: null,
    pending: busy,
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
    <StoreContext value={currentStore}>
      <CustomerContext value={customerValue}>
        <CartContext value={cartValue}>
          <button onClick={() => setBusy(true)}>Simulate cart update</button>
          <button onClick={() => setCurrentCart({ ...cart, count: 2, items: [{ ...cart.items[0], quantity: 2, lineTotal: 240 }], itemsTotal: 240 })}>Change cart contents</button>
          <button onClick={() => setCurrentStore({ ...store, id: "other-store" })}>Change store</button>
          {children}
        </CartContext>
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
  // Typing both full addresses can exceed the default deadline on a busy runner.
  it("reviews the submitted contact and separate delivery address with exact method and edit destinations", async () => {
    const user = userEvent.setup();
    offerMethods();
    renderCheckout();
    await screen.findByRole("heading", { name: "Checkout" });
    await fillGuestAddress(user);
    await user.click(screen.getByRole("checkbox", { name: "Ship to a different address" }));
    const shipping = screen.getByRole("group", { name: "Shipping address" });
    for (const [name, value] of [["Full name", "Delivery Buyer"], ["Street and number", "2 New Road"], ["City", "Cork"], ["Postal code", "C01"], ["Country", "IE"]]) {
      await user.type(within(shipping).getByRole("textbox", { name: new RegExp("^" + name) }), value);
    }
    const review = screen.getByRole("region", { name: "Check your details" });
    expect(within(review).getByText("guest@example.com")).toBeTruthy();
    expect(within(review).getByText("Delivery Buyer")).toBeTruthy();
    expect(within(review).getByText(/2 New Road/)).toBeTruthy();
    expect(within(review).queryByText("1 Main Street")).toBeNull();
    expect(within(review).getByRole("link", { name: "Edit address" }).getAttribute("href")).toBe("#checkout-shipping-address-heading");
    expect(within(review).getByRole("link", { name: "Edit contact" }).getAttribute("href")).toBe("#checkout-contact-heading");
    expect(screen.getByText("Estimated total")).toBeTruthy();
    expect(document.querySelector(".checkout-total strong")?.textContent).toBe("€130.00");
    expect(document.querySelector(".shopping-progress [aria-current=step]")?.textContent).toContain("Checkout");
    expect(mocks.placeOrder).not.toHaveBeenCalled();
  }, 10_000);

  it("keeps checkout inactive while a cart write is unresolved", async () => {
    const user = userEvent.setup();
    offerMethods();
    renderCheckout();
    const submit = await screen.findByRole("button", { name: "Place order" });
    await fillGuestAddress(user);
    await user.click(screen.getByRole("button", { name: "Simulate cart update" }));
    expect(submit).toHaveProperty("disabled", true);
    expect(screen.getByRole("textbox", { name: "E-mail" }).matches(":disabled")).toBe(true);
    expect(screen.getByText("Updating cart…")).toBeTruthy();
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).not.toHaveBeenCalled();
  });

  it("requires a successful method refresh after a conflict even when a form submission is forced", async () => {
    const user = userEvent.setup();
    mocks.getCheckoutMethods
      .mockResolvedValueOnce(methods)
      .mockResolvedValueOnce({ ...methods, destinationCountry: "IE" })
      .mockRejectedValue(new HttpError("network", null));
    mocks.getCart.mockResolvedValue(cart);
    mocks.placeOrder.mockRejectedValue(new HttpError("http", 409));
    renderCheckout();
    await screen.findByRole("heading", { name: "Checkout" });
    await fillGuestAddress(user);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    const review = await screen.findByRole("button", { name: "I reviewed these changes" });
    await waitFor(() => expect(mocks.getCheckoutMethods).toHaveBeenCalledTimes(3));
    expect(review).toHaveProperty("disabled", true);
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveProperty("value", "guest@example.com");
  });

  it("replays an unchanged checkout after a lost response with the same key and body", async () => {
    const user = userEvent.setup();
    offerMethods();
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
    offerMethods();
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
    offerMethods();
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

  it("requires a new-attempt warning when the destination changes after an in-progress response", async () => {
    const user = userEvent.setup();
    offerMethods();
    mocks.placeOrder.mockRejectedValue(new HttpError("http", 409, { title: "A request with this key is still in progress" }));
    renderCheckout(); await fillGuestAddress(user);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await screen.findByText("Your request is still processing");
    const key = mocks.placeOrder.mock.calls[0][1];
    fireEvent.change(screen.getByRole("textbox", { name: /^Country/ }), { target: { value: "CZ" } });
    await screen.findByText("The previous order may have been placed");
    const review = await screen.findByRole("button", { name: "Start a new attempt" });
    await waitFor(() => expect(review).toHaveProperty("disabled", false));
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    await user.click(review);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][1]).not.toBe(key);
    expect(mocks.placeOrder.mock.calls[1][0].billingAddress.country).toBe("CZ");
  });

  it("keeps a failed guest draft and serializes rapid order attempts", async () => {
    const user = userEvent.setup();
    const request = deferred<never>();
    offerMethods();
    mocks.placeOrder.mockReturnValue(request.promise);
    renderCheckout();

    await screen.findByRole("heading", { name: "Checkout" });
    await fillGuestAddress(user);
    const submit = screen.getByRole("button", { name: "Place order" });
    await user.click(submit);
    submit.click();
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("textbox", { name: "E-mail" }).matches(":disabled")).toBe(true);
    expect(screen.getByRole("textbox", { name: "Street and number" }).matches(":disabled")).toBe(true);

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
    offerMethods();
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

  it("requires a fresh listed point when the store removes it during conflict recovery", async () => {
    const user = userEvent.setup();
    offerMethods();
    const point = { code: "old", name: "Old counter", line1: "Street", city: "City", postalCode: "123", country: "IE" };
    mocks.getPickupPoints.mockResolvedValue([point]);
    mocks.getCart.mockResolvedValue(cart);
    mocks.placeOrder.mockRejectedValue(new HttpError("http", 409));
    renderCheckout(); await fillGuestAddress(user);
    await user.click(screen.getByRole("radio", { name: /Pickup/ }));
    await user.selectOptions(await screen.findByRole("combobox", { name: "Pickup point" }), "old");
    mocks.getPickupPoints.mockResolvedValue([{ ...point, code: "new", name: "New counter" }]);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await screen.findByText(/Choose your pickup point again/);
    expect(await screen.findByRole("combobox", { name: "Pickup point" })).toHaveProperty("value", "");
    const review = screen.getByRole("button", { name: "I reviewed these changes" });
    await waitFor(() => expect(review).toHaveProperty("disabled", false));
    await user.click(review);
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    await user.selectOptions(screen.getByRole("combobox", { name: "Pickup point" }), "new");
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][0].pickupPointCode).toBe("new");
  });

  it("requires review after a conflict and saves recovery before navigation", async () => {
    const user = userEvent.setup();
    offerMethods();
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
      destinationCountry: null,
      paymentMethods: [],
      shippingMethods: [],
    });
    renderCheckout();

    expect(
      await screen.findByText(
        "This store is not taking orders at the moment.",
      ),
    ).toBeTruthy();
    expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBe("true");
    expect(screen.getByRole("textbox", { name: /^Country/ })).toBeTruthy();
  });

  it("prefills a signed-in customer and exposes a separate shipping address", async () => {
    const user = userEvent.setup();
    offerMethods();
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
    offerMethods();
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
    offerMethods();
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
    offerMethods();
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
    offerMethods();
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

  // A box and a doorstep from the same carrier: moving between them must not leave the other one's answer
  // behind, and the doorstep must not open anything.
  it("forgets the chosen box when the shopper asks for their door instead", async () => {
    const user = userEvent.setup();
    offerMethods();
    mocks.placeOrder.mockResolvedValue({ number: "2026-00002", token: "t", paymentInstructions: null, redirectUrl: null });
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
      expect(within(screen.getByRole("region", { name: "Check your details" })).getByText("Z-BOX Hlavní nádraží")).toBeTruthy();

      await user.click(screen.getByRole("radio", { name: /Home delivery/ }));
      expect(screen.queryByRole("button", { name: /pickup point/ })).toBeNull();
      expect(screen.queryByText("Z-BOX Hlavní nádraží")).toBeNull();

      // And back again: the box has to be chosen afresh rather than remembered.
      await user.click(screen.getByRole("radio", { name: /Z-BOX/ }));
      expect(screen.queryByText("Z-BOX Hlavní nádraží")).toBeNull();
      expect(screen.getByRole("button", { name: "Choose a pickup point" })).toBeTruthy();

      await user.click(screen.getByRole("radio", { name: /Home delivery/ }));
      await user.click(screen.getByRole("button", { name: "Place order" }));

      await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(1));
      expect(mocks.placeOrder.mock.calls[0][0]).toMatchObject({
        shippingMethodCode: "home-delivery",
        pickupPointCode: null,
      });
    } finally {
      delete (window as { Packeta?: unknown }).Packeta;
    }
  });

  it("focuses a linked validation summary and blocks an incomplete order", async () => {
    const user = userEvent.setup();
    offerMethods();
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
    offerMethods();
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
  it("uses the separate delivery country, retains drafts, and ignores edits to a non-destination billing country", async () => {
    const user = userEvent.setup();
    offerMethods();
    renderCheckout();
    await fillGuestAddress(user);
    await waitFor(() => expect(mocks.getCheckoutMethods).toHaveBeenLastCalledWith("IE", expect.any(AbortSignal)));
    await user.click(screen.getByRole("checkbox", { name: "Ship to a different address" }));
    const countries = screen.getAllByRole("textbox", { name: /^Country/ });
    fireEvent.change(countries[1], { target: { value: " sk " } });
    await waitFor(() => expect(mocks.getCheckoutMethods).toHaveBeenLastCalledWith("SK", expect.any(AbortSignal)));
    const reads = mocks.getCheckoutMethods.mock.calls.length;
    fireEvent.change(countries[0], { target: { value: "CZ" } });
    expect(mocks.getCheckoutMethods).toHaveBeenCalledTimes(reads);
    await user.click(screen.getByRole("checkbox", { name: "Ship to a different address" }));
    await waitFor(() => expect(mocks.getCheckoutMethods).toHaveBeenLastCalledWith("CZ", expect.any(AbortSignal)));
    expect(screen.getByRole("textbox", { name: "Street and number" })).toHaveProperty("value", "1 Main Street");
    await user.click(screen.getByRole("checkbox", { name: "Ship to a different address" }));
    expect(screen.getAllByRole("textbox", { name: /^Country/ })[1]).toHaveProperty("value", " sk ");
  });

  it("discards late country responses and blocks forced submission during a refresh", async () => {
    const user = userEvent.setup(), oldRead = deferred<CheckoutMethods>(), currentRead = deferred<CheckoutMethods>();
    offerMethods();
    renderCheckout();
    await fillGuestAddress(user);
    mocks.getCheckoutMethods.mockImplementation((country: string) => country === "CZ" ? oldRead.promise : currentRead.promise);
    const country = screen.getByRole("textbox", { name: /^Country/ });
    fireEvent.change(country, { target: { value: "CZ" } });
    fireEvent.change(country, { target: { value: "SK" } });
    expect(mocks.getCheckoutMethods.mock.calls.at(-2)![1].aborted).toBe(true);
    expect(screen.getByRole("button", { name: "Place order" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("textbox", { name: "E-mail" }).matches(":disabled")).toBe(false);
    expect(document.querySelector(".checkout-total")).toBeNull();
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).not.toHaveBeenCalled();
    await act(async () => currentRead.resolve({ ...methods, destinationCountry: "SK" }));
    await act(async () => oldRead.resolve({ ...methods, destinationCountry: "CZ", shippingMethods: [] }));
    expect(screen.getByRole("radio", { name: /Courier/ })).toHaveProperty("checked", true);
    expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull();
  });

  it.each(["C", "12", "ČZ", "CZE", "ß"])("blocks an incomplete or non-ASCII country %s without querying stale availability", async value => {
    const user = userEvent.setup();
    offerMethods(); renderCheckout(); await fillGuestAddress(user);
    const calls = mocks.getCheckoutMethods.mock.calls.length;
    fireEvent.change(screen.getByRole("textbox", { name: /^Country/ }), { target: { value } });
    expect(await screen.findByText("Enter a two-letter delivery country to check available methods.")).toBeTruthy();
    expect(mocks.getCheckoutMethods).toHaveBeenCalledTimes(calls);
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).not.toHaveBeenCalled();
    expect(screen.getByRole("textbox", { name: /^Country/ }).getAttribute("aria-invalid")).toBe("true");
  });

  it("requires fresh choices when delivery and payment disappear, without falling back or reviving them later", async () => {
    const user = userEvent.setup();
    offerMethods(); renderCheckout(); await fillGuestAddress(user);
    const country = screen.getByRole("textbox", { name: /^Country/ });
    mocks.getCheckoutMethods.mockResolvedValue({ ...methods, destinationCountry: "CZ",
      paymentMethods: [{ code: "card", name: "Card" }], shippingMethods: [methods.shippingMethods[3]] });
    fireEvent.change(country, { target: { value: "CZ" } });
    await screen.findByText("Delivery options changed. Choose an available shipping method.");
    expect(screen.getByRole("radio", { name: /Home delivery/ })).toHaveProperty("checked", false);
    expect(screen.getByRole("radio", { name: "Card" })).toHaveProperty("checked", false);
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).not.toHaveBeenCalled();
    offerMethods(); fireEvent.change(country, { target: { value: "IE" } });
    await screen.findByRole("radio", { name: /Courier/ });
    expect(screen.getByRole("radio", { name: /Courier/ })).toHaveProperty("checked", false);
    await user.click(screen.getByRole("radio", { name: /Home delivery/ }));
    await user.click(screen.getByRole("radio", { name: "Cash" }));
    expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull();
  });

  it("preserves contact and address through an empty offer, failed reads and explicit GET-only recovery", async () => {
    const user = userEvent.setup();
    offerMethods(); renderCheckout(); await fillGuestAddress(user);
    mocks.getCheckoutMethods.mockResolvedValue({ ...methods, destinationCountry: "SK", shippingMethods: [] });
    fireEvent.change(screen.getByRole("textbox", { name: /^Country/ }), { target: { value: "SK" } });
    await screen.findByText(/No shipping method is currently available/);
    mocks.getCheckoutMethods.mockRejectedValue(new HttpError("network", null));
    await user.click(screen.getByRole("button", { name: "Check available methods again" }));
    await screen.findByRole("button", { name: "Try again" });
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).not.toHaveBeenCalled();
    expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveProperty("value", "guest@example.com");
    expect(screen.getByRole("textbox", { name: "Street and number" })).toHaveProperty("value", "1 Main Street");
    offerMethods(); await user.click(screen.getByRole("button", { name: "Try again" }));
    await screen.findByRole("radio", { name: /Courier/ });
    expect(screen.getByRole("radio", { name: /Courier/ })).toHaveProperty("checked", false);
    expect(mocks.placeOrder).not.toHaveBeenCalled();
  });

  it("rejects a mismatched response country and links server country validation to the effective address", async () => {
    const user = userEvent.setup();
    offerMethods(); renderCheckout(); await fillGuestAddress(user);
    mocks.getCheckoutMethods.mockResolvedValue({ ...methods, destinationCountry: "IE" });
    fireEvent.change(screen.getByRole("textbox", { name: /^Country/ }), { target: { value: "CZ" } });
    await screen.findByText(/unexpected response/);
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).not.toHaveBeenCalled();
    mocks.getCheckoutMethods.mockRejectedValue(new HttpError("http", 400, { errors: { country: ["A country is its two-letter code"] } }));
    await user.click(screen.getByRole("button", { name: "Try again" }));
    const issue = await screen.findByRole("button", { name: "Use an allowed two-letter country code." });
    const field = screen.getByRole("textbox", { name: /^Country/ });
    field.scrollIntoView = vi.fn();
    await user.click(issue);
    expect(document.activeElement).toBe(field);
    expect(field.scrollIntoView).toHaveBeenCalledWith({ block: "center" });
  });

  it("invalidates method reads on cart and store changes and keeps the current draft only within its store", async () => {
    const user = userEvent.setup(), oldRead = deferred<CheckoutMethods>();
    offerMethods(); renderCheckout(); await fillGuestAddress(user);
    mocks.getCheckoutMethods.mockReturnValueOnce(oldRead.promise);
    await user.click(screen.getByRole("button", { name: "Change cart contents" }));
    expect(screen.getByRole("button", { name: "Place order" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("textbox", { name: "Street and number" })).toHaveProperty("value", "1 Main Street");
    const reads = mocks.getCheckoutMethods.mock.calls.length;
    await user.click(screen.getByRole("button", { name: "Change store" }));
    await waitFor(() => expect(mocks.getCheckoutMethods).toHaveBeenCalledTimes(reads + 1));
    await act(async () => oldRead.resolve({ ...methods, destinationCountry: "IE", shippingMethods: [] }));
    expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveProperty("value", "");
    expect(screen.getByRole("radio", { name: /Courier/ })).toBeTruthy();
  });

  it("clears listed pickup choices when the destination changes even if the same method and point remain", async () => {
    const user = userEvent.setup();
    offerMethods();
    mocks.getPickupPoints.mockResolvedValue([{ code: "counter", name: "Counter", line1: "Street", city: "City", postalCode: "123", country: "CZ" }]);
    renderCheckout(); await fillGuestAddress(user);
    await user.click(screen.getByRole("radio", { name: /Pickup/ }));
    await user.selectOptions(await screen.findByRole("combobox", { name: "Pickup point" }), "counter");
    fireEvent.change(screen.getByRole("textbox", { name: /^Country/ }), { target: { value: "CZ" } });
    await screen.findByText(/Choose your pickup point again/);
    const select = await screen.findByRole("combobox", { name: "Pickup point" });
    expect(select).toHaveProperty("value", "");
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).not.toHaveBeenCalled();
    await user.selectOptions(select, "counter");
    expect(screen.queryByText(/Choose your pickup point again/)).toBeNull();
    expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull();
  });

  it("ignores a carrier callback from the old destination and requires a new point", async () => {
    const user = userEvent.setup();
    let callback!: (point: { id: string; name: string }) => void;
    (window as { Packeta?: unknown }).Packeta = { Widget: { pick: (_key: string, answer: typeof callback) => { callback = answer; } } };
    try {
      offerMethods(); renderCheckout(); await fillGuestAddress(user);
      await user.click(screen.getByRole("radio", { name: /Z-BOX/ }));
      await user.click(screen.getByRole("button", { name: "Choose a pickup point" }));
      const stale = callback;
      fireEvent.change(screen.getByRole("textbox", { name: /^Country/ }), { target: { value: "CZ" } });
      await screen.findByRole("button", { name: "Choose a pickup point" });
      await act(async () => stale({ id: "old", name: "Old point" }));
      expect(screen.queryByText("Old point")).toBeNull();
      await user.click(screen.getByRole("button", { name: "Choose a pickup point" }));
      await act(async () => callback({ id: "new", name: "Current point" }));
      expect(screen.getAllByText("Current point")).toHaveLength(2);
    } finally { delete (window as { Packeta?: unknown }).Packeta; }
  });

  it("keeps an uncertain checkout key when the destination is restored and requires explicit review for a changed destination", async () => {
    const user = userEvent.setup();
    offerMethods(); mocks.placeOrder.mockRejectedValue(new HttpError("network", null));
    renderCheckout(); await fillGuestAddress(user);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await screen.findByText(/same request key/);
    const key = mocks.placeOrder.mock.calls[0][1];
    const country = screen.getByRole("textbox", { name: /^Country/ });
    fireEvent.change(country, { target: { value: "CZ" } });
    await screen.findByText("The previous order may have been placed");
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    fireEvent.change(country, { target: { value: "IE" } });
    await waitFor(() => expect(screen.queryByText("The previous order may have been placed")).toBeNull());
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][1]).toBe(key);
    fireEvent.change(country, { target: { value: "CZ" } });
    const review = await screen.findByRole("button", { name: "Start a new attempt" });
    await waitFor(() => expect(review).toHaveProperty("disabled", false));
    await user.click(review);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(3));
    expect(mocks.placeOrder.mock.calls[2][1]).not.toBe(key);
    expect(mocks.placeOrder.mock.calls[2][0].billingAddress.country).toBe("CZ");
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

function offerMethods(value = methods) {
  mocks.getCheckoutMethods.mockImplementation(async (country: string | null) => ({ ...value, destinationCountry: country }));
}

const parcelMethod = { ...methods.shippingMethods[0], code: "parcel", name: "Parcel delivery", price: 16,
  pricing: "perParcel" as const, parcels: 2, mapParcel: { weightKg: 1.25, lengthCm: 31, widthCm: 20, depthCm: 10 } };
const replacementQuote = { currency: "EUR", total: 20, isAvailable: true, unavailable: null, parcels: [{
  contents: [{ variantId: "oak-variant", sku: "OAK", quantity: 1 }],
  grossWeightGrams: 1200, bandUpToGrams: 2000, price: 20, alone: true,
}] };
function fillFast() {
  for (const [name, value] of [["email", "guest@example.com"], ["phone", "+420123456789"],
    ["billing.fullName", "Guest Buyer"], ["billing.line1", "1 Main Street"], ["billing.city", "Dublin"],
    ["billing.postalCode", "D01"], ["billing.country", "IE"]])
    fireEvent.change(document.querySelector(`[name="${name}"]`)!, { target: { value } });
}
async function readyParcelCheckout() {
  offerMethods({ ...methods, shippingMethods: [parcelMethod] });
  renderCheckout();
  await screen.findByRole("radio", { name: /Parcel delivery/ });
  fillFast();
  await waitFor(() => expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull());
}
function priceConflict(quote: unknown = replacementQuote) {
  return new HttpError("http", 409, { title: "The delivery price has changed", shippingQuote: quote });
}

describe("cart-priced delivery at checkout", () => {
  it("ignores an order answer after switching stores", async () => {
    const answer = deferred<{ number: string; token: string; paymentInstructions: string; redirectUrl: null }>();
    await readyParcelCheckout();
    mocks.placeOrder.mockReturnValue(answer.promise);
    fireEvent.submit(document.querySelector("form.checkout")!);
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByRole("button", { name: "Change store" }));
    await screen.findByRole("radio", { name: /Parcel delivery/ });
    await act(async () => answer.resolve({ number: "OLD-ORDER", token: "11111111-1111-1111-1111-111111111111", paymentInstructions: "Pay", redirectUrl: null }));
    expect(screen.queryByText("Order destination")).toBeNull();
    expect(mocks.reloadCart).not.toHaveBeenCalled();
    expect(window.sessionStorage.getItem("shopforge.checkout.recovery")).toBeNull();
    expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveProperty("value", "");
  });

  it("aborts a conflict recovery read when the store changes and ignores its late cart", async () => {
    const read = deferred<Cart>();
    await readyParcelCheckout();
    mocks.getCart.mockReturnValue(read.promise);
    mocks.placeOrder.mockRejectedValue(priceConflict());
    fireEvent.submit(document.querySelector("form.checkout")!);
    await waitFor(() => expect(mocks.getCart).toHaveBeenCalledTimes(1));
    const signal = mocks.getCart.mock.calls[0][0] as AbortSignal;
    fireEvent.click(screen.getByRole("button", { name: "Change store" }));
    expect(signal.aborted).toBe(true);
    await act(async () => read.resolve({ ...cart, itemsTotal: 999 }));
    await screen.findByRole("radio", { name: /Parcel delivery/ });
    expect(document.querySelector(".delivery-quote-review")).toBeNull();
    expect(screen.queryByText("€999.00")).toBeNull();
  });

  it.each(["flat", "perParcel"])("sends exactly the displayed %s delivery total", async pricing => {
    offerMethods({ ...methods, shippingMethods: [pricing === "flat" ? { ...methods.shippingMethods[0], price: 0 } : parcelMethod] });
    mocks.placeOrder.mockRejectedValue(new HttpError("http", 400));
    renderCheckout();
    await screen.findByRole("radio", { name: pricing === "flat" ? /Courier/ : /Parcel delivery/ });
    fillFast();
    await waitFor(() => expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull());
    fireEvent.submit(document.querySelector("form.checkout")!);
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(1));
    expect(mocks.placeOrder.mock.calls[0][0].expectedShippingPrice).toBe(pricing === "flat" ? 0 : 16);
    if (pricing === "perParcel") {
      expect(screen.getByText("2 parcels · total delivery price")).toBeTruthy();
      expect(document.querySelector(".checkout-total strong")?.textContent).toBe("€136.00");
    }
  });

  it("shows the changed quote and blocks Enter until explicit review, then submits the refreshed price with a new key", async () => {
    const user = userEvent.setup();
    await readyParcelCheckout();
    mocks.getCart.mockResolvedValue(cart);
    offerMethods({ ...methods, shippingMethods: [{ ...parcelMethod, price: 20, parcels: 1 }] });
    mocks.placeOrder.mockRejectedValueOnce(priceConflict()).mockRejectedValue(new HttpError("http", 400));
    await user.click(screen.getByRole("button", { name: "Place order" }));
    expect(await screen.findByText("The delivery price has changed")).toBeTruthy();
    expect(document.querySelector(".delivery-quote-review > p")?.textContent).toBe("Previously shown: €16.00. New quote: €20.00.");
    expect(screen.getByText("Parcel 1")).toBeTruthy();
    expect(document.querySelector(".checkout-total strong")?.textContent).toBe("€140.00");
    await waitFor(() => expect(document.activeElement).toBe(document.querySelector(".checkout-review")));
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "I reviewed delivery at €20.00" }));
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][0].expectedShippingPrice).toBe(20);
    expect(mocks.placeOrder.mock.calls[1][1]).not.toBe(mocks.placeOrder.mock.calls[0][1]);
    expect(document.querySelector(".delivery-quote-review")).toBeNull();
  });

  it.each(["flat", "perParcel"])("reviews paid-to-free %s delivery without mistaking parcel cost for the new price", async pricing => {
    const user = userEvent.setup();
    const initial = { ...parcelMethod, pricing: pricing as "flat" | "perParcel", fullPrice: 16 };
    offerMethods({ ...methods, shippingMethods: [initial] });
    renderCheckout(); await screen.findByRole("radio", { name: /Parcel delivery/ }); fillFast();
    await waitFor(() => expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull());
    mocks.getCart.mockResolvedValue(cart);
    offerMethods({ ...methods, shippingMethods: [{ ...initial, price: 0, fullPrice: 20, parcels: pricing === "perParcel" ? 1 : null }] });
    mocks.placeOrder.mockRejectedValueOnce(priceConflict({ ...replacementQuote, charged: 0, shippingDiscount: 20,
      freeShippingThreshold: 100, parcels: pricing === "perParcel" ? replacementQuote.parcels : [] }))
      .mockRejectedValue(new HttpError("http", 400));
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(document.querySelector(".delivery-quote-review > p")?.textContent).toBe("Previously shown: €16.00. New quote: €0.00."));
    await waitFor(() => expect(document.querySelector(".checkout-total strong")?.textContent).toBe("€120.00"));
    expect(document.querySelector(".checkout-option del .currency-value")?.textContent).toBe("€20.00");
    if (pricing === "perParcel") {
      expect(document.querySelector(".delivery-parcel-prices strong")?.textContent).toBe("€20.00");
      expect(screen.getByText(/Parcel prices below show delivery costs before/)).toBeTruthy();
    }
    fireEvent.submit(document.querySelector("form.checkout")!); expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "I reviewed delivery at €0.00" }));
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][0].expectedShippingPrice).toBe(0);
    expect(mocks.placeOrder.mock.calls[1][1]).not.toBe(mocks.placeOrder.mock.calls[0][1]);
  });

  it("requires explicit review when previously free delivery becomes payable", async () => {
    const user = userEvent.setup();
    offerMethods({ ...methods, shippingMethods: [{ ...parcelMethod, price: 0, fullPrice: 16 }] });
    renderCheckout(); await screen.findByRole("radio", { name: /Parcel delivery/ }); fillFast();
    await waitFor(() => expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull());
    mocks.getCart.mockResolvedValue(cart);
    offerMethods({ ...methods, shippingMethods: [{ ...parcelMethod, price: 20, fullPrice: 20 }] });
    mocks.placeOrder.mockRejectedValueOnce(priceConflict({ ...replacementQuote, charged: 20, shippingDiscount: 0,
      freeShippingThreshold: null })).mockRejectedValue(new HttpError("http", 400));
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(document.querySelector(".delivery-quote-review > p")?.textContent).toBe("Previously shown: €0.00. New quote: €20.00."));
    await waitFor(() => expect(document.querySelector(".checkout-total strong")?.textContent).toBe("€140.00"));
    expect(document.querySelector(".checkout-option del")).toBeNull();
    expect(mocks.placeOrder.mock.calls[0][0].expectedShippingPrice).toBe(0);
    fireEvent.submit(document.querySelector("form.checkout")!); expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "I reviewed delivery at €20.00" }));
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][0].expectedShippingPrice).toBe(20);
    expect(mocks.placeOrder.mock.calls[1][1]).not.toBe(mocks.placeOrder.mock.calls[0][1]);
  });

  it("does not approve a changed quote while refreshing methods has failed", async () => {
    await readyParcelCheckout();
    mocks.getCart.mockResolvedValue(cart);
    mocks.getCheckoutMethods.mockRejectedValue(new HttpError("network", null));
    mocks.placeOrder.mockRejectedValue(priceConflict());
    fireEvent.submit(document.querySelector("form.checkout")!);
    const review = await screen.findByRole("button", { name: "I reviewed delivery at €16.00" });
    await waitFor(() => expect(review).toHaveProperty("disabled", true));
    expect(document.querySelector(".delivery-quote-review > p")?.textContent).toBe("Previously shown: €16.00. New quote: €20.00.");
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    expect(document.querySelector(".checkout-total strong")).toBeNull();
  });

  it("uses an even newer GET price only after the shopper reviews the current summary", async () => {
    const user = userEvent.setup();
    await readyParcelCheckout();
    mocks.getCart.mockResolvedValue(cart);
    offerMethods({ ...methods, shippingMethods: [{ ...parcelMethod, price: 22 }] });
    mocks.placeOrder.mockRejectedValueOnce(priceConflict()).mockRejectedValue(new HttpError("http", 400));
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(document.querySelector(".delivery-quote-review > p")?.textContent).toBe("Previously shown: €16.00. New quote: €20.00."));
    await waitFor(() => expect(document.querySelector(".checkout-total strong")?.textContent).toBe("€142.00"));
    const review = screen.getByRole("button", { name: "I reviewed delivery at €22.00" });
    await waitFor(() => expect(review).toHaveProperty("disabled", false));
    await user.click(review);
    await user.click(screen.getByRole("button", { name: "Place order" }));
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][0].expectedShippingPrice).toBe(22);
  });

  it.each([null, { ...replacementQuote, currency: "CZK" }, { ...replacementQuote, total: 1 }])("keeps malformed price changes out of the review %j", async quote => {
    await readyParcelCheckout();
    mocks.getCart.mockResolvedValue(cart);
    mocks.placeOrder.mockRejectedValue(priceConflict(quote));
    fireEvent.submit(document.querySelector("form.checkout")!);
    await screen.findByRole("button", { name: "I reviewed these changes" });
    expect(screen.queryByText("The delivery price has changed")).toBeNull();
    expect(document.querySelector(".delivery-quote-review")).toBeNull();
    expect(screen.getByText(/unexpected response/i)).toBeTruthy();
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
  });

  it("discards the old destination's quote when the shopper changes country during review", async () => {
    await readyParcelCheckout();
    mocks.getCart.mockResolvedValue(cart);
    mocks.placeOrder.mockRejectedValue(priceConflict());
    fireEvent.submit(document.querySelector("form.checkout")!);
    await waitFor(() => expect(document.querySelector(".delivery-quote-review > p")?.textContent).toBe("Previously shown: €16.00. New quote: €20.00."));
    fireEvent.change(document.querySelector('[name="billing.country"]')!, { target: { value: "CZ" } });
    await screen.findByRole("radio", { name: /Parcel delivery/ });
    expect(document.querySelector(".delivery-quote-review")).toBeNull();
    expect(screen.getByText("Review the updated order")).toBeTruthy();
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
  });

  it("retains the shopper's exact shipping refusal beside the methods while only refreshing reads", async () => {
    await readyParcelCheckout();
    mocks.getCart.mockResolvedValue(cart);
    mocks.placeOrder.mockRejectedValue(new HttpError("http", 400, { errors: { shippingMethodCode: ["<b>This order is too heavy.</b>"] } }));
    fireEvent.submit(document.querySelector("form.checkout")!);
    await waitFor(() => expect(document.querySelector("#shipping-method-error")?.textContent).toBe("<b>This order is too heavy.</b>"));
    expect(document.querySelector("#shipping-method-error b")).toBeNull();
    expect(screen.getByRole("radio", { name: /Parcel delivery/ }).getAttribute("aria-describedby")).toBe("shipping-method-error");
    expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveProperty("value", "guest@example.com");
    expect(mocks.getCart).toHaveBeenCalledTimes(1);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
    fireEvent.submit(document.querySelector("form.checkout")!);
    expect(mocks.placeOrder).toHaveBeenCalledTimes(1);
  });

  it.each(["en", "cs"])("shows all unavailable reasons with real item names and no selectable or priced refusal in %s", async language => {
    await i18n.changeLanguage(language);
    try {
      offerMethods({ ...methods, shippingMethods: [], unavailableShippingMethods:
        ["notToCountry", "tooHeavy", "tooLarge", "missingData", "tooManyParcels"].map((reason, index) => ({
          code: "refused-" + index, name: "<b>Refused " + index + "</b>", reason: reason as "tooLarge", variantId: index === 0 ? "oak-variant" : "unknown",
        })) });
      renderCheckout();
      await screen.findByText("<b>Refused 0</b>");
      const region = document.querySelector(".checkout-unavailable-delivery")!;
      expect(region.querySelectorAll("li")).toHaveLength(5);
      expect(region.querySelectorAll("b")).toHaveLength(0);
      expect(within(region as HTMLElement).getByText("Oak chair")).toBeTruthy();
      expect(within(region as HTMLElement).getByRole("link").getAttribute("href")).toBe("/cart");
      expect(screen.queryByRole("radio", { name: /Refused/ })).toBeNull();
      expect(mocks.placeOrder).not.toHaveBeenCalled();
    } finally { await i18n.changeLanguage("en"); }
  });

  it("passes exactly the quoted numeric units to Packeta and invalidates old callbacks when they change", async () => {
    const user = userEvent.setup();
    const callbacks: ((point: { id: number }) => void)[] = [];
    const pick = vi.fn((_key, callback) => callbacks.push(callback));
    (window as { Packeta?: unknown }).Packeta = { Widget: { pick } };
    try {
      offerMethods({ ...methods, shippingMethods: [{ ...parcelMethod, requiresPickupPoint: true, pickupPointChoice: "carrier-map" }] });
      renderCheckout();
      await screen.findByRole("radio", { name: /Parcel delivery/ }); fillFast();
      await user.click(await screen.findByRole("button", { name: "Choose a pickup point" }));
      expect(pick).toHaveBeenLastCalledWith("widget-key", expect.any(Function), { country: "cz", group: "zbox", language: "en", weight: 1.25, length: 31, width: 20, depth: 10 });
      offerMethods({ ...methods, shippingMethods: [{ ...parcelMethod, requiresPickupPoint: true, pickupPointChoice: "carrier-map", mapParcel: { ...parcelMethod.mapParcel, weightKg: 2.5 } }] });
      await user.click(screen.getByRole("button", { name: "Change cart contents" }));
      await screen.findByRole("button", { name: "Choose a pickup point" });
      act(() => callbacks[0]({ id: 999 }));
      expect(screen.queryByText("999")).toBeNull();
      await user.click(screen.getByRole("button", { name: "Choose a pickup point" }));
      expect(pick).toHaveBeenLastCalledWith("widget-key", expect.any(Function), expect.objectContaining({ weight: 2.5 }));
    } finally { delete (window as { Packeta?: unknown }).Packeta; }
  });
});
