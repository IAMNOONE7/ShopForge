// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api, type Product, type ShippingMethod, type StoreProduct } from "../api";
import { HttpError } from "../api/http";
import type { TaxClass } from "../api/taxClasses";
import { i18n, initializeI18n } from "../i18n";
import { ListingForm } from "./ListingForm";
import { ShippingMethodsSection } from "./ShippingMethodsSection";

const books: TaxClass = { code: "books", name: "Printed books", rates: [{ country: "CZ", rate: 10 }, { country: "DE", rate: 7 }, { country: "IE", rate: 0 }] };
const standard: TaxClass = { code: "standard", name: "Standard", rates: [] };
const listing: StoreProduct = { id: "listing", productId: "product", sku: "BOOK", name: "Book", slug: "book", description: "Saved description", price: 0, vatRate: 21,
  isVisible: false, sortOrder: -2, categoryIds: ["saved"], archivedAt: null, taxClass: "standard", seoTitle: null, seoDescription: null, seoSocialImageUrl: null, seoNoIndex: false };
const product: Product = { id: "product", sku: "BOOK", ean: null, brand: null, weightGrams: null, images: [], optionNames: [], variants: [] };
const shipping: ShippingMethod = { code: "parcel", name: "Parcel", providerKey: "packeta", price: 0, vatRate: 0, isActive: false, requiresPickupPoint: false,
  maxWeightGrams: 5000, countries: ["CZ", "SK"], taxClass: "standard" };
