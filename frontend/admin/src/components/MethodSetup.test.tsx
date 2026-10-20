// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api, type PaymentMethod, type ShippingMethod } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { MethodsSection } from "./MethodsSection";

const payment: PaymentMethod = { code: "card", name: "Card", providerKey: "comgate", isActive: true };
const shipping: ShippingMethod = { code: "box", name: "Z-BOX", providerKey: "packeta", isActive: false, price: 0, vatRate: 21.125,
  requiresPickupPoint: true, maxWeightGrams: 5000, countries: ["CZ"], taxClass: null };
const money = new Intl.NumberFormat("en", { style: "currency", currency: "CZK" });
function app(currency = money, allowed = true) {
  return <MemoryRouter><MethodsSection storeId="store-a" money={currency} canManage={allowed} storeStale={false} /></MemoryRouter>;
}
async function paymentEditor(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "Edit payment method Card" }));
  return within(screen.getByRole("form", { name: "Edit payment method" }));
}
async function newShipping(user: ReturnType<typeof userEvent.setup>) {
  const add = await screen.findByRole("button", { name: "Add Packeta delivery" });
  await waitFor(() => expect(add).toHaveProperty("disabled", false)); await user.click(add);
  const form = within(screen.getByRole("form", { name: "New shipping method" }));
  await user.type(form.getByRole("textbox", { name: /Method name/ }), "Doprava domů");
  await user.selectOptions(form.getByRole("combobox", { name: "Shipping provider" }), "packeta");
  await user.type(form.getByRole("textbox", { name: /Price/ }), "0"); return form;
}
beforeAll(async () => { await initializeI18n(); await i18n.changeLanguage("en"); HTMLElement.prototype.scrollIntoView = vi.fn(); });
beforeEach(() => {
  vi.spyOn(api, "paymentMethods").mockResolvedValue([payment]);
  vi.spyOn(api, "shippingMethods").mockResolvedValue([shipping]);
  vi.spyOn(api, "paymentProviders").mockResolvedValue(["comgate"]);
  vi.spyOn(api, "shippingProviders").mockResolvedValue(["manual", "packeta"]);
  vi.spyOn(api, "providerConnections").mockResolvedValue([]);
  vi.spyOn(api, "storeReadiness").mockResolvedValue({ isReady: false, checks: [] });
  vi.spyOn(api, "taxClasses").mockResolvedValue([]);
  vi.spyOn(api, "pickupPoints").mockResolvedValue([]);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("keeps unavailable Stripe and active manual shipping visible without making automatic changes", async () => {
  vi.mocked(api.paymentMethods).mockResolvedValue([{ ...payment, providerKey: "stripe" }]);
  vi.mocked(api.shippingMethods).mockResolvedValue([{ ...shipping, name: "Personal pickup", providerKey: "manual", isActive: true }]);
  const save = vi.spyOn(api, "updateShippingMethod"), create = vi.spyOn(api, "createShippingMethod"); render(app());
  await screen.findByText(/Active delivery outside the Packeta launch setup: Personal pickup/);
  expect(screen.getByText(/Provider stripe is not currently offered/)).toBeTruthy();
  expect(screen.getByRole("button", { name: "Edit payment method Card" })).toBeTruthy();
  expect(screen.getByText("Existing store pickup locations", { selector: "summary" }).closest("details")).toHaveProperty("open", false);
  expect(save).not.toHaveBeenCalled(); expect(create).not.toHaveBeenCalled();
});

it("creates payment only after explicit discovery selection, preserving a Czech storefront name", async () => {
  const user = userEvent.setup(), create = vi.spyOn(api, "createPaymentMethod").mockResolvedValue({ ...payment, code: "kartou", name: "Žlutá karta" });
  render(app()); const add = await screen.findByRole("button", { name: "Add payment method" });
  await waitFor(() => expect(add).toHaveProperty("disabled", false)); await user.click(add);
  const form = within(screen.getByRole("form", { name: "Add payment method" }));
  expect(form.getByRole("combobox")).toHaveProperty("value", ""); expect(form.queryByRole("option", { name: "stripe" })).toBeNull();
  await user.type(form.getByRole("textbox", { name: /Method name/ }), "Žlutá karta"); await user.click(form.getByRole("button", { name: "Save" }));
  expect(create).not.toHaveBeenCalled(); await user.selectOptions(form.getByRole("combobox"), "comgate");
  await user.click(form.getByRole("button", { name: "Save" })); await waitFor(() => expect(create).toHaveBeenCalledExactlyOnceWith("store-a", "Žlutá karta", "comgate"));
  expect(screen.queryByRole("form", { name: "Add payment method" })).toBeNull();
});

it.each([400, 409])("keeps a refused payment draft after %i and links recognized server fields", async status => {
  const user = userEvent.setup(), save = vi.spyOn(api, "updatePaymentMethod").mockRejectedValue(new HttpError("http", status, { errors: { Name: ["Rejected"] } }));
  render(app()); const form = await paymentEditor(user), name = form.getByRole("textbox", { name: /Method name/ });
  await user.clear(name); await user.type(name, "Platba kartou"); await user.click(form.getByRole("checkbox", { name: "Active" }));
  await user.click(form.getByRole("button", { name: "Save" })); await screen.findByText(/The request was refused/);
  expect(name).toHaveProperty("value", "Platba kartou"); expect(form.queryByRole("combobox")).toBeNull();
  expect(save).toHaveBeenCalledExactlyOnceWith("store-a", "card", { name: "Platba kartou", isActive: false });
  await user.click(screen.getByRole("button", { name: "Review this method field." })); expect(document.activeElement).toBe(name);
  if (status === 409) expect(screen.getByText(/Codes are derived from new names/)).toBeTruthy();
});

it("does not replay a lost payment save and requires review of a successful fresh read", async () => {
  let release!: () => void; const held = new Promise<void>(resolve => { release = resolve; });
  const user = userEvent.setup(), save = vi.spyOn(api, "updatePaymentMethod").mockImplementation(async () => {
    await held; vi.mocked(api.paymentMethods).mockResolvedValue([{ ...payment, name: "Saved card" }]); throw new TypeError("Lost response");
  }); render(app()); const form = await paymentEditor(user);
  await user.clear(form.getByRole("textbox", { name: /Method name/ })); await user.type(form.getByRole("textbox", { name: /Method name/ }), "Saved card");
  const element = form.getByRole("button", { name: "Save" }).closest("form")!;
  fireEvent.submit(element); fireEvent.submit(element); expect(save).toHaveBeenCalledOnce(); release();
  await screen.findByText(/The outcome is not confirmed/); await screen.findByRole("heading", { name: "Saved card" });
  expect(screen.getByRole("button", { name: "Edit payment method Saved card" })).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Finish review" }));
  expect(screen.getByRole("button", { name: "Edit payment method Saved card" })).toHaveProperty("disabled", false); expect(save).toHaveBeenCalledOnce();
});

it("blocks an acknowledged payment change when its refresh fails, without asking to repeat the save", async () => {
  const user = userEvent.setup(), save = vi.spyOn(api, "updatePaymentMethod").mockImplementation(async () => {
    vi.mocked(api.paymentMethods).mockRejectedValue(new HttpError("http", 503)); return payment;
  }); render(app()); const form = await paymentEditor(user); await user.click(form.getByRole("button", { name: "Save" }));
  await screen.findByText(/The save was acknowledged/); await screen.findByRole("button", { name: "Try again" });
  expect(screen.getByRole("button", { name: "Edit payment method Card" })).toHaveProperty("disabled", true);
  vi.mocked(api.paymentMethods).mockResolvedValue([payment]); await user.click(screen.getByRole("button", { name: "Try again" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "Edit payment method Card" })).toHaveProperty("disabled", false)); expect(save).toHaveBeenCalledOnce();
});

it("blocks a payment draft changed by a refreshed read until it is explicitly reopened", async () => {
  const user = userEvent.setup(), save = vi.spyOn(api, "updatePaymentMethod"); render(app()); const form = await paymentEditor(user);
  vi.mocked(api.paymentMethods).mockResolvedValue([{ ...payment, isActive: false }]); await user.click(screen.getByRole("button", { name: "Refresh methods and checks" }));
  await screen.findByText(/This method changed or disappeared/); fireEvent.submit(form.getByRole("button", { name: "Save" }).closest("form")!);
  expect(save).not.toHaveBeenCalled(); expect(form.getByRole("checkbox", { name: "Active" })).toHaveProperty("checked", true);
  await user.click(form.getByRole("button", { name: "Cancel" })); const reopened = await paymentEditor(user);
  expect(reopened.getByRole("checkbox", { name: "Active" })).toHaveProperty("checked", false);
});

it.each([["box", true], ["address", false]] as const)("requires an explicit Packeta %s mode and sends its actual pickup discriminator", async (mode, requiresPickupPoint) => {
  const user = userEvent.setup(), create = vi.spyOn(api, "createShippingMethod").mockResolvedValue(shipping); render(app()); const form = await newShipping(user);
  await user.click(form.getByRole("button", { name: "Add" })); expect(create).not.toHaveBeenCalled(); expect(form.getByRole("combobox", { name: "Packeta delivery mode" })).toHaveProperty("value", "");
  await user.selectOptions(form.getByRole("combobox", { name: "Packeta delivery mode" }), mode);
  await user.type(form.getByRole("textbox", { name: /Countries/ }), "cz, cz"); await user.click(form.getByRole("button", { name: "Add" }));
  await waitFor(() => expect(create).toHaveBeenCalledExactlyOnceWith("store-a", { name: "Doprava domů", providerKey: "packeta", price: 0, vatRate: 21,
    isActive: true, requiresPickupPoint, maxWeightGrams: null, countries: ["CZ"], taxClass: null }));
});

it.each([["JPY", "1,5", null], ["JPY", "1", 1], ["KWD", "1,125", 1.125], ["CZK", "1,125", null]] as const)("uses %s precision for delivery price %s", async (currency, price, expected) => {
  const user = userEvent.setup(), create = vi.spyOn(api, "createShippingMethod").mockResolvedValue(shipping);
  render(app(new Intl.NumberFormat("en", { style: "currency", currency }))); const form = await newShipping(user);
  await user.selectOptions(form.getByRole("combobox", { name: "Packeta delivery mode" }), "address");
  await user.clear(form.getByRole("textbox", { name: /Price/ })); await user.type(form.getByRole("textbox", { name: /Price/ }), price);
  await user.click(form.getByRole("button", { name: "Add" }));
  if (expected === null) { expect(create).not.toHaveBeenCalled(); expect(form.getByText(/Enter a nonnegative price/)).toBeTruthy(); }
  else await waitFor(() => expect(create).toHaveBeenCalledWith("store-a", expect.objectContaining({ price: expected })));
});

it("does not fabricate a shipping fallback when Packeta is omitted, while saved legacy settings remain editable", async () => {
  const user = userEvent.setup(); vi.mocked(api.shippingProviders).mockResolvedValue(["manual"]);
  vi.mocked(api.shippingMethods).mockResolvedValue([{ ...shipping, providerKey: "manual" }]); render(app());
  await screen.findByText(/Packeta is absent/); expect(screen.getByRole("button", { name: "Add Packeta delivery" })).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Edit shipping method Z-BOX" }));
  const form = within(screen.getByRole("form", { name: "Z-BOX" })); expect(form.queryByRole("combobox", { name: "Packeta delivery mode" })).toBeNull();
  expect(form.getByRole("checkbox", { name: "Needs a pickup point" })).toHaveProperty("checked", true);
});

it("retains precise fallback VAT, zero and immutable provider in a full shipping repair even when account checks fail", async () => {
  const user = userEvent.setup(), save = vi.spyOn(api, "updateShippingMethod").mockResolvedValue(shipping);
  vi.mocked(api.providerConnections).mockRejectedValue(new HttpError("http", 503)); vi.mocked(api.storeReadiness).mockRejectedValue(new HttpError("http", 503));
  render(app()); await user.click(await screen.findByRole("button", { name: "Edit shipping method Z-BOX" }));
  const form = within(screen.getByRole("form", { name: "Z-BOX" })); expect(form.queryByRole("combobox", { name: "Shipping provider" })).toBeNull();
  await user.click(form.getByRole("button", { name: "Save" })); await waitFor(() => expect(save).toHaveBeenCalledExactlyOnceWith("store-a", "box", {
    name: "Z-BOX", isActive: false, price: 0, vatRate: 21.125, requiresPickupPoint: true, maxWeightGrams: 5000, countries: ["CZ"], taxClass: null }));
});

it("keeps read-only pickup controls disabled even on a direct form submit", async () => {
  const user = userEvent.setup(), create = vi.spyOn(api, "createPickupPoint"); render(app(money, false));
  await screen.findByRole("heading", { name: "Card" }); await user.click(screen.getByText("Existing store pickup locations", { selector: "summary" }));
  const input = screen.getByRole("textbox", { name: "New pickup point" }); expect(input.closest("fieldset")).toHaveProperty("disabled", true);
  fireEvent.submit(input.closest("form")!); expect(create).not.toHaveBeenCalled(); expect(screen.queryByRole("button", { name: "Add payment method" })).toBeNull();
});

it.each([["maxWeightGrams", "0"], ["maxWeightGrams", "1.5"], ["maxWeightGrams", "2147483648"], ["countries", "CZE"], ["countries", "C1"], ["vatRate", "101"]])("refuses invalid shipping %s=%s before a write", async (field, value) => {
  const user = userEvent.setup(), save = vi.spyOn(api, "updateShippingMethod"); render(app());
  await user.click(await screen.findByRole("button", { name: "Edit shipping method Z-BOX" }));
  const form = screen.getByRole("form", { name: "Z-BOX" }), input = form.querySelector<HTMLInputElement>(`[name="${field}"]`)!;
  await user.clear(input); await user.type(input, value); fireEvent.submit(form);
  expect(save).not.toHaveBeenCalled(); expect(input).toBe(document.activeElement); expect(input.getAttribute("aria-invalid")).toBe("true");
});

it("treats a changed immutable payment provider in a save reply as an unknown outcome", async () => {
  const user = userEvent.setup(), save = vi.spyOn(api, "updatePaymentMethod").mockResolvedValue({ ...payment, providerKey: "manual" });
  render(app()); const form = await paymentEditor(user); await user.click(form.getByRole("button", { name: "Save" }));
  await screen.findByText(/The outcome is not confirmed/); expect(screen.queryByText(/The save was acknowledged/)).toBeNull(); expect(save).toHaveBeenCalledOnce();
});

it("permits repairs to saved payment methods when discovery fails but blocks new creation", async () => {
  const user = userEvent.setup(); vi.mocked(api.paymentProviders).mockRejectedValue(new HttpError("http", 503)); render(app());
  await screen.findByRole("button", { name: "Try again" }); expect(screen.getByRole("button", { name: "Add payment method" })).toHaveProperty("disabled", true);
  const form = await paymentEditor(user); expect(form.getByRole("button", { name: "Save" })).toHaveProperty("disabled", false);
  expect(form.getByText(/Provider availability is not current/)).toBeTruthy();
});
