// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type ShippingMethod } from "../api";
import { HttpError } from "../api/http";
import { parcelShippingApi, quoteReasons, type Quote } from "../api/parcelShipping";
import { initializeI18n, i18n } from "../i18n";
import { metadataListing, metadataStore } from "../test/metadataFixtures";
import { parcelForms, parcelInput, parcelMethod, parcelOtherVariantId, parcelQuote, parcelService, parcelVariantId } from "../test/parcelFixtures";
import { MethodsSection } from "./MethodsSection";
import { ShippingQuotePanel } from "./ShippingQuotePanel";
let current: ShippingMethod;
const money = (currency = "CZK") => new Intl.NumberFormat("en", { style: "currency", currency });
function app(currency = "CZK") { return <MemoryRouter><MethodsSection storeId={metadataStore.id} money={money(currency)} canManage storeStale={false} /></MemoryRouter>; }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
const terms = () => screen.getByRole("form", { name: "Z-BOX" });
const rates = () => screen.getByRole("form", { name: i18n.t("parcelShipping:rateEditor", { name: "Z-BOX" }) });
async function edit(user: ReturnType<typeof userEvent.setup>) { await user.click(await screen.findByRole("button", { name: i18n.t("methodSetup:editShippingNamed", { name: "Z-BOX" }) })); }
async function chooseLine(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: i18n.t("parcelShipping:chooseProduct", { name: metadataListing.name }) }));
  await screen.findByRole("option", { name: "CHAIR-S · S" });
  await user.selectOptions(screen.getByLabelText(i18n.t("parcelShipping:variant")), parcelVariantId);
  fireEvent.change(screen.getByLabelText(i18n.t("parcelShipping:quantity")), { target: { value: "2" } });
  await user.click(screen.getByRole("button", { name: i18n.t("parcelShipping:addLine") }));
}
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); current = structuredClone(parcelMethod); HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (text: string) => text });
  vi.spyOn(api, "shippingMethods").mockImplementation(async () => [current]); vi.spyOn(api, "shippingProviders").mockResolvedValue(["packeta"]);
  vi.spyOn(api, "paymentMethods").mockResolvedValue([]); vi.spyOn(api, "paymentProviders").mockResolvedValue([]); vi.spyOn(api, "providerConnections").mockResolvedValue([]); vi.spyOn(api, "pickupPoints").mockResolvedValue([]);
  vi.spyOn(api, "storeReadiness").mockResolvedValue({ isReady: false, checks: [{ check: "orders.parcel-methods", isReady: false, weight: "required", problem: "Z-BOX still has placeholder prices." }] });
  vi.spyOn(api, "taxClasses").mockResolvedValue([{ code: "standard", name: "Standard", rates: [] }]);
  vi.spyOn(parcelShippingApi, "services").mockResolvedValue([parcelService, { ...parcelService, key: "home", name: "Home CZ", kind: "home" }]);
  vi.spyOn(parcelShippingApi, "rates").mockImplementation(async (_store, method, card) => { current = { ...method, ratesFrom: card.source, ratesPricedOn: card.pricedOn, rateBands: card.bands }; return current; });
  vi.spyOn(parcelShippingApi, "forms").mockResolvedValue(parcelForms); vi.spyOn(parcelShippingApi, "quote").mockResolvedValue(parcelQuote);
  vi.spyOn(api, "storeProducts").mockResolvedValue({ items: [metadataListing], totalCount: 1, hasMore: false, page: 1, pageSize: 25 });
  vi.spyOn(api, "updateShippingMethod").mockImplementation(async (_store, _code, input) => { current = { ...current, ...input }; return current; });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.history.replaceState(null, "", "/"); });
