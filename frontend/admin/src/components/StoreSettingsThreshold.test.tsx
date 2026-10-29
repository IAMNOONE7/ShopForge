// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { api, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import { initializeI18n, i18n } from "../i18n";
import { StoreSettingsPage } from "../pages/StoreSettingsPage";
import { SessionContext } from "../session";
import { metadataStore } from "../test/metadataFixtures";
import { StoreSettingsSection } from "./StoreSettingsSection";

const store: AdminStore = { ...metadataStore, theme: { ...metadataStore.theme, secondaryColor: "#EEF4FF" }, freeShipping: { isOn: false, threshold: 1000 } };
const reload = vi.fn();
const editor = (selected = store, extra: Partial<React.ComponentProps<typeof StoreSettingsSection>> = {}) =>
  <MemoryRouter><StoreSettingsSection store={selected} reloadStores={reload} {...extra} /></MemoryRouter>;
const field = () => document.getElementsByName("freeShipping.threshold")[0] as HTMLInputElement;
const checkbox = () => document.getElementsByName("freeShipping.isOn")[0] as HTMLInputElement;
const submit = () => fireEvent.submit(document.querySelector("form.store-settings-form")!);
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(accept => { resolve = accept; }); return { promise, resolve }; }
beforeAll(async () => { await initializeI18n(); HTMLElement.prototype.scrollIntoView = vi.fn(); });
beforeEach(async () => {
  await i18n.changeLanguage("en"); reload.mockReset();
  vi.spyOn(api, "updateStore").mockImplementation(async (_id, input) => ({ ...store, ...input, company: input.company ?? store.company }));
  vi.spyOn(api, "storeReadiness").mockResolvedValue({ isReady: true, checks: [] });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe("free-delivery settings", () => {
  it.each(["en", "cs"])("hydrates the saved off state and retained amount without a write in %s", async language => {
    await i18n.changeLanguage(language); render(editor());
    expect(checkbox().checked).toBe(false); expect(field().value).toBe("1000");
    expect(screen.getByRole("textbox", { name: i18n.t("stores:freeShippingThreshold", { currency: "CZK" }) })).toBe(field());
    expect(api.updateStore).not.toHaveBeenCalled();
  });
  it.each([true, false])("omits unchanged free delivery on a general settings save (isOn %s)", async isOn => {
    const selected = { ...store, freeShipping: { isOn, threshold: 1000 } };
    vi.mocked(api.updateStore).mockResolvedValue(selected); render(editor(selected)); submit();
    await screen.findByText("Store settings were saved.");
    expect(vi.mocked(api.updateStore).mock.calls[0][1]).not.toHaveProperty("freeShipping");
    expect(vi.mocked(api.updateStore).mock.calls[0][1]).not.toHaveProperty("seo");
  });
  it.each([["CZK", "1234,50", 1234.5], ["JPY", "1234", 1234], ["KWD", "1234.567", 1234.567]] as const)("saves exact %s replacement values without rounding", async (currency, raw, threshold) => {
    const selected = { ...store, currency, freeShipping: { isOn: false, threshold: null } };
    vi.mocked(api.updateStore).mockImplementation(async (_id, input) => ({ ...selected, ...input }));
    render(editor(selected)); fireEvent.click(checkbox()); fireEvent.change(field(), { target: { value: raw } }); submit();
    await screen.findByText("Store settings were saved.");
    expect(vi.mocked(api.updateStore).mock.calls[0][1].freeShipping).toEqual({ isOn: true, threshold });
    expect(document.activeElement?.id).toBe("store-details-title");
    expect(reload).toHaveBeenCalledOnce();
  });
  it.each(["", "0", "-1", "1.234", "1e3", "1 000", "Infinity"])("retains and focuses an invalid enabled threshold %j without saving", async raw => {
    render(editor()); fireEvent.click(checkbox()); fireEvent.change(field(), { target: { value: raw } }); submit();
    await waitFor(() => expect(document.activeElement?.id).toBe("store-settings-validation"));
    expect(field().value).toBe(raw); expect(field().getAttribute("aria-invalid")).toBe("true"); expect(api.updateStore).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: /^Free-delivery threshold:/ })); expect(document.activeElement).toBe(field());
  });
  it.each([false, true])("turns off delivery and explicitly %s clears the threshold", async clear => {
    const selected = { ...store, freeShipping: { isOn: true, threshold: 1000 } };
    vi.mocked(api.updateStore).mockImplementation(async (_id, input) => ({ ...selected, ...input }));
    render(editor(selected)); fireEvent.click(checkbox()); if (clear) fireEvent.change(field(), { target: { value: "" } }); submit();
    await screen.findByText("Store settings were saved.");
    expect(vi.mocked(api.updateStore).mock.calls[0][1].freeShipping).toEqual({ isOn: false, threshold: clear ? null : 1000 });
  });
  it("checks the threshold against a changed draft currency, including a retained off amount", async () => {
    const selected = { ...store, status: "draft" as const, freeShipping: { isOn: false, threshold: 1000.5 } };
    render(editor(selected)); fireEvent.change(screen.getByRole("textbox", { name: "Currency" }), { target: { value: "JPY" } }); submit();
    await screen.findByRole("alert"); expect(api.updateStore).not.toHaveBeenCalled(); expect(field().value).toBe("1000.5");
    expect(screen.getByText(/Enter JPY with at most 0 decimal places/)).toBeTruthy();
  });
  it("shows a literal dotted-field refusal, retains all edits and allows correction", async () => {
    vi.mocked(api.updateStore).mockRejectedValueOnce(new HttpError("http", 400, { errors: { "FreeShipping.Threshold": ["At most 2 decimals in CZK. <b>literal</b>"] } }));
    render(editor()); fireEvent.click(checkbox()); fireEvent.change(field(), { target: { value: "1500" } }); submit();
    await screen.findAllByText("At most 2 decimals in CZK. <b>literal</b>");
    expect(field().value).toBe("1500"); expect(checkbox().checked).toBe(true); expect(document.querySelector(".store-settings-form b")).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "At most 2 decimals in CZK. <b>literal</b>" })); expect(document.activeElement).toBe(field());
    fireEvent.change(field(), { target: { value: "1600" } }); submit(); await screen.findByText("Store settings were saved.");
    expect(api.updateStore).toHaveBeenCalledTimes(2);
  });
  it.each([undefined, { isOn: true, threshold: null }])("leaves unknown free-delivery values unchanged %j", freeShipping => {
    render(editor({ ...store, freeShipping })); expect(screen.getByText(/Saved free-delivery settings are unavailable/)).toBeTruthy();
    expect(document.getElementsByName("freeShipping.isOn")).toHaveLength(0); expect(api.updateStore).not.toHaveBeenCalled();
  });
  it("locks duplicate submissions and aborts departed writes without reload or late success", async () => {
    const held = deferred<AdminStore>(); vi.mocked(api.updateStore).mockReturnValueOnce(held.promise);
    const view = render(editor()); fireEvent.click(checkbox()); act(() => { submit(); submit(); }); expect(api.updateStore).toHaveBeenCalledOnce();
    expect(field().matches(":disabled")).toBe(true);
    const signal = vi.mocked(api.updateStore).mock.calls[0][2]; view.unmount(); expect(signal?.aborted).toBe(true);
    await act(async () => held.resolve({ ...store, freeShipping: { isOn: true, threshold: 1000 } })); expect(reload).not.toHaveBeenCalled();
  });
  it("preserves the draft when current store values change, then replaces it only by explicit review", async () => {
    const view = render(editor()); fireEvent.change(field(), { target: { value: "1500" } });
    view.rerender(editor({ ...store, freeShipping: { isOn: false, threshold: 2000 } }));
    expect(field().value).toBe("1500"); submit(); expect(api.updateStore).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Use current settings and replace this draft" }));
    expect(field().value).toBe("2000"); expect(document.activeElement?.id).toBe("store-details-title");
  });
  it.each([new HttpError("network", null), new HttpError("http", 500), new HttpError("invalid-response", 200), new HttpError("http", 409)])("requires a fresh read and explicit review after a potentially applied save %s", async error => {
    vi.mocked(api.updateStore).mockRejectedValueOnce(error); const view = render(editor()); fireEvent.click(checkbox()); submit();
    await waitFor(() => expect(reload).toHaveBeenCalledOnce());
    const review = screen.getByRole("button", { name: "Use current settings and replace this draft" }); expect(review.matches(":disabled")).toBe(true);
    submit(); expect(api.updateStore).toHaveBeenCalledOnce();
    const refreshed = { ...store, freeShipping: { isOn: true, threshold: 1000 } };
    view.rerender(editor(refreshed, { storeRefreshing: true })); expect(review.matches(":disabled")).toBe(true);
    view.rerender(editor(refreshed, { storeRefreshError: new HttpError("network", null) })); expect(review.matches(":disabled")).toBe(true);
    view.rerender(editor(refreshed)); await userEvent.click(review); expect(checkbox().checked).toBe(true); expect(api.updateStore).toHaveBeenCalledOnce();
    expect(screen.queryByText("Store settings were saved.")).toBeNull();
  });
  it("keeps a late save answer from overwriting an observed same-store change", async () => {
    const held = deferred<AdminStore>(); vi.mocked(api.updateStore).mockReturnValueOnce(held.promise); const view = render(editor()); fireEvent.click(checkbox()); submit();
    view.rerender(editor({ ...store, name: "New name" }));
    await act(async () => held.resolve({ ...store, freeShipping: { isOn: true, threshold: 1000 } }));
    expect(screen.queryByText("Store settings were saved.")).toBeNull(); expect(reload).toHaveBeenCalledOnce(); submit(); expect(api.updateStore).toHaveBeenCalledOnce();
  });
  it("does not silently adopt a changed threshold read back after an acknowledged save", async () => {
    const selected = { ...store, currency: "KWD" };
    vi.mocked(api.updateStore).mockImplementation(async (_id, input) => ({ ...selected, ...input }));
    const view = render(editor(selected)); fireEvent.click(checkbox()); fireEvent.change(field(), { target: { value: "1234.567" } }); submit();
    await screen.findByText("Store settings were saved.");
    view.rerender(editor({ ...selected, freeShipping: { isOn: true, threshold: 1234.57 } }));
    expect(field().value).toBe("1234.567"); expect(field().matches(":disabled")).toBe(true);
    expect(screen.queryByText("Store settings were saved.")).toBeNull(); submit(); expect(api.updateStore).toHaveBeenCalledOnce();
  });
  it.each([401, 403, 404])("prevents another save after a %s access refusal", async status => {
    vi.mocked(api.updateStore).mockRejectedValueOnce(new HttpError("http", status)); render(editor()); submit();
    await screen.findByRole("alert"); expect(screen.getByRole("button", { name: "Save settings" }).matches(":disabled")).toBe(true); submit(); expect(api.updateStore).toHaveBeenCalledOnce();
  });
});