let classes: TaxClass[], methods: ShippingMethod[];
const money = new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" });
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (reason: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
function edit(item = listing, disabled = false) { const save = vi.fn().mockResolvedValue(undefined); render(<MemoryRouter><ListingForm storeId="a" item={item} disabled={disabled} pending={false} onSave={save} /></MemoryRouter>); return save; }
async function shippingApp(allowed = true, storeStale = false, open = true) {
  const view = render(<MemoryRouter><ShippingMethodsSection storeId="a" money={money} allowed={allowed} storeStale={storeStale} /></MemoryRouter>);
  if (open && allowed && !storeStale) { const button = await screen.findByRole("button", { name: "Edit shipping method Parcel" }); fireEvent.click(button); }
  return view;
}
const parcelForm = () => screen.getByRole("form", { name: "Parcel" });
async function choicesReady() { await screen.findAllByRole("option", { name: "Printed books (books)" }); }
beforeAll(async () => { HTMLElement.prototype.scrollIntoView = vi.fn(); await initializeI18n(); });
beforeEach(async () => {
  await i18n.changeLanguage("en"); classes = structuredClone([standard, books]); methods = structuredClone([shipping]);
  vi.spyOn(api, "taxClasses").mockImplementation(async () => structuredClone(classes));
  vi.spyOn(api, "shippingMethods").mockImplementation(async () => structuredClone(methods));
  vi.spyOn(api, "shippingProviders").mockResolvedValue(["manual", "packeta"]);
  vi.spyOn(api, "updateShippingMethod").mockImplementation(async (_store, code, input) => {
    const saved = { ...methods.find(row => row.code === code)!, ...input }; methods = [saved]; return saved;
  });
  vi.spyOn(api, "createShippingMethod").mockImplementation(async (_store, input) => { const saved = { ...input, isActive: true, code: "new-parcel" }; methods.push(saved); return saved; });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it.each(["books", ""])("assigns or clears listing class %s without changing prices, fallback VAT or other details", async value => {
  const user = userEvent.setup(), save = edit(); await choicesReady();
  await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), value);
  if (value) expect(screen.getByText(/IE 0%/)).toBeTruthy();
  expect(screen.getByRole("textbox", { name: "VAT rate %" })).toHaveProperty("value", "21");
  await user.click(screen.getByRole("button", { name: "Save listing" }));
  expect(save).toHaveBeenCalledExactlyOnceWith({ name: "Book", slug: "book", description: "Saved description", price: 0, vatRate: 21, isVisible: false, sortOrder: -2, taxClass: value || null }, "product");
});
it.each(["standard", null])("preserves saved listing assignment %s when choices fail; a lookup retry never saves", async taxClass => {
  vi.mocked(api.taxClasses).mockRejectedValueOnce(new HttpError("http", 503));
  const user = userEvent.setup(), save = edit({ ...listing, taxClass }); await screen.findByRole("alert");
  expect(screen.getByRole("combobox", { name: "Tax class" })).toHaveProperty("value", taxClass ?? "");
  await user.click(screen.getByRole("button", { name: "Save listing" })); expect(save.mock.calls[0][0].taxClass).toBe(taxClass);
  await user.click(screen.getByRole("button", { name: "Try again" })); await choicesReady(); expect(save).toHaveBeenCalledOnce();
});
it("preserves an unavailable saved class and permits its explicit removal", async () => {
  const user = userEvent.setup(), save = edit({ ...listing, taxClass: "legacy" }); await choicesReady();
  expect(screen.getByText(/Saved class legacy is not in/)).toBeTruthy();
  await user.click(screen.getByRole("button", { name: "Save listing" })); expect(save.mock.calls[0][0].taxClass).toBe("legacy");
  await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), ""); await user.click(screen.getByRole("button", { name: "Save listing" }));
  expect(save.mock.calls[1][0].taxClass).toBeNull();
});
it("keeps the saved assignment while the initial class read is pending", async () => {
  const held = deferred<TaxClass[]>(); vi.mocked(api.taxClasses).mockReturnValueOnce(held.promise); const user = userEvent.setup(), save = edit();
  await user.click(screen.getByRole("button", { name: "Save listing" })); expect(save.mock.calls[0][0].taxClass).toBe("standard");
  await act(async () => held.resolve(classes)); await choicesReady();
});
it.each(["deleted", "failed", "pending"])("blocks a newly selected listing class after a %s refresh without clearing it", async kind => {
  const user = userEvent.setup(), save = edit(); await choicesReady(); await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), "books");
  const held = deferred<TaxClass[]>();
  if (kind === "deleted") classes = [standard];
  if (kind === "failed") vi.mocked(api.taxClasses).mockRejectedValueOnce(new HttpError("http", 503));
  if (kind === "pending") vi.mocked(api.taxClasses).mockReturnValueOnce(held.promise);
  await user.click(screen.getByRole("button", { name: "Refresh class choices" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "Save listing" }).matches(":disabled")).toBe(true));
  expect(screen.getByRole("combobox", { name: "Tax class" })).toHaveProperty("value", "books"); fireEvent.submit(document.querySelector("form")!); expect(save).not.toHaveBeenCalled();
  if (kind === "pending") { await act(async () => held.resolve([standard])); }
  await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), ""); await user.click(screen.getByRole("button", { name: "Save listing" })); expect(save.mock.calls[0][0].taxClass).toBeNull();
});
it("creates a listing with the chosen class and leaves the physical product reference intact", async () => {
  const user = userEvent.setup(), save = vi.fn().mockResolvedValue(undefined);
  render(<MemoryRouter><ListingForm storeId="a" products={[product]} disabled={false} pending={false} onSave={save} /></MemoryRouter>); await choicesReady();
  await user.selectOptions(screen.getByRole("combobox", { name: "Product to list…" }), "product");
  await user.type(screen.getByRole("textbox", { name: "Name" }), "New book"); await user.type(screen.getByRole("textbox", { name: "Price" }), "100");
  await user.selectOptions(screen.getByRole("combobox", { name: "Tax class" }), "books"); await user.click(screen.getByRole("button", { name: "List product" }));
  expect(save).toHaveBeenCalledWith(expect.objectContaining({ taxClass: "books", price: 100, vatRate: 21 }), "product");
});
it("guards a disabled listing form against direct submission", async () => {
  const save = edit(listing, true); await choicesReady(); fireEvent.submit(document.querySelector("form")!); expect(save).not.toHaveBeenCalled();
});
it.each(["en", "cs"])("labels assignment controls and renders no inline styles in %s", async language => {
  await i18n.changeLanguage(language); edit(); await choicesReady(); const label = language === "cs" ? "Daňová třída" : "Tax class";
  expect(screen.getByRole("combobox", { name: label }).getAttribute("aria-describedby")).toBe("listing-taxClass-hint"); expect(document.querySelector("[style],style,script")).toBeNull();
});
it.each(["books", ""])("saves shipping class %s with every editable saved field, including zero and false", async value => {
  const user = userEvent.setup(); await shippingApp(); await choicesReady(); const form = parcelForm();
  await user.selectOptions(within(form).getByRole("combobox", { name: "Tax class" }), value); await user.click(within(form).getByRole("button", { name: "Save" }));
  await waitFor(() => expect(api.updateShippingMethod).toHaveBeenCalledExactlyOnceWith("a", "parcel", {
    name: "Parcel", price: 0, vatRate: 0, isActive: false, requiresPickupPoint: false, maxWeightGrams: 5000, countries: ["CZ", "SK"], taxClass: value || null,
  }));
  await waitFor(() => expect(screen.queryByRole("form", { name: "Parcel" })).toBeNull()); expect(api.shippingMethods).toHaveBeenCalledTimes(2);
  await waitFor(() => expect(document.activeElement).toBe(document.querySelector(".shipping-method-result")));
});
it("creates shipping with an assigned class through the existing POST contract", async () => {
  const user = userEvent.setup(); await shippingApp(true, false, false); await user.click(await screen.findByRole("button", { name: "Add Packeta delivery" })); await choicesReady(); const form = screen.getByRole("form", { name: "New shipping method" });
  await user.type(within(form).getByRole("textbox", { name: "Method name" }), "New parcel");
  await user.selectOptions(within(form).getByRole("combobox", { name: "Shipping provider" }), "packeta");
  await user.selectOptions(within(form).getByRole("combobox", { name: "Packeta delivery mode" }), "address"); await user.type(within(form).getByRole("textbox", { name: "Price" }), "0");
  await user.selectOptions(within(form).getByRole("combobox", { name: "Tax class" }), "books"); await user.click(within(form).getByRole("button", { name: "Add" }));
  await waitFor(() => expect(api.createShippingMethod).toHaveBeenCalledExactlyOnceWith("a", { name: "New parcel", providerKey: "packeta", price: 0, vatRate: 21, isActive: true, requiresPickupPoint: false, maxWeightGrams: null, countries: [], taxClass: "books" }));
});
it("does not silently clear shipping classes when lookup fails", async () => {
  vi.mocked(api.taxClasses).mockRejectedValue(new HttpError("http", 503)); const user = userEvent.setup(); await shippingApp(); const form = await screen.findByRole("form", { name: "Parcel" });
  await user.click(within(form).getByRole("button", { name: "Save" })); await waitFor(() => expect(api.updateShippingMethod).toHaveBeenCalledOnce()); expect(vi.mocked(api.updateShippingMethod).mock.calls[0][2].taxClass).toBe("standard");
});
it("reads back a lost shipping answer, blocks failed reads and never replays on retry", async () => {
  vi.mocked(api.updateShippingMethod).mockImplementationOnce(async (_store, _code, input) => { methods = [{ ...shipping, ...input }]; throw new HttpError("network", null); });
  const user = userEvent.setup(); await shippingApp(); await choicesReady(); const form = parcelForm(); await user.selectOptions(within(form).getByRole("combobox", { name: "Tax class" }), "books");
  vi.mocked(api.shippingMethods).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(within(form).getByRole("button", { name: "Save" }));
  await screen.findByText(/The outcome is not confirmed/); expect(screen.getByRole("button", { name: "Edit shipping method Parcel" }).matches(":disabled")).toBe(true); expect(api.updateShippingMethod).toHaveBeenCalledOnce();
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Finish review" }).matches(":disabled")).toBe(false));
  await user.click(screen.getByRole("button", { name: "Finish review" })); await user.click(screen.getByRole("button", { name: "Edit shipping method Parcel" }));
  expect(within(parcelForm()).getByRole("combobox", { name: "Tax class" })).toHaveProperty("value", "books"); expect(api.updateShippingMethod).toHaveBeenCalledOnce();
});
it("locks simultaneous shipping saves until the request and fresh read complete", async () => {
  const held = deferred<ShippingMethod>(); vi.mocked(api.updateShippingMethod).mockReturnValueOnce(held.promise); await shippingApp(); await choicesReady();
  const form = parcelForm(); fireEvent.submit(form); fireEvent.submit(form); expect(api.updateShippingMethod).toHaveBeenCalledOnce();
  expect(screen.getAllByRole("button", { name: /Save|Add/ }).every(button => button.matches(":disabled"))).toBe(true);
  await act(async () => held.resolve(shipping)); await waitFor(() => expect(screen.getByRole("button", { name: "Edit shipping method Parcel" }).matches(":disabled")).toBe(false));
});
it.each([[false, false], [true, true]])("guards shipping handlers when allowed=%s, stale=%s", async (allowed, stale) => {
  const view = await shippingApp(); const form = parcelForm();
  view.rerender(<MemoryRouter><ShippingMethodsSection storeId="a" money={money} allowed={allowed} storeStale={stale} /></MemoryRouter>); fireEvent.submit(form);
  expect(api.updateShippingMethod).not.toHaveBeenCalled(); expect(within(form).getByRole("button", { name: "Save" }).matches(":disabled")).toBe(true);
  // The already-open editor cannot write after its authorization changes.
});

it("retains a refused shipping tax choice and focuses its localized field error after the read", async () => {
  vi.mocked(api.updateShippingMethod).mockRejectedValueOnce(new HttpError("http", 400, { errors: { TaxClass: ["Unknown class"] } }));
  const user = userEvent.setup(); await shippingApp(); await choicesReady(); await user.selectOptions(within(parcelForm()).getByRole("combobox", { name: "Tax class" }), "books");
  await user.click(within(parcelForm()).getByRole("button", { name: "Save" })); await screen.findByText(/The request was refused/);
  await waitFor(() => expect(document.activeElement).toBe(document.querySelector(".shipping-method-result")));
  await user.click(screen.getByRole("button", { name: "Review this method field." })); expect(document.activeElement).toBe(within(parcelForm()).getByRole("combobox", { name: "Tax class" }));
  expect(within(parcelForm()).getByRole("combobox", { name: "Tax class" })).toHaveProperty("value", "books"); expect(api.updateShippingMethod).toHaveBeenCalledOnce();
});