describe("per-parcel configuration", () => {
  it("shows real saved rate provenance without labeling the ignored flat price as the parcel price", async () => {
    render(app()); await screen.findByRole("heading", { name: "Z-BOX" }); expect(screen.getByText("Price per parcel")).toBeTruthy(); expect(screen.getByText(/Price source: Placeholder examples/)).toBeTruthy(); expect(screen.getByText("Z-BOX still has placeholder prices.")).toBeTruthy(); expect(parcelShippingApi.services).not.toHaveBeenCalled(); expect(parcelShippingApi.forms).not.toHaveBeenCalled();
  });
  it("preserves saved terms while deactivating an unoffered service without sending pricing", async () => {
    vi.mocked(parcelShippingApi.services).mockResolvedValue([]); const user = userEvent.setup(); current.isActive = true; render(app()); await edit(user);
    await user.click(within(terms()).getByRole("checkbox", { name: "Active" })); await user.click(within(terms()).getByRole("button", { name: "Save" }));
    await waitFor(() => expect(api.updateShippingMethod).toHaveBeenCalledOnce()); const input = vi.mocked(api.updateShippingMethod).mock.calls[0][2]; expect(input).not.toHaveProperty("pricing"); expect(input.isActive).toBe(false); expect(input.price).toBe(0); expect(input.taxClass).toBe("standard");
  });
  it.each(["en", "cs"])("selects a matching home service and sends all parcel limits in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); render(app()); await edit(user); await screen.findByRole("option", { name: "Home CZ · CZ" });
    await user.selectOptions(screen.getByRole("combobox", { name: i18n.t("parcelShipping:service") }), "home");
    for (const [key, value] of Object.entries({ parcelMaxWeightGrams: "1000", parcelLengthMm: "100", parcelWidthMm: "200", parcelHeightMm: "300" })) fireEvent.change(document.querySelector('[name="' + key + '"]')!, { target: { value } });
    fireEvent.submit(terms()); await waitFor(() => expect(api.updateShippingMethod).toHaveBeenCalledWith(metadataStore.id, parcelMethod.code, expect.objectContaining({ pricing: "perParcel", carrierService: "home", requiresPickupPoint: false, maxWeightGrams: null, countries: ["CZ"], parcelMaxWeightGrams: 1000, parcelLengthMm: 100, parcelWidthMm: 200, parcelHeightMm: 300 }), expect.any(AbortSignal)));
    expect(parcelShippingApi.rates).not.toHaveBeenCalled();
  });
  it("requires all three sides and refuses limits above the active service weight", async () => {
    const user = userEvent.setup(); render(app()); await edit(user); await screen.findByRole("option", { name: "Z-BOX CZ · CZ" });
    fireEvent.change(screen.getByLabelText("Parcel length limit (mm)"), { target: { value: "100" } }); fireEvent.submit(terms()); expect(api.updateShippingMethod).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("Parcel weight limit (g)"), { target: { value: "15001" } }); fireEvent.submit(terms()); expect(api.updateShippingMethod).not.toHaveBeenCalled(); expect(within(terms()).getAllByText(i18n.t("parcelShipping:limitInvalid")).length).toBeGreaterThan(0);
  });
  it("creates a measured per-parcel method first and does not automatically write a guessed rate card", async () => {
    const user = userEvent.setup(), create = vi.spyOn(api, "createShippingMethod").mockResolvedValue({ ...parcelMethod, code: "new", name: "New delivery", isActive: true, taxClass: null }); render(app());
    await user.click(await screen.findByRole("button", { name: "Add Packeta delivery" })); const form = screen.getByRole("form", { name: "New shipping method" });
    fireEvent.change(within(form).getByRole("textbox", { name: "Method name" }), { target: { value: "New delivery" } }); await user.selectOptions(within(form).getByLabelText("Shipping provider"), "packeta"); await user.selectOptions(within(form).getByLabelText("Pricing"), "perParcel"); await screen.findByRole("option", { name: "Z-BOX CZ · CZ" });
    await user.selectOptions(within(form).getByLabelText("Carrier service"), parcelService.key); fireEvent.submit(form); await waitFor(() => expect(create).toHaveBeenCalledOnce()); expect(create.mock.calls[0][1]).toMatchObject({ pricing: "perParcel", carrierService: parcelService.key, price: 0, requiresPickupPoint: true, countries: ["CZ"], maxWeightGrams: null }); expect(parcelShippingApi.rates).not.toHaveBeenCalled();
  });
  it.each(["CZK", "JPY", "KWD"])("replaces the whole saved card with exact %s values and keeps term saving paused", async currency => {
    const user = userEvent.setup(); render(app(currency)); await edit(user); const form = rates(), price = within(form).getByLabelText("Band 1 price (" + currency + ")");
    fireEvent.change(price, { target: { value: currency === "KWD" ? "0.123" : "0" } }); await waitFor(() => expect(within(terms()).getByRole("button", { name: "Save" })).toHaveProperty("disabled", true));
    await user.selectOptions(within(form).getByLabelText("Price source"), "own"); fireEvent.submit(form); await waitFor(() => expect(parcelShippingApi.rates).toHaveBeenCalledOnce());
    expect(vi.mocked(parcelShippingApi.rates).mock.calls[0][2]).toMatchObject({ source: "own", pricedOn: null, bands: [{ upToGrams: 2000, price: currency === "KWD" ? 0.123 : 0 }, { upToGrams: 5000, price: 99 }] }); expect(api.updateShippingMethod).not.toHaveBeenCalled();
  });
  it.each(["missing-date", "unordered", "fractional-weight", "excess-decimals"])("rejects a %s card locally with focus and retained draft", async issue => {
    const user = userEvent.setup(); render(app()); await edit(user); const form = rates();
    if (issue === "missing-date") await user.selectOptions(within(form).getByLabelText("Price source"), "carrier");
    if (issue === "unordered") fireEvent.change(within(form).getByLabelText("Band 2 up to (g)"), { target: { value: "1000" } });
    if (issue === "fractional-weight") fireEvent.change(within(form).getByLabelText("Band 1 up to (g)"), { target: { value: "1.5" } });
    if (issue === "excess-decimals") fireEvent.change(within(form).getByLabelText("Band 1 price (CZK)"), { target: { value: "1.001" } });
    fireEvent.submit(form); await waitFor(() => expect(document.activeElement).toBe(within(form).getByRole("alert"))); expect(parcelShippingApi.rates).not.toHaveBeenCalled();
  });
  it("requires and sends the actual carrier price-list date", async () => {
    const user = userEvent.setup(); render(app()); await edit(user); const form = rates(); await user.selectOptions(within(form).getByLabelText("Price source"), "carrier"); fireEvent.change(within(form).getByLabelText("Price-list date"), { target: { value: "2026-10-27" } }); fireEvent.submit(form);
    await waitFor(() => expect(parcelShippingApi.rates).toHaveBeenCalledOnce()); expect(vi.mocked(parcelShippingApi.rates).mock.calls[0][2]).toMatchObject({ source: "carrier", pricedOn: "2026-10-27" });
  });
  it("shows the card-clearing warning and sends an explicit flat conversion without parcel limits", async () => {
    const user = userEvent.setup(); render(app()); await edit(user); await user.selectOptions(within(terms()).getByLabelText("Pricing"), "flat"); await screen.findByText(i18n.t("parcelShipping:clearRatesWarning")); fireEvent.submit(terms());
    await waitFor(() => expect(api.updateShippingMethod).toHaveBeenCalledOnce()); expect(vi.mocked(api.updateShippingMethod).mock.calls[0][2]).toMatchObject({ pricing: "flat", carrierService: null, parcelMaxWeightGrams: null, parcelLengthMm: null, parcelWidthMm: null, parcelHeightMm: null }); expect(parcelShippingApi.rates).not.toHaveBeenCalled();
  });
  it("does not submit a rate draft after an observed method change", async () => {
    const user = userEvent.setup(); render(app()); await edit(user); const form = rates(); await user.selectOptions(within(form).getByLabelText("Price source"), "own"); current = { ...current, parcelMaxWeightGrams: 1000 }; await user.click(screen.getByRole("button", { name: "Refresh methods and checks" })); await screen.findByText(i18n.t("methodSetup:draftStale")); fireEvent.submit(form); expect(parcelShippingApi.rates).not.toHaveBeenCalled();
  });
  it("discards rate edits and restores independent term editing without saving", async () => {
    const user = userEvent.setup(); render(app()); await edit(user); const form = rates(); fireEvent.change(within(form).getByLabelText("Band 1 price (CZK)"), { target: { value: "0" } });
    await user.click(within(form).getByRole("button", { name: "Discard rate-card edits" })); await waitFor(() => expect(within(terms()).getByRole("button", { name: "Save" })).toHaveProperty("disabled", false)); expect(within(form).getByLabelText("Band 1 price (CZK)")).toHaveProperty("value", "79"); expect(parcelShippingApi.rates).not.toHaveBeenCalled();
  });
  it("preserves a refused card and exposes the exact numerical server field reason", async () => {
    const user = userEvent.setup(); vi.mocked(parcelShippingApi.rates).mockRejectedValueOnce(new HttpError("http", 400, { errors: { Bands: ["Every band exceeds 0 g, at most 50 bands. <b>literal</b>"] } })); render(app()); await edit(user);
    await user.selectOptions(within(rates()).getByLabelText("Price source"), "own"); fireEvent.submit(rates()); await screen.findByText("Every band exceeds 0 g, at most 50 bands. <b>literal</b>"); expect(document.querySelector(".method-result b")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Review this method field." })); expect(document.activeElement?.id).toBe("shipping-zbox-bands"); expect(within(rates()).getByLabelText("Price source")).toHaveProperty("value", "own"); expect(parcelShippingApi.rates).toHaveBeenCalledOnce();
  });
  it("locks duplicate card writes and aborts a departed editor without replay or late reload", async () => {
    const user = userEvent.setup(), held = deferred<ShippingMethod>(); vi.mocked(parcelShippingApi.rates).mockReturnValueOnce(held.promise); const view = render(app()); await edit(user); const form = rates();
    act(() => { fireEvent.submit(form); fireEvent.submit(form); }); expect(parcelShippingApi.rates).toHaveBeenCalledOnce(); const signal = vi.mocked(parcelShippingApi.rates).mock.calls[0][3]; view.unmount(); expect(signal?.aborted).toBe(true); const reads = vi.mocked(api.shippingMethods).mock.calls.length;
    await act(async () => held.resolve(parcelMethod)); expect(api.shippingMethods).toHaveBeenCalledTimes(reads);
  });
  it("requires current-read review after an uncertain applied rate replacement", async () => {
    const user = userEvent.setup(); vi.mocked(parcelShippingApi.rates).mockRejectedValueOnce(new HttpError("network", null)); render(app()); await edit(user); fireEvent.submit(rates());
    await screen.findByText(i18n.t("methodSetup:uncertain")); expect(screen.queryByRole("form", { name: "Rate card for Z-BOX" })).toBeNull(); const editButton = screen.getByRole("button", { name: "Edit shipping method Z-BOX" }); expect(editButton).toHaveProperty("disabled", true);
    await waitFor(() => expect(screen.getByRole("button", { name: "Finish review" })).toHaveProperty("disabled", false)); await user.click(screen.getByRole("button", { name: "Finish review" })); expect(parcelShippingApi.rates).toHaveBeenCalledOnce();
  });
});
describe("read-only cart previews", () => {
  function panel(stale = false) { return <MemoryRouter><ShippingQuotePanel storeId={metadataStore.id} method={parcelMethod} money={money()} stale={stale} onClose={vi.fn()} /></MemoryRouter>; }
  it.each(["en", "cs"])("uses the selected listing and exact variant, shows authoritative parcels, and clears changed input in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); render(panel()); await chooseLine(user); fireEvent.change(screen.getByLabelText(i18n.t("parcelShipping:country")), { target: { value: "cz" } });
    await user.click(screen.getByRole("button", { name: i18n.t("parcelShipping:quote") })); await screen.findByRole("heading", { name: i18n.t("parcelShipping:quoteResult") });
    expect(parcelShippingApi.quote).toHaveBeenCalledExactlyOnceWith(metadataStore.id, parcelMethod, "CZK", parcelInput, expect.any(AbortSignal)); expect(screen.getByText(i18n.t("parcelShipping:alone"))).toBeTruthy();
    fireEvent.change(screen.getByLabelText(i18n.t("parcelShipping:lineQuantity", { number: 1 })), { target: { value: "1" } }); expect(screen.queryByRole("heading", { name: i18n.t("parcelShipping:quoteResult") })).toBeNull();
  });
  it("requires a selected exact form and quantities within 1–99, without choosing the first form implicitly", async () => {
    const user = userEvent.setup(); render(panel()); await user.click(await screen.findByRole("button", { name: "Choose Žlutá židle" })); await screen.findByRole("option", { name: "CHAIR-L · L" }); expect(screen.getByLabelText("Product form")).toHaveProperty("value", ""); expect(screen.getByRole("button", { name: "Add form to preview cart" })).toHaveProperty("disabled", true);
    await user.selectOptions(screen.getByLabelText("Product form"), parcelOtherVariantId); fireEvent.change(screen.getByLabelText("Quantity to add"), { target: { value: "100" } }); expect(screen.getByRole("button", { name: "Add form to preview cart" })).toHaveProperty("disabled", true); expect(parcelShippingApi.quote).not.toHaveBeenCalled();
  });
  it.each(quoteReasons)("renders an authoritative %s refusal without a false price or mutation", async reason => {
    const user = userEvent.setup(); vi.mocked(parcelShippingApi.quote).mockResolvedValueOnce({ isAvailable: false, currency: "CZK", total: 0, parcels: [], unavailable: { reason, detail: "Limit 2000 g <img src=x>", variantId: parcelVariantId } }); render(panel()); await chooseLine(user); await user.click(screen.getByRole("button", { name: "Preview shipping quote" })); await screen.findByText(i18n.t(`parcelShipping:reasons.${reason}`)); expect(screen.getByText("Limit 2000 g <img src=x>")).toBeTruthy(); expect(document.querySelector(".quote-feedback img")).toBeNull(); expect(screen.queryByText("Shipping total:")).toBeNull(); expect(parcelShippingApi.rates).not.toHaveBeenCalled();
  });
  it("aborts an out-of-date preview and ignores its late answer after cart edits", async () => {
    const user = userEvent.setup(), held = deferred<Quote>(); vi.mocked(parcelShippingApi.quote).mockReturnValueOnce(held.promise); render(panel()); await chooseLine(user); fireEvent.submit(screen.getByRole("button", { name: "Preview shipping quote" }).closest("form")!);
    fireEvent.change(screen.getByLabelText("Destination country"), { target: { value: "sk" } }); expect(vi.mocked(parcelShippingApi.quote).mock.calls[0][4]?.aborted).toBe(true); await act(async () => held.resolve(parcelQuote)); expect(screen.queryByRole("heading", { name: "Shipping preview result" })).toBeNull();
  });
  it("clears a completed quote when method/store reads become stale and does not resurrect it", async () => {
    const user = userEvent.setup(), view = render(panel()); await chooseLine(user); await user.click(screen.getByRole("button", { name: "Preview shipping quote" })); await screen.findByRole("heading", { name: "Shipping preview result" }); view.rerender(panel(true)); expect(screen.queryByRole("heading", { name: "Shipping preview result" })).toBeNull(); view.rerender(panel()); expect(screen.queryByRole("heading", { name: "Shipping preview result" })).toBeNull();
  });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("allows the actual route’s read-only quote controls for %s without exposing rate writes", async role => {
    const user = userEvent.setup(); vi.spyOn(api, "me").mockResolvedValue({ id: "user", email: "staff@example.test", role, tenantId: "tenant" }); vi.spyOn(api, "stores").mockResolvedValue([metadataStore]); window.history.replaceState(null, "", "/stores/" + metadataStore.id + "/methods"); render(<App />);
    await user.click(await screen.findByRole("button", { name: "Try a cart with Z-BOX" })); await chooseLine(user); await user.click(screen.getByRole("button", { name: "Preview shipping quote" })); await screen.findByRole("heading", { name: "Shipping preview result" }); expect(parcelShippingApi.rates).not.toHaveBeenCalled(); if (!["Owner", "Admin"].includes(role)) expect(screen.queryByRole("button", { name: "Edit shipping method Z-BOX" })).toBeNull();
  });
});