function route(role: string, selected = store) {
  return <MemoryRouter initialEntries={["/settings"]}><Routes><Route element={<SessionContext value={{ user: { id: "user", email: "user@example.test", tenantId: "tenant", role }, logout: vi.fn(), logoutPending: false, logoutError: null }}><Outlet context={{ store: selected, storeRefreshing: false, storeRefreshError: null, reloadStores: reload }} /></SessionContext>}>
    <Route path="/settings" element={<StoreSettingsPage />} />
  </Route></Routes></MemoryRouter>;
}
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("guards actual settings-route submissions for %s", async role => {
  render(route(role)); await screen.findByRole("heading", { name: "Store details" });
  if (["Owner", "Admin"].includes(role)) { fireEvent.click(checkbox()); submit(); await screen.findByText("Store settings were saved."); expect(api.updateStore).toHaveBeenCalledOnce(); }
  else { expect(field().matches(":disabled")).toBe(true); submit(); expect(api.updateStore).not.toHaveBeenCalled(); }
});
it("discards old route drafts and ignores the aborted answer when the role changes", async () => {
  const held = deferred<AdminStore>(); vi.mocked(api.updateStore).mockReturnValueOnce(held.promise); const view = render(route("Owner"));
  fireEvent.click(checkbox()); submit(); const signal = vi.mocked(api.updateStore).mock.calls[0][2];
  view.rerender(route("Support")); expect(signal?.aborted).toBe(true); expect(checkbox().checked).toBe(false);
  await act(async () => held.resolve({ ...store, freeShipping: { isOn: true, threshold: 1000 } })); expect(reload).not.toHaveBeenCalled();
});
