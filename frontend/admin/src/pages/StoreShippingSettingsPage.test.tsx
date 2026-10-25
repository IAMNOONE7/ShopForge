// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { HttpError } from "../api/http";
import { shippingSettingsApi, type ShippingSettings } from "../api/shippingSettings";
import { initializeI18n, i18n } from "../i18n";
import { SessionContext } from "../session";
import { metadataStore } from "../test/metadataFixtures";
import { StoreShippingSettingsPage } from "./StoreShippingSettingsPage";

let current: ShippingSettings;
const defaults: ShippingSettings = { packagingWeightGrams: 300, fillPercent: 70, largestParcelOrder: 10 };
const account = (role = "Owner") => ({ id: "owner", email: "owner@example.test", role, tenantId: "tenant-a" });
function Shell({ role: initialRole }: { role: string }) {
  const [store, setStore] = useState(metadataStore), [role, setRole] = useState(initialRole), [staff, setStaff] = useState("owner"), [refreshing, setRefreshing] = useState(false);
  return <SessionContext value={{ user: { ...account(role), id: staff }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => setStore({ ...metadataStore, id: "store-b" })}>Other store</button><button onClick={() => setRole("Support")}>Other role</button><button onClick={() => setStaff("other-owner")}>Other session</button>
    <button onClick={() => setRefreshing(value => !value)}>Toggle store refresh</button><button onClick={() => setStore({ ...store, name: "New store name" })}>Rename store</button>
    <Outlet context={{ store, storeRefreshing: refreshing, storeRefreshError: null, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function route(role = "Owner") { return render(<MemoryRouter initialEntries={["/settings"]}><Routes><Route element={<Shell role={role} />}><Route path="settings" element={<StoreShippingSettingsPage />} /></Route></Routes></MemoryRouter>); }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
const edit = async (user: ReturnType<typeof userEvent.setup>) => user.click(await screen.findByRole("button", { name: i18n.t("shippingSettings:edit") }));
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); current = { ...defaults }; vi.stubGlobal("CSS", { escape: (value: string) => value }); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.spyOn(shippingSettingsApi, "read").mockImplementation(async () => ({ ...current }));
  vi.spyOn(shippingSettingsApi, "save").mockImplementation(async (_store, input) => { current = { ...input }; return { ...current }; });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.history.replaceState(null, "", "/"); });
describe("store parcel settings", () => {
  it.each(["en", "cs"])("saves exact boundary values and restores cancel focus in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); route(); await edit(user);
    const weight = screen.getByLabelText(i18n.t("shippingSettings:packagingWeightGrams")); expect(document.activeElement).toBe(weight);
    await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: i18n.t("shippingSettings:edit") })));
    await edit(user);
    for (const [field, value] of Object.entries({ packagingWeightGrams: "0", fillPercent: "100", largestParcelOrder: "100" })) fireEvent.change(document.getElementById("shipping-settings-" + field)!, { target: { value } });
    await user.click(screen.getByRole("button", { name: i18n.t("shippingSettings:save") })); await screen.findByText(i18n.t("shippingSettings:saved"));
    expect(shippingSettingsApi.save).toHaveBeenCalledExactlyOnceWith(metadataStore.id, { packagingWeightGrams: 0, fillPercent: 100, largestParcelOrder: 100 }, expect.any(AbortSignal));
    expect(screen.queryByRole("textbox")).toBeNull(); expect(shippingSettingsApi.read).toHaveBeenCalledTimes(2);
  });
  it("validates blanks/fractions and focuses the local error before requesting", async () => {
    const user = userEvent.setup(); route(); await edit(user);
    fireEvent.change(screen.getByLabelText("Packaging weight per parcel (g)"), { target: { value: "" } }); fireEvent.change(screen.getByLabelText("Space for products (%)"), { target: { value: "70.5" } });
    await user.click(screen.getByRole("button", { name: "Save parcel settings" })); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("alert")));
    await user.click(within(screen.getByRole("alert")).getAllByRole("button")[0]); expect(document.activeElement).toBe(screen.getByLabelText("Packaging weight per parcel (g)")); expect(shippingSettingsApi.save).not.toHaveBeenCalled();
  });
  it.each(["packagingWeightGrams", "fillPercent", "largestParcelOrder"] as const)("maps server %s errors without clearing the draft", async field => {
    const user = userEvent.setup(); route(); await edit(user); vi.mocked(shippingSettingsApi.save).mockRejectedValueOnce(new HttpError("http", 400, { errors: { [field]: ["Invalid"] } }));
    await user.click(screen.getByRole("button", { name: "Save parcel settings" })); await user.click(await screen.findByRole("button", { name: i18n.t("shippingSettings:" + field + "Invalid", { defaultValue: "" }) }));
    expect(document.activeElement).toBe(document.getElementById("shipping-settings-" + field)); expect(document.getElementById("shipping-settings-fillPercent")).toHaveProperty("value", "70"); expect(shippingSettingsApi.save).toHaveBeenCalledOnce();
  });
  it.each(["settings", "store"])("blocks retained drafts after observed %s changes", async change => {
    const user = userEvent.setup(); route(); await edit(user); fireEvent.change(screen.getByLabelText("Space for products (%)"), { target: { value: "80" } });
    if (change === "settings") { current = { ...defaults, packagingWeightGrams: 400 }; await user.click(screen.getByRole("button", { name: "Refresh parcel settings" })); } else await user.click(screen.getByRole("button", { name: "Rename store" }));
    await screen.findByText(i18n.t("shippingSettings:changed")); expect(screen.getByLabelText("Space for products (%)")).toHaveProperty("value", "80");
    fireEvent.submit(screen.getByRole("form")); expect(shippingSettingsApi.save).not.toHaveBeenCalled();
  });
  it("locks a submit racing a current read and blocks on refreshing store details", async () => {
    const user = userEvent.setup(), held = deferred<ShippingSettings>(); route(); await edit(user); vi.mocked(shippingSettingsApi.read).mockReturnValueOnce(held.promise);
    act(() => { fireEvent.click(screen.getByRole("button", { name: "Refresh parcel settings" })); fireEvent.submit(screen.getByRole("form")); }); expect(shippingSettingsApi.save).not.toHaveBeenCalled();
    await act(async () => held.resolve(current)); await user.click(screen.getByRole("button", { name: "Toggle store refresh" })); fireEvent.submit(screen.getByRole("form")); expect(shippingSettingsApi.save).not.toHaveBeenCalled();
  });
  it("locks duplicate writes and retains an adopted reply after a failed refresh", async () => {
    const user = userEvent.setup(), held = deferred<ShippingSettings>(); route(); await edit(user);
    fireEvent.change(screen.getByLabelText("Space for products (%)"), { target: { value: "80" } });
    vi.mocked(shippingSettingsApi.save).mockReturnValueOnce(held.promise); vi.mocked(shippingSettingsApi.read).mockRejectedValueOnce(new HttpError("http", 503));
    const form = screen.getByRole("form"); act(() => { fireEvent.submit(form); fireEvent.submit(form); }); expect(shippingSettingsApi.save).toHaveBeenCalledOnce();
    await act(async () => held.resolve({ ...defaults, fillPercent: 80 })); await screen.findByText("Parcel settings saved."); await screen.findByRole("button", { name: "Try again" });
    expect(screen.getByRole("button", { name: "Edit parcel settings" })).toHaveProperty("disabled", true); current.fillPercent = 80;
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Edit parcel settings" })).toHaveProperty("disabled", false)); expect(shippingSettingsApi.save).toHaveBeenCalledOnce();
  });
  it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 200)])("requires a fresh read and explicit current-values review after uncertainty: %j", async error => {
    const user = userEvent.setup(); route(); await edit(user); vi.mocked(shippingSettingsApi.save).mockRejectedValueOnce(error); vi.mocked(shippingSettingsApi.read).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Save parcel settings" })); await screen.findByText(i18n.t("shippingSettings:uncertain"));
    expect(screen.getByRole("button", { name: "Finish review and use current values" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Finish review and use current values" })).toHaveProperty("disabled", false));
    expect(screen.getByRole("button", { name: "Save parcel settings" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Finish review and use current values" })); expect(screen.queryByRole("form")).toBeNull(); expect(shippingSettingsApi.save).toHaveBeenCalledOnce();
  });
  it.each([401, 403, 404])("hides earlier settings after a %i read", async code => {
    const user = userEvent.setup(); route(); await edit(user); vi.mocked(shippingSettingsApi.read).mockRejectedValueOnce(new HttpError("http", code)); await user.click(screen.getByRole("button", { name: "Refresh parcel settings" }));
    await screen.findByRole("alert"); expect(screen.queryByRole("form")).toBeNull(); expect(screen.queryByRole("heading", { name: "Current parcel settings" })).toBeNull();
  });
  it.each(["store", "role", "session", "unmount"])("aborts writes and ignores late replies on %s departure", async departure => {
    const user = userEvent.setup(), held = deferred<ShippingSettings>(), view = route(); await edit(user); vi.mocked(shippingSettingsApi.save).mockReturnValueOnce(held.promise); await user.click(screen.getByRole("button", { name: "Save parcel settings" }));
    if (departure === "unmount") view.unmount(); else await user.click(screen.getByRole("button", { name: "Other " + departure }));
    expect(vi.mocked(shippingSettingsApi.save).mock.calls[0][2]?.aborted).toBe(true); const count = vi.mocked(shippingSettingsApi.read).mock.calls.length;
    await act(async () => held.resolve(defaults)); expect(screen.queryByText("Parcel settings saved.")).toBeNull(); expect(shippingSettingsApi.read).toHaveBeenCalledTimes(count);
  });
  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("uses actual direct navigation and write permissions for %s", async role => {
    vi.spyOn(api, "me").mockResolvedValue(account(role)); vi.spyOn(api, "stores").mockResolvedValue([metadataStore]); window.history.replaceState(null, "", "/stores/" + metadataStore.id + "/shipping-settings"); render(<App />);
    await screen.findByRole("heading", { name: "Current parcel settings" });
    expect(screen.getByRole("link", { name: "Parcel settings" }).getAttribute("href")).toBe("/stores/" + metadataStore.id + "/shipping-settings");
    expect(screen.queryByRole("button", { name: "Edit parcel settings" }) !== null).toBe(["Owner", "Admin"].includes(role)); expect(shippingSettingsApi.save).not.toHaveBeenCalled();
    await waitFor(() => expect(shippingSettingsApi.read).toHaveBeenCalledTimes(1));
  });
});
