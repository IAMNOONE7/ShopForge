// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore } from "../api";
import type { StoreDomain } from "../api/domains";
import { HttpError } from "../api/http";
import { initializeI18n, i18n } from "../i18n";
import { SessionContext } from "../session";
import { StoreDomainsPage } from "./StoreDomainsPage";
import { StoreSettingsPage } from "./StorePage";

const store: AdminStore = { id: "store-a", name: "Shop", currency: "CZK", culture: "cs-CZ", status: "published", logoUrl: null, company: null, primaryHostName: "shop.localhost", returnWindowDays: 14,
  theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 } };
const primary: StoreDomain = { id: "platform", hostName: "shop.localhost", isPrimary: true, isVerified: true, verifiedAt: "2026-01-01T00:00:00Z", challengeName: null, challengeValue: null, claimExpiresAt: null };
const pending: StoreDomain = { id: "custom", hostName: "shop.example.cz", isPrimary: false, isVerified: false, verifiedAt: null, challengeName: "_shopforge-challenge.shop.example.cz", challengeValue: "exact-proof-123", claimExpiresAt: "2030-01-01T00:00:00Z" };
const verified: StoreDomain = { ...pending, isVerified: true, verifiedAt: "2026-10-15T00:00:00Z", challengeName: null, challengeValue: null, claimExpiresAt: null };
let rows: StoreDomain[];
const reload = vi.fn();
function deferred<T>() {
  let resolve!: (value: T) => void; let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
function session(role: string) { return { user: { id: "owner", role, email: "owner@example.test", tenantId: "tenant" }, logout: vi.fn(), logoutPending: false, logoutError: null }; }
function Shell({ refreshing = false, refreshError = null, role = "Owner" }: { refreshing?: boolean; refreshError?: unknown; role?: string }) {
  const [selected, setSelected] = useState(store);
  const navigate = useNavigate();
  return <SessionContext value={session(role)}>
    <button onClick={() => { setSelected({ ...store, id: "store-b" }); navigate("/stores/store-b/domains"); }}>Other store</button>
    <Outlet context={{ store: selected, reloadStores: reload, storeRefreshing: refreshing, storeRefreshError: refreshError }} />
  </SessionContext>;
}
function route(options: { refreshing?: boolean; refreshError?: unknown; role?: string } = {}) {
  return render(<MemoryRouter initialEntries={["/stores/store-a/domains"]}><Routes><Route element={<Shell {...options} />}>
    <Route path="/stores/:storeId/domains" element={<StoreDomainsPage />} />
    <Route path="/stores/:storeId/settings" element={<StoreSettingsPage />} />
  </Route></Routes></MemoryRouter>);
}
async function ready() { await screen.findByRole("heading", { level: 3, name: primary.hostName }); }
function row(host = pending.hostName) { return within(screen.getByRole("heading", { level: 3, name: host }).closest("li")!); }
beforeAll(initializeI18n);
beforeEach(async () => {
  vi.restoreAllMocks(); reload.mockReset(); rows = [primary, pending];
  await i18n.changeLanguage("en");
  vi.spyOn(api, "storeDomains").mockImplementation(async () => [...rows]);
  vi.spyOn(api, "addStoreDomain").mockResolvedValue(pending);
  vi.spyOn(api, "verifyStoreDomain").mockImplementation(async () => { rows = [primary, verified]; return verified; });
  vi.spyOn(api, "makeStoreDomainPrimary").mockImplementation(async () => {
    rows = [{ ...primary, isPrimary: false }, { ...verified, isPrimary: true }]; return rows[1];
  });
  vi.spyOn(api, "removeStoreDomain").mockImplementation(async () => { rows = [primary]; });
  vi.spyOn(api, "storeReadiness").mockResolvedValue({ isReady: false, checks: [{ check: "store.primary-domain", isReady: false, problem: "No proof", weight: "required" }] });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

describe("domain onboarding", () => {
  it("shows exact DNS proof, ownership-only wording and primary protection", async () => {
    route(); await ready();
    expect(screen.getByRole("textbox", { name: "TXT record name" })).toHaveProperty("value", pending.challengeName);
    expect(screen.getByRole("textbox", { name: "TXT record value" })).toHaveProperty("value", pending.challengeValue);
    expect(screen.getByText(/does not confirm HTTPS/)).toBeTruthy();
    expect(row(primary.hostName).queryByRole("button", { name: /Remove/ })).toBeNull();
    expect(row().getByRole("button", { name: /Make .* primary/ })).toHaveProperty("disabled", true);
    expect(screen.getByText(/Unverified claim ends/)).toBeTruthy();
  });

  it("normalizes a new claim, rejects invalid/empty input and preserves failed drafts", async () => {
    const user = userEvent.setup(); route(); await ready();
    const input = screen.getByRole("textbox", { name: "Domain name" });
    await user.click(screen.getByRole("button", { name: "Add domain" }));
    expect(api.addStoreDomain).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(input);
    await user.type(input, "https://bad.cz");
    await user.click(screen.getByRole("button", { name: "Add domain" }));
    expect(api.addStoreDomain).not.toHaveBeenCalled();
    await user.clear(input); await user.type(input, "  SHOP.New.CZ. ");
    vi.mocked(api.addStoreDomain).mockRejectedValueOnce(new HttpError("http", 409, { title: "That domain is already taken" }));
    await user.click(screen.getByRole("button", { name: "Add domain" }));
    expect(await screen.findByText(/domain is already claimed/)).toBeTruthy();
    expect(input).toHaveProperty("value", "  SHOP.New.CZ. ");
    await waitFor(() => expect(input).toHaveProperty("disabled", false));
    await user.click(screen.getByRole("button", { name: "Add domain" }));
    expect(api.addStoreDomain).toHaveBeenLastCalledWith("store-a", "shop.new.cz");
    await waitFor(() => expect(input).toHaveProperty("value", ""));
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it("keeps verification refusal visible without marking the domain verified, then reads after success", async () => {
    const user = userEvent.setup();
    vi.mocked(api.verifyStoreDomain).mockRejectedValueOnce(new HttpError("http", 409, { title: "The record is not there yet" }));
    route(); await ready();
    await user.click(row().getByRole("button", { name: /Check ownership/ }));
    expect(await screen.findByText(/TXT proof is not visible yet/)).toBeTruthy();
    expect(row().getByText("Awaiting ownership proof")).toBeTruthy();
    await waitFor(() => expect(row().getByRole("button", { name: /Check ownership/ })).toHaveProperty("disabled", false));
    await user.click(row().getByRole("button", { name: /Check ownership/ }));
    await waitFor(() => expect(row().getByText("Ownership verified")).toBeTruthy());
    expect(row().queryByRole("textbox", { name: "TXT record value" })).toBeNull();
    expect(api.verifyStoreDomain).toHaveBeenCalledTimes(2);
  });

  it("confirms a primary change, restores cancel focus, serializes writes and refreshes store metadata", async () => {
    const user = userEvent.setup(); rows = [primary, verified]; route(); await ready();
    const open = row().getByRole("button", { name: /Make .* primary/ });
    await user.click(open);
    expect(api.makeStoreDomainPrimary).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(row().getByRole("button", { name: "Confirm change" }));
    await user.click(row().getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(document.activeElement).toBe(open));
    await user.click(open);
    const write = deferred<StoreDomain>(); vi.mocked(api.makeStoreDomainPrimary).mockReturnValueOnce(write.promise);
    await user.click(row().getByRole("button", { name: "Confirm change" }));
    await user.click(row().getByRole("button", { name: "Saving address…" }));
    expect(api.makeStoreDomainPrimary).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Add domain" })).toHaveProperty("disabled", true);
    rows = [{ ...primary, isPrimary: false }, { ...verified, isPrimary: true }];
    await act(async () => write.resolve(rows[1]));
    await waitFor(() => expect(row().getByText("Primary address")).toBeTruthy());
    expect(reload).toHaveBeenCalledTimes(1);
    expect(document.activeElement).toBe(screen.getByRole("heading", { level: 1, name: "Domains and addresses" }));
  });

  it("names a removal, retains rows on a protected-primary refusal, and never repeats it on a read retry", async () => {
    const user = userEvent.setup();
    vi.mocked(api.removeStoreDomain).mockRejectedValueOnce(new HttpError("http", 409, { title: "The primary domain cannot be removed" }));
    route(); await ready();
    await user.click(row().getByRole("button", { name: /Remove/ }));
    expect(screen.getByRole("group", { name: "Remove shop.example.cz?" })).toBeTruthy();
    await user.click(row().getByRole("button", { name: "Confirm change" }));
    expect(await screen.findByText(/primary address cannot be removed/)).toBeTruthy();
    await waitFor(() => expect(row().getByRole("button", { name: /Remove/ })).toHaveProperty("disabled", false));
    await user.click(screen.getByRole("button", { name: "Refresh addresses" }));
    expect(api.removeStoreDomain).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(row().getByRole("button", { name: /Remove/ })).toHaveProperty("disabled", false));
    await user.click(row().getByRole("button", { name: /Remove/ }));
    await user.click(row().getByRole("button", { name: "Confirm change" }));
    await waitFor(() => expect(screen.queryByRole("heading", { name: pending.hostName })).toBeNull());
  });

  it("blocks further changes after a confirmed write with a failed read and retries only that read", async () => {
    const user = userEvent.setup(); route(); await ready();
    vi.mocked(api.storeDomains).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(row().getByRole("button", { name: /Check ownership/ }));
    await screen.findByRole("button", { name: "Try again" });
    expect(row().getByText("Awaiting ownership proof")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Add domain" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(row().getByText("Ownership verified")).toBeTruthy());
    expect(api.verifyStoreDomain).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Add domain" })).toHaveProperty("disabled", false);
  });

  it("keeps a failed/refreshing store metadata snapshot from enabling domain writes", async () => {
    const user = userEvent.setup();
    route({ refreshError: new HttpError("network", null) }); await ready();
    expect(screen.getByRole("button", { name: "Add domain" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Refresh store details" }));
    expect(reload).toHaveBeenCalledTimes(1);
    expect(api.addStoreDomain).not.toHaveBeenCalled();
  });

  it("retains an expired claim and permits checking it until the server removes it", async () => {
    rows = [primary, { ...pending, claimExpiresAt: "2020-01-01T00:00:00Z" }]; route(); await ready();
    expect(screen.getByText(/Claim period ended/)).toBeTruthy();
    expect(screen.getByText(/can remain listed until server cleanup/)).toBeTruthy();
    expect(row().getByRole("button", { name: /Check ownership/ })).toHaveProperty("disabled", false);
  });

  it("copies exact TXT values and offers manual selection when clipboard access fails", async () => {
    const user = userEvent.setup(); route(); await ready();
    const copy = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    await user.click(screen.getByRole("button", { name: "Copy TXT record value" }));
    expect(copy).toHaveBeenCalledWith(pending.challengeValue);
    await screen.findByText("Copied");
    copy.mockRejectedValueOnce(new Error("permission"));
    await user.click(screen.getByRole("button", { name: "Copy TXT record name" }));
    await screen.findByText(/copy its complete value manually/);
    await user.click(screen.getByRole("textbox", { name: "TXT record name" }));
    const input = screen.getByRole("textbox", { name: "TXT record name" }) as HTMLInputElement;
    expect(input.selectionEnd).toBe(pending.challengeName!.length);
  });

  it("discards drafts, confirmations and late results after switching stores", async () => {
    const user = userEvent.setup(); const write = deferred<StoreDomain>();
    vi.mocked(api.verifyStoreDomain).mockReturnValueOnce(write.promise);
    route(); await ready();
    await user.type(screen.getByRole("textbox", { name: "Domain name" }), "draft.cz");
    await user.click(row().getByRole("button", { name: /Check ownership/ }));
    await user.click(screen.getByRole("button", { name: "Other store" }));
    await waitFor(() => expect(api.storeDomains).toHaveBeenCalledWith("store-b", expect.any(AbortSignal)));
    await act(async () => write.resolve(verified));
    expect(screen.getByRole("textbox", { name: "Domain name" })).toHaveProperty("value", "");
    expect(reload).not.toHaveBeenCalled();
    expect(screen.queryByText(/Ownership verified. Live HTTPS/)).toBeNull();
  });

  it("refreshes readiness on returning to settings and exposes its domain fix link", async () => {
    const user = userEvent.setup(); route(); await ready();
    await user.click(screen.getByRole("link", { name: "Back to store settings" }));
    await screen.findByText("Verified primary address");
    expect(api.storeReadiness).toHaveBeenCalledWith("store-a", expect.any(AbortSignal));
    expect(screen.getByRole("link", { name: "Open settings" }).getAttribute("href")).toBe("/stores/store-a/domains");
  });

  it("localizes domain instructions, refusals and controls in Czech", async () => {
    await i18n.changeLanguage("cs"); route(); await ready();
    expect(screen.getByRole("heading", { name: "Domény a adresy" })).toBeTruthy();
    expect(screen.getByRole("textbox", { name: "Název TXT záznamu" })).toHaveProperty("value", pending.challengeName);
    expect(screen.getByText(/Ověření vlastnictví nepotvrzuje HTTPS/)).toBeTruthy();
  });

  it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "unknown"])("gates the actual direct route and list reads for %s", async (role) => {
    vi.spyOn(api, "me").mockResolvedValue(session(role).user);
    vi.spyOn(api, "stores").mockResolvedValue([store]);
    window.history.replaceState(null, "", "/stores/store-a/domains"); render(<App />);
    const allowed = ["Owner", "Admin"].includes(role);
    if (allowed) { await ready(); expect(screen.getAllByRole("link", { name: "Domains" }).length).toBeGreaterThan(0); }
    else { await screen.findByRole("heading", { name: "Permission required" }); expect(api.storeDomains).not.toHaveBeenCalled(); }
  });
});
