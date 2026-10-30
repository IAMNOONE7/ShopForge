// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import type { Cart, CheckoutMethods } from "../cart";
import { HttpError } from "../api/http";
import { CartProvider } from "../components/CartProvider";
import { CustomerContext } from "../customerContext";
import { initializeI18n, i18n } from "../i18n";
import { StoreContext } from "../storeContext";
import type { Store } from "../store";
import { CheckoutPage } from "./CheckoutPage";
const mocks = vi.hoisted(() => ({ getCart: vi.fn(), getCheckoutMethods: vi.fn(), removeDiscount: vi.fn(), placeOrder: vi.fn() }));
vi.mock("../cart", async original => ({ ...await original<typeof import("../cart")>(), ...mocks }));
const store: Store = { id: "store", name: "Store", currency: "EUR", culture: "en-IE", logoUrl: null, providerKeys: [], theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 4 } };
const invalid: Cart = { items: [{ storeProductId: "listing", variantId: "form", name: "Tool", slug: "tool", optionValues: [], unitPrice: 120, quantity: 1, lineTotal: 120, available: 5, imageUrl: null }], count: 1, itemsTotal: 120, vatTotal: 20, changed: false, discount: null, discountProblem: "Delivery is already free. <b>literal</b>", freeShipping: { threshold: 100, remaining: 0 } };
const clean: Cart = { ...invalid, discountProblem: null };
const validCode: Cart = { ...clean, discount: { code: "FREESHIP", name: "Free shipping", amount: 0 } };
const methods: CheckoutMethods = { destinationCountry: null, paymentMethods: [{ code: "bank", name: "Bank" }], shippingMethods: [{ code: "courier", name: "Courier", price: 0, fullPrice: 20, requiresPickupPoint: false, pickupPointChoice: "none", pricing: "flat", parcels: null, mapParcel: null }], freeShipping: { threshold: 300, remaining: 42 } };
function app(id = store.id) { return <StoreContext value={{ ...store, id }}><CustomerContext value={{ status: "guest", customer: null, error: null, apply: vi.fn(), retry: vi.fn() }}><CartProvider key={id}><MemoryRouter initialEntries={["/checkout"]}><Routes><Route path="/checkout" element={<CheckoutPage />} /><Route path="/cart" element={<p>Cart destination</p>} /></Routes></MemoryRouter></CartProvider></CustomerContext></StoreContext>; }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(accept => { resolve = accept; }); return { promise, resolve }; }
function fill() {
  for (const [name, value] of Object.entries({ email: "shopper@example.test", phone: "+420123456789", "billing.fullName": "Billing Buyer", "billing.line1": "1 Main Street", "billing.city": "Prague", "billing.postalCode": "10000", "billing.country": "CZ" }))
    fireEvent.change(document.getElementsByName(name)[0], { target: { value } });
  fireEvent.click(screen.getByRole("checkbox", { name: "Ship to a different address" }));
  for (const [name, value] of Object.entries({ "shipping.fullName": "Delivery Buyer", "shipping.line1": "2 Side Street", "shipping.city": "Brno", "shipping.postalCode": "60000", "shipping.country": "CZ" }))
    fireEvent.change(document.getElementsByName(name)[0], { target: { value } });
}
const submit = () => fireEvent.submit(document.querySelector("form.checkout")!);
const remove = () => screen.getByRole("button", { name: "Remove code" });
const review = () => screen.getByRole("button", { name: "I reviewed these changes" });
async function ready() { render(app()); await screen.findByRole("radio", { name: /Courier/ }); fill(); await waitFor(() => expect(document.querySelector(".free-shipping-progress")?.textContent).toContain("€42.00")); }
beforeAll(async () => { await initializeI18n(); HTMLElement.prototype.scrollIntoView = vi.fn(); });
beforeEach(async () => {
  await i18n.changeLanguage("en"); mocks.getCart.mockImplementation(async () => ({ ...invalid }));
  mocks.getCheckoutMethods.mockImplementation(async (country: string | null) => ({ ...methods, destinationCountry: country }));
  mocks.removeDiscount.mockResolvedValue(clean); mocks.placeOrder.mockRejectedValue(new HttpError("http", 400));
});
afterEach(() => { cleanup(); vi.resetAllMocks(); sessionStorage.clear(); });
describe("checkout code recovery", () => {
  it("keeps entered addresses and contact, blocks invalid-code orders, and requires review after removal", async () => {
    const user = userEvent.setup(); await ready();
    expect(document.querySelectorAll("form")).toHaveLength(1);
    expect(screen.getByText(invalid.discountProblem!)).toBeTruthy(); expect(document.querySelector(".cart-discount b")).toBeNull();
    submit(); expect(mocks.placeOrder).not.toHaveBeenCalled();
    await user.click(remove()); await waitFor(() => expect(review().getAttribute("disabled")).toBeNull());
    expect(mocks.removeDiscount).toHaveBeenCalledOnce(); expect(document.querySelectorAll("form")).toHaveLength(1);
    expect(document.getElementsByName("email")[0]).toHaveProperty("value", "shopper@example.test");
    expect(document.getElementsByName("shipping.fullName")[0]).toHaveProperty("value", "Delivery Buyer");
    expect(document.getElementsByName("billing.line1")[0]).toHaveProperty("value", "1 Main Street");
    await waitFor(() => expect(document.activeElement).toBe(document.querySelector(".checkout-review")));
    submit(); expect(mocks.placeOrder).not.toHaveBeenCalled(); await user.click(review()); expect(mocks.placeOrder).not.toHaveBeenCalled();
    submit(); await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledOnce());
    expect(mocks.placeOrder.mock.calls[0][0]).toMatchObject({ expectedShippingPrice: 0, email: "shopper@example.test", shippingAddress: { fullName: "Delivery Buyer", line1: "2 Side Street" } });
  });
  it("uses only the current destination offer's progress and hides it during a new quote", async () => {
    const held = deferred<CheckoutMethods>(); await ready();
    expect(document.querySelector(".free-shipping-progress")?.textContent).toContain("€42.00");
    mocks.getCheckoutMethods.mockReturnValueOnce(held.promise);
    fireEvent.change(document.getElementsByName("shipping.country")[0], { target: { value: "SK" } });
    await waitFor(() => expect(document.querySelector(".free-shipping-progress")).toBeNull());
    fireEvent.change(document.getElementsByName("shipping.country")[0], { target: { value: "IE" } });
    await waitFor(() => expect(document.querySelector(".free-shipping-progress")?.textContent).toContain("€42.00"));
    await act(async () => held.resolve({ ...methods, destinationCountry: "SK", freeShipping: { threshold: 999, remaining: 0 } }));
    expect(document.querySelector(".free-shipping-progress")?.textContent).not.toContain("€999.00");
  });
  it("locks duplicate removals until the server answer and fresh methods arrive", async () => {
    const held = deferred<Cart>(); mocks.removeDiscount.mockReturnValueOnce(held.promise); await ready();
    act(() => { fireEvent.click(remove()); fireEvent.click(remove()); }); expect(mocks.removeDiscount).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Removing…" }).matches(":disabled")).toBe(true); expect(document.querySelector(".free-shipping-progress")).toBeNull();
    await act(async () => held.resolve(clean)); await screen.findByRole("button", { name: "I reviewed these changes" }); expect(mocks.placeOrder).not.toHaveBeenCalled();
  });
  it("reads after a lost removal answer without replaying it or losing typed details", async () => {
    mocks.getCart.mockResolvedValueOnce(invalid).mockResolvedValue(clean); mocks.removeDiscount.mockRejectedValueOnce(new HttpError("network", null));
    await ready(); await userEvent.click(remove());
    await waitFor(() => expect(review().matches(":disabled")).toBe(false)); expect(mocks.removeDiscount).toHaveBeenCalledOnce(); expect(mocks.getCart).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole("button", { name: "Remove code" })).toBeNull();
    expect(document.getElementsByName("shipping.line1")[0]).toHaveProperty("value", "2 Side Street"); expect(mocks.placeOrder).not.toHaveBeenCalled();
  });
  it("keeps writes blocked through a failed recovery read and retries only reads", async () => {
    mocks.getCart.mockResolvedValueOnce(invalid).mockRejectedValueOnce(new HttpError("network", null)).mockResolvedValue(clean);
    mocks.removeDiscount.mockRejectedValueOnce(new HttpError("network", null)); await ready(); await userEvent.click(remove());
    await screen.findByRole("button", { name: "Try again" }); expect(review().matches(":disabled")).toBe(true); expect(remove().matches(":disabled")).toBe(true);
    submit(); expect(mocks.placeOrder).not.toHaveBeenCalled(); await userEvent.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(review().matches(":disabled")).toBe(false)); expect(mocks.removeDiscount).toHaveBeenCalledOnce(); expect(mocks.getCart).toHaveBeenCalledTimes(3);
    await waitFor(() => expect(document.activeElement).toBe(document.querySelector(".checkout-review")));
  });
  it("recovers the checkout's code conflict and uses a new key only after code removal and review", async () => {
    mocks.getCart.mockResolvedValueOnce(validCode).mockResolvedValue(invalid);
    mocks.placeOrder.mockRejectedValueOnce(new HttpError("http", 409, { title: "The discount code cannot be used", detail: invalid.discountProblem! })).mockRejectedValue(new HttpError("http", 400));
    await ready(); await waitFor(() => expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull()); submit();
    await waitFor(() => expect(review().matches(":disabled")).toBe(true)); expect(mocks.placeOrder).toHaveBeenCalledOnce();
    expect(screen.getByRole("link", { name: i18n.t("cart:discountNeedsRemoval") }).getAttribute("href")).toBe("#checkout-discount");
    await userEvent.click(remove()); await waitFor(() => expect(review().matches(":disabled")).toBe(false));
    await userEvent.click(review()); submit(); await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledTimes(2));
    expect(mocks.placeOrder.mock.calls[1][1]).not.toBe(mocks.placeOrder.mock.calls[0][1]); expect(mocks.placeOrder.mock.calls[1][0].expectedShippingPrice).toBe(0);
  });
  it.each(["network", "inFlight"])("does not mutate a code while a previous order outcome is %s", async kind => {
    mocks.getCart.mockResolvedValue(validCode); mocks.placeOrder.mockRejectedValue(kind === "network" ? new HttpError("network", null) : new HttpError("http", 409, { title: "A request with this key is still in progress" }));
    await ready(); await waitFor(() => expect(screen.getByRole("button", { name: "Place order" }).getAttribute("aria-disabled")).toBeNull()); submit();
    await waitFor(() => expect(mocks.placeOrder).toHaveBeenCalledOnce());
    if (kind === "inFlight") await userEvent.click(screen.getByRole("button", { name: "I checked; allow the same retry" }));
    expect(remove().matches(":disabled")).toBe(true); fireEvent.click(remove()); expect(mocks.removeDiscount).not.toHaveBeenCalled();
  });
  it("does not apply or announce a departed store's late removal", async () => {
    const held = deferred<Cart>(); mocks.removeDiscount.mockReturnValueOnce(held.promise); const view = render(app()); await screen.findByRole("button", { name: "Remove code" });
    await userEvent.click(remove()); view.rerender(app("other")); await screen.findByRole("button", { name: "Remove code" });
    await act(async () => held.resolve({ ...clean, changed: true })); expect(screen.queryByRole("button", { name: "I reviewed these changes" })).toBeNull(); expect(sessionStorage.getItem("shopforge.cart.adjusted")).toBeNull();
  });
});
