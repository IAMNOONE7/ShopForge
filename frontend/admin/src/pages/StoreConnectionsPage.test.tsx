// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import { api, type AdminStore } from "../api";
import { HttpError } from "../api/http";
import type { ProviderConnection, ProviderChoices } from "../api/providerConnections";
import { AdminNavigation } from "../components/AdminNavigation";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { StoreConnectionsPage } from "./StoreConnectionsPage";
import { StoreOverviewPage } from "./StoreOverviewPage";

const row: ProviderConnection = { provider: "comgate", merchantId: "merchant-a", environment: "Test", isActive: false, hasSecret: false, publishableKey: "saved-public-key", changedAt: "2026-10-20T12:00:00Z" };
const store: AdminStore = { id: "a", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "published", logoUrl: null, primaryHostName: null, returnWindowDays: 14, company: null, theme: { primaryColor: "#123456", secondaryColor: "#fff", borderRadius: 4 } };
let rows: ProviderConnection[];
const canary = "transient-credential-canary";
type Options = { role?: string; tenant?: string; storeId?: string; stale?: boolean };
function Shell({ role = "Owner", tenant = "tenant-a", storeId = "a", stale = false }: Options) {
  return <SessionContext value={{ user: { id: "user", email: "owner@example.test", role, tenantId: tenant }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <Outlet context={{ store: { ...store, id: storeId }, storeRefreshing: stale, storeRefreshError: null, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function app(options: Options = {}, path = "/stores/a/provider-connections") { return <MemoryRouter initialEntries={[path]}><Routes><Route element={<Shell {...options} />}>
  <Route path="/stores/:id/provider-connections" element={<StoreConnectionsPage />} /><Route path="/stores/:id" element={<StoreOverviewPage />} />
  </Route></Routes></MemoryRouter>; }
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (error: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
async function ready() { await screen.findByRole("button", { name: "Edit account settings for comgate" }); await waitFor(() => expect(screen.getByRole("button", { name: "Edit account settings for comgate" }).matches(":disabled")).toBe(false)); }
const form = () => document.querySelector<HTMLFormElement>(".connection-editor")!;
async function open(user: ReturnType<typeof userEvent.setup>, kind = "metadata", provider = "comgate") {
  await user.click(screen.getByRole("button", { name: kind === "metadata" ? `Edit account settings for ${provider}` : kind === "secret" ? `Set credential for ${provider}` : `Disconnect ${provider}` }));
}
beforeAll(async () => { HTMLElement.prototype.scrollIntoView = vi.fn(); await initializeI18n(); });
beforeEach(async () => {
  await i18n.changeLanguage("en"); rows = structuredClone([row]);
  vi.spyOn(api, "providerConnections").mockImplementation(async id => id === "a" ? structuredClone(rows) : []);
  vi.spyOn(api, "providerChoices").mockResolvedValue({ payment: ["manual", "comgate"], shipping: ["manual", "packeta"] });
  vi.spyOn(api, "storeReadiness").mockResolvedValue({ isReady: false, checks: [
    { check: "orders.payment-connected", isReady: false, weight: "required", problem: "Gateway is switched on but not connected." },
    { check: "orders.shipping-connected", isReady: true, weight: "required", problem: null },
    { check: "store.logo", isReady: false, weight: "required", problem: "Missing logo" },
  ] });
  vi.spyOn(api, "saveProviderConnection").mockImplementation(async (_store, provider, input) => {
    const existing = rows.find(item => item.provider === provider); const saved = { ...row, ...existing, ...input, provider, isActive: existing ? input.isActive : true, hasSecret: existing?.hasSecret ?? false, changedAt: "2026-10-20T12:01:00Z" };
    rows = [...rows.filter(item => item.provider !== provider), saved]; return saved;
  });
  vi.spyOn(api, "setProviderSecret").mockImplementation(async (_store, provider) => { rows = rows.map(item => item.provider === provider ? { ...item, hasSecret: true, changedAt: "2026-10-20T12:01:00Z" } : item); });
  vi.spyOn(api, "disconnectProvider").mockImplementation(async (_store, provider) => { rows = rows.filter(item => item.provider !== provider); });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("keeps reference storage, active state, discovery and method-scoped readiness distinct", async () => {
  rows[0].hasSecret = true; const user = userEvent.setup(); render(app()); await ready();
  expect(screen.getByText("Reference stored — not a credential test")).toBeTruthy(); expect(screen.getByText("Inactive")).toBeTruthy();
  expect(screen.getByText(/These server checks cover providers used by active checkout methods/)).toBeTruthy(); expect(screen.queryByText("Missing logo")).toBeNull();
  await user.click(screen.getByText("Account settings and credentials")); expect(screen.getByText(/within two seconds/)).toBeTruthy(); expect(screen.getByText(/revoke it at the provider first/)).toBeTruthy();
});
it.each(["Owner", "Admin"])("saves metadata separately for %s and preserves false flags, public key and credential reference", async role => {
  rows[0].hasSecret = true; const user = userEvent.setup(); render(app({ role })); await ready(); await open(user);
  expect(document.activeElement).toBe(screen.getByRole("heading", { name: "Account settings — comgate" }));
  const account = screen.getByRole("textbox", { name: "Comgate merchant ID" }); await user.clear(account); await user.type(account, " merchant-b ");
  await user.click(screen.getByRole("button", { name: "Save account settings" })); await screen.findByText(/Account settings for comgate were acknowledged/);
  expect(api.saveProviderConnection).toHaveBeenCalledExactlyOnceWith("a", "comgate", { merchantId: "merchant-b", environment: "Test", isActive: false, publishableKey: "saved-public-key" });
  expect(api.setProviderSecret).not.toHaveBeenCalled(); expect(rows[0].hasSecret).toBe(true); expect(api.providerConnections).toHaveBeenCalledTimes(2); expect(api.storeReadiness).toHaveBeenCalledTimes(2);
});
it("requires an explicit environment, keeps Packeta reference blank and explains the unresolved account/sandbox contract", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await open(user, "metadata", "packeta");
  expect(screen.getByRole("textbox", { name: "Account reference" })).toHaveProperty("value", ""); expect(screen.getByRole("combobox", { name: "Environment" })).toHaveProperty("value", "");
  expect(screen.getByText(/Packeta-specific meaning is not defined yet/)).toBeTruthy(); expect(screen.getByText(/does not currently select a separate Packeta sandbox/)).toBeTruthy();
  await user.click(screen.getByRole("button", { name: "Save account settings" })); expect(document.activeElement).toBe(screen.getByRole("textbox", { name: "Account reference" }));
  await user.type(screen.getByRole("textbox", { name: "Account reference" }), "packeta-confirmed-reference"); await user.click(screen.getByRole("button", { name: "Save account settings" })); expect(document.activeElement).toBe(screen.getByRole("combobox", { name: "Environment" }));
  await user.selectOptions(screen.getByRole("combobox", { name: "Environment" }), "Test"); await user.type(screen.getByRole("textbox", { name: "Packeta map key" }), "map-key");
  await user.click(screen.getByRole("button", { name: "Save account settings" })); await screen.findByText(/Account settings for packeta were acknowledged/);
  expect(api.saveProviderConnection).toHaveBeenCalledWith("a", "packeta", { merchantId: "packeta-confirmed-reference", environment: "Test", isActive: true, publishableKey: "map-key" }); expect(api.setProviderSecret).not.toHaveBeenCalled();
});
it("clears public keys explicitly, can select Live and never replaces a credential during metadata edits", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await open(user); await user.clear(screen.getByRole("textbox", { name: "Publishable key" })); await user.selectOptions(screen.getByRole("combobox", { name: "Environment" }), "Live");
  expect(screen.getByText(/Live connections use the production account/)).toBeTruthy(); await user.click(screen.getByRole("button", { name: "Save account settings" })); await waitFor(() => expect(api.saveProviderConnection).toHaveBeenCalledOnce());
  expect(vi.mocked(api.saveProviderConnection).mock.calls[0][2]).toEqual({ merchantId: "merchant-a", environment: "Live", isActive: false, publishableKey: null }); expect(api.setProviderSecret).not.toHaveBeenCalled();
});
it.each([["merchantId", "Comgate merchant ID", "x".repeat(101)], ["publishableKey", "Publishable key", "x".repeat(201)]])("retains invalid %s values and focuses the field without sending", async (_field, label, value) => {
  const user = userEvent.setup(); render(app()); await ready(); await open(user); const field = screen.getByRole("textbox", { name: label }); await user.clear(field); fireEvent.change(field, { target: { value } });
  await user.click(screen.getByRole("button", { name: "Save account settings" })); expect(document.activeElement).toBe(field); expect(field).toHaveProperty("value", value); expect(api.saveProviderConnection).not.toHaveBeenCalled();
});
it("shows an unknown saved environment and requires a known environment before resaving", async () => {
  rows[0].environment = "Future"; const user = userEvent.setup(); render(app()); await ready(); expect(screen.getByText("Unknown recorded environment: Future")).toBeTruthy(); await open(user);
  await user.click(screen.getByRole("button", { name: "Save account settings" })); expect(document.activeElement).toBe(screen.getByRole("combobox", { name: "Environment" })); expect(api.saveProviderConnection).not.toHaveBeenCalled();
});
it("sends only a separately entered credential, immediately clears it and refreshes reference/readiness facts", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await open(user, "secret"); const field = screen.getByLabelText("Provider credential");
  expect(field.getAttribute("type")).toBe("password"); expect(field.getAttribute("autocomplete")).toBe("new-password"); await user.type(field, canary);
  await user.click(screen.getByRole("button", { name: "Save credential" })); await screen.findByText(/credential save for comgate was acknowledged/);
  expect(api.setProviderSecret).toHaveBeenCalledExactlyOnceWith("a", "comgate", canary); expect(api.saveProviderConnection).not.toHaveBeenCalled(); expect(document.body.innerHTML).not.toContain(canary);
  expect(Object.values(localStorage).join(" ")).not.toContain(canary); expect(sessionStorage.length).toBe(0); expect(window.location.href).not.toContain(canary);
});
it("requires a nonblank credential and discards it on Escape/cancel", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await open(user, "secret"); await user.click(screen.getByRole("button", { name: "Save credential" })); expect(document.activeElement).toBe(screen.getByLabelText("Provider credential"));
  await user.type(screen.getByLabelText("Provider credential"), canary); await user.keyboard("{Escape}"); expect(screen.queryByLabelText("Provider credential")).toBeNull(); expect(api.setProviderSecret).not.toHaveBeenCalled(); expect(document.body.innerHTML).not.toContain(canary);
  await open(user, "secret"); expect(screen.getByLabelText("Provider credential")).toHaveProperty("value", "");
});
it.each([400, 404, 409, 503])("never echoes or retains failed credentials for status %s, including hostile server diagnostics", async status => {
  vi.mocked(api.setProviderSecret).mockRejectedValueOnce(new HttpError("http", status, { detail: canary, traceId: canary, errors: { secret: [canary] } }, canary));
  const user = userEvent.setup(); render(app()); await ready(); await open(user, "secret"); await user.type(screen.getByLabelText("Provider credential"), canary); await user.click(screen.getByRole("button", { name: "Save credential" }));
  await screen.findByText(status === 503 ? /The outcome is not confirmed/ : /The request was refused/); expect(document.body.innerHTML).not.toContain(canary); expect(screen.queryByLabelText("Provider credential")).toBeNull(); expect(api.setProviderSecret).toHaveBeenCalledOnce();
  if (status === 409) expect(screen.getByText(/no writable credential store/)).toBeTruthy();
  if (status === 503) expect(screen.getByText(/Credential storage is temporarily unavailable/)).toBeTruthy();
});
it("does not infer secret rotation success from a hasSecret=true read after a lost reply", async () => {
  rows[0].hasSecret = true; vi.mocked(api.setProviderSecret).mockRejectedValueOnce(new HttpError("network", null)); const user = userEvent.setup(); render(app()); await ready(); await open(user, "secret");
  await user.type(screen.getByLabelText("Provider credential"), canary); await user.click(screen.getByRole("button", { name: "Save credential" })); await screen.findByText(/The outcome is not confirmed/);
  expect(screen.queryByText(/credential save for comgate was acknowledged/)).toBeNull(); expect(screen.getByText("Reference stored — not a credential test")).toBeTruthy();
  await waitFor(() => expect(screen.getByRole("button", { name: "Finish review" }).matches(":disabled")).toBe(false)); await user.click(screen.getByRole("button", { name: "Finish review" })); expect(api.setProviderSecret).toHaveBeenCalledOnce();
});
it("requires named reviewed disconnect intent and leaves methods untouched", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await open(user, "disconnect"); expect(screen.getByText(/Disconnect comgate, account merchant-a, environment Test/)).toBeTruthy();
  fireEvent.submit(form()); expect(api.disconnectProvider).not.toHaveBeenCalled(); await user.click(screen.getByRole("checkbox", { name: /I have reviewed this store/ })); await user.click(screen.getByRole("button", { name: "Confirm disconnect" }));
  await screen.findByText(/disconnect for comgate was acknowledged/); expect(api.disconnectProvider).toHaveBeenCalledExactlyOnceWith("a", "comgate"); expect(screen.queryByText("merchant-a")).toBeNull();
});
it("keeps an unoffered legacy connection visible and supports explicit recovery without advertising a new setup", async () => {
  rows.push({ ...row, provider: "retired-gateway", hasSecret: true }); const user = userEvent.setup(); render(app()); await ready();
  expect(screen.getByRole("heading", { name: "retired-gateway" })).toBeTruthy(); expect(screen.getByText(/not currently offered to stores/)).toBeTruthy();
  await open(user, "metadata", "retired-gateway"); expect(screen.getByRole("textbox", { name: "Account reference" })).toHaveProperty("value", "merchant-a"); await user.click(screen.getByRole("button", { name: "Cancel" }));
  vi.mocked(api.providerChoices).mockResolvedValue({ payment: ["manual", "stripe", "other"], shipping: [] }); await user.click(screen.getByRole("button", { name: "Refresh connections" }));
  await screen.findByRole("heading", { name: "stripe" }); expect(screen.queryByRole("button", { name: "Edit account settings for stripe" })).toBeNull(); expect(screen.queryByRole("heading", { name: "manual" })).toBeNull();
});
it("retains a definitively refused metadata draft, sanitizes server text and maps a field link", async () => {
  vi.mocked(api.saveProviderConnection).mockRejectedValueOnce(new HttpError("http", 400, { errors: { MerchantId: [canary] }, detail: canary }, canary));
  const user = userEvent.setup(); render(app()); await ready(); await open(user); const field = screen.getByRole("textbox", { name: "Comgate merchant ID" }); await user.clear(field); await user.type(field, "revised");
  await user.click(screen.getByRole("button", { name: "Save account settings" })); await screen.findByText(/The request was refused/); await waitFor(() => expect(screen.getByRole("button", { name: "Save account settings" }).matches(":disabled")).toBe(false));
  expect(field).toHaveProperty("value", "revised"); expect(document.body.innerHTML).not.toContain(canary); await user.click(screen.getByRole("button", { name: "Review this account setting." })); expect(document.activeElement).toBe(field); expect(api.saveProviderConnection).toHaveBeenCalledOnce();
});
it.each(["metadata", "secret", "disconnect"])("blocks writes until a failed post-%s read recovers; retries never replay", async kind => {
  const user = userEvent.setup(); render(app()); await ready(); await open(user, kind); vi.mocked(api.providerConnections).mockRejectedValueOnce(new HttpError("http", 503));
  if (kind === "secret") await user.type(screen.getByLabelText("Provider credential"), canary);
  if (kind === "disconnect") await user.click(screen.getByRole("checkbox"));
  await user.click(screen.getByRole("button", { name: kind === "metadata" ? "Save account settings" : kind === "secret" ? "Save credential" : "Confirm disconnect" }));
  await screen.findByRole("alert"); expect(screen.getAllByRole("button", { name: /Edit account settings for/ }).every(button => button.matches(":disabled"))).toBe(true);
  await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getAllByRole("button", { name: /Edit account settings for/ }).every(button => !button.matches(":disabled"))).toBe(true));
  expect(vi.mocked(api.saveProviderConnection).mock.calls.length + vi.mocked(api.setProviderSecret).mock.calls.length + vi.mocked(api.disconnectProvider).mock.calls.length).toBe(1);
});
it("retains failed initial reads as errors and recovers an empty collection without creating anything", async () => {
  rows = []; vi.mocked(api.providerConnections).mockRejectedValueOnce(new HttpError("invalid-response", null)); const user = userEvent.setup(); render(app()); await screen.findByRole("alert"); expect(screen.queryByText(/No provider accounts are saved/)).toBeNull();
  await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByText(/No provider accounts are saved/); expect(api.saveProviderConnection).not.toHaveBeenCalled();
});
it("does not confuse failed discovery with an unoffered saved provider and keeps readiness failure independent", async () => {
  vi.mocked(api.providerChoices).mockRejectedValueOnce(new HttpError("http", 503)); vi.mocked(api.storeReadiness).mockRejectedValueOnce(new HttpError("http", 503)); const user = userEvent.setup(); render(app()); await ready();
  expect(screen.getByText(/Provider availability is not current/)).toBeTruthy(); expect(screen.queryByText(/not currently offered to stores/)).toBeNull(); expect(screen.queryByRole("button", { name: "Edit account settings for packeta" })).toBeNull();
  await open(user); expect(screen.getByRole("button", { name: "Save account settings" }).matches(":disabled")).toBe(false); expect(screen.getByText(/Provider readiness is not current/)).toBeTruthy();
});
it("invalidates a changed or missing account snapshot without silently rebasing a draft", async () => {
  const user = userEvent.setup(); render(app()); await ready(); await open(user); rows[0].merchantId = "changed-by-colleague"; await user.click(screen.getByRole("button", { name: "Refresh connections" }));
  await screen.findByText(/The saved account changed or disappeared/); expect(screen.getByRole("button", { name: "Save account settings" }).matches(":disabled")).toBe(true); fireEvent.submit(form()); expect(api.saveProviderConnection).not.toHaveBeenCalled(); await user.click(screen.getByRole("button", { name: "Cancel" }));
});
it("locks duplicate credential submissions and clears the field before the held request returns", async () => {
  const held = deferred<void>(); vi.mocked(api.setProviderSecret).mockReturnValueOnce(held.promise); const user = userEvent.setup(); render(app()); await ready(); await open(user, "secret"); await user.type(screen.getByLabelText("Provider credential"), canary);
  fireEvent.submit(form()); fireEvent.submit(form()); expect(api.setProviderSecret).toHaveBeenCalledOnce(); expect(screen.getByLabelText("Provider credential")).toHaveProperty("value", ""); expect(screen.getByRole("button", { name: "Cancel" }).matches(":disabled")).toBe(true);
  await act(async () => held.resolve()); await screen.findByText(/credential save for comgate was acknowledged/);
});
it.each(["CatalogManager", "Warehouse", "Support"])("allows nonsecret reads but no mutation controls for %s", async role => {
  render(app({ role })); await screen.findByText("merchant-a"); expect(screen.queryByRole("button", { name: /Edit account settings for|Set credential for|Disconnect comgate/ })).toBeNull(); expect(screen.queryByLabelText("Provider credential")).toBeNull(); expect(api.setProviderSecret).not.toHaveBeenCalled();
});
it.each([{ tenant: "tenant-b" }, { storeId: "b" }, { role: "Admin" }])("discards transient credentials when scope changes: %j", async options => {
  const user = userEvent.setup(), view = render(app()); await ready(); await open(user, "secret"); await user.type(screen.getByLabelText("Provider credential"), canary); view.rerender(app(options));
  await screen.findByRole("heading", { name: "Provider connections" }); expect(screen.queryByLabelText("Provider credential")).toBeNull(); expect(document.body.innerHTML).not.toContain(canary); expect(api.setProviderSecret).not.toHaveBeenCalled();
});
it("ignores old-scope connection and discovery replies", async () => {
  const old = deferred<ProviderConnection[]>(), discovery = deferred<ProviderChoices>(); vi.mocked(api.providerConnections).mockReturnValueOnce(old.promise); vi.mocked(api.providerChoices).mockReturnValueOnce(discovery.promise);
  const view = render(app()); view.rerender(app({ storeId: "b" })); await screen.findByText(/No provider accounts are saved/); await act(async () => { old.resolve(rows); discovery.resolve({ payment: ["old-provider"], shipping: [] }); });
  expect(screen.queryByText("merchant-a")).toBeNull(); expect(screen.queryByRole("heading", { name: "old-provider" })).toBeNull();
});
it("ignores a departed scope's late credential outcome and does not refresh the next store for it", async () => {
  const held = deferred<void>(); vi.mocked(api.setProviderSecret).mockReturnValueOnce(held.promise); const user = userEvent.setup(), view = render(app()); await ready(); await open(user, "secret"); await user.type(screen.getByLabelText("Provider credential"), canary); fireEvent.submit(form());
  view.rerender(app({ storeId: "b" })); await screen.findByText(/No provider accounts are saved/); const reads = vi.mocked(api.providerConnections).mock.calls.length; await act(async () => held.resolve());
  expect(api.providerConnections).toHaveBeenCalledTimes(reads); expect(screen.queryByText(/credential save for comgate was acknowledged/)).toBeNull();
});
it("guards account mutations while store metadata is stale", async () => {
  render(app({ stale: true })); await screen.findByText("merchant-a"); expect(screen.getAllByRole("button", { name: /Edit account settings for/ }).every(button => button.matches(":disabled"))).toBe(true); expect(api.saveProviderConnection).not.toHaveBeenCalled();
});
it.each(["en", "cs"])("renders accessible localized controls and no inline styles/scripts in %s", async language => {
  await i18n.changeLanguage(language); render(app()); await screen.findByText("merchant-a"); expect(document.querySelector("[style],style,script")).toBeNull(); expect(screen.getByRole("heading", { name: language === "cs" ? "Připojení poskytovatelů" : "Provider connections" })).toBeTruthy();
});
it("offers the new page from the store overview and tenant navigation", async () => {
  const user = userEvent.setup(); render(app({}, "/stores/a")); await user.click(screen.getByRole("link", { name: "Provider connections" })); await ready(); cleanup();
  render(<MemoryRouter><AdminNavigation stores={{ status: "ready", data: [store], refreshing: false, refreshError: null }} selectedStore={store} reloadStores={vi.fn()} role="Support" /></MemoryRouter>);
  expect(screen.getByRole("link", { name: "Provider connections" }).getAttribute("href")).toBe("/stores/a/provider-connections");
});
