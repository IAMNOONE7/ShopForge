// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore } from "../api";
import type { StoreDomain } from "../api/domains";
import { HttpError } from "../api/http";
import { setupProgressApi, type SetupDetail } from "../api/setupProgress";
import { initializeI18n, i18n } from "../i18n";
import { SessionContext } from "../session";
import { StoreDomainsPage } from "./StoreDomainsPage";
import { StoreSettingsPage } from "./StorePage";
import { StoreSetupDetailPage, StoreSetupPage } from "./StoreSetupPage";

const store: AdminStore = { id: "store-a", name: "Shop", currency: "CZK", culture: "cs-CZ", status: "published", logoUrl: null, company: null, primaryHostName: "shop.localhost", returnWindowDays: 14,
  theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 } };
const primary: StoreDomain = { edge: null, zone: null, id: "platform", hostName: "shop.localhost", isPrimary: true, isVerified: true, verifiedAt: "2026-01-01T00:00:00Z", challengeName: null, challengeValue: null, claimExpiresAt: null };
const pending: StoreDomain = { edge: null, zone: null, id: "custom", hostName: "shop.example.cz", isPrimary: false, isVerified: false, verifiedAt: null, challengeName: "_shopforge-challenge.shop.example.cz", challengeValue: "exact-proof-123", claimExpiresAt: "2030-01-01T00:00:00Z" };
const verified: StoreDomain = { ...pending, isVerified: true, verifiedAt: "2026-10-15T00:00:00Z", challengeName: null, challengeValue: null, claimExpiresAt: null };
const operationId = "11111111-1111-1111-1111-111111111111", zoneId = "22222222-2222-2222-2222-222222222222";
const edge = { state: "binding", operationId, recordsByPlatform: false, validation: "pendingRevalidation", records: [{ type: "TXT", name: "_dnsauth.shop.example.cz", value: '<script>literal</script>\nexact token', purpose: "validation" }, { type: "CNAME", name: "shop.example.cz", value: "edge.example.test", purpose: "traffic" }] };
const zone = { name: "example.cz", state: "waiting", nameServers: ["one.example.test", "two.example.test"], operationId: zoneId };
let rows: StoreDomain[];
const reload = vi.fn();
function deferred<T>() {
  let resolve!: (value: T) => void; let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
function session(role: string) { return { user: { id: "owner", role, email: "owner@example.test", tenantId: "tenant" }, logout: vi.fn(), logoutPending: false, logoutError: null }; }
function Shell({ refreshing = false, refreshError = null, role: initialRole = "Owner" }: { refreshing?: boolean; refreshError?: unknown; role?: string }) {
  const [selected, setSelected] = useState(store);
  const [role, setRole] = useState(initialRole), [staff, setStaff] = useState("owner");
  const navigate = useNavigate();
  return <SessionContext value={{ ...session(role), user: { ...session(role).user, id: staff } }}>
    <button onClick={() => { setSelected({ ...store, id: "store-b" }); navigate("/stores/store-b/domains"); }}>Other store</button>
    <button onClick={() => setRole("Support")}>New role</button><button onClick={() => setStaff("other-owner")}>New session</button>
    <button onClick={() => setSelected(current => ({ ...current, name: "Renamed shop" }))}>Rename store</button>
    <Outlet context={{ store: selected, reloadStores: reload, storeRefreshing: refreshing, storeRefreshError: refreshError }} />
  </SessionContext>;
}
function route(options: { refreshing?: boolean; refreshError?: unknown; role?: string } = {}) {
  return render(<MemoryRouter initialEntries={["/stores/store-a/domains"]}><Routes><Route element={<Shell {...options} />}>
    <Route path="/stores/:storeId/domains" element={<StoreDomainsPage />} />
    <Route path="/stores/:storeId/settings" element={<StoreSettingsPage />} />
    <Route path="/stores/:storeId/setup" element={<StoreSetupPage />} /><Route path="/stores/:storeId/setup/:operationId" element={<StoreSetupDetailPage />} />
  </Route></Routes></MemoryRouter>);
}
async function ready() { await screen.findByRole("heading", { level: 3, name: primary.hostName }); }
function row(host = pending.hostName) { return within(screen.getByRole("heading", { level: 3, name: host }).closest("li")!); }
beforeAll(initializeI18n);
beforeEach(async () => {
  vi.restoreAllMocks(); reload.mockReset(); rows = [primary, pending];
  vi.stubGlobal("CSS", { escape: (value: string) => value }); HTMLElement.prototype.scrollIntoView = vi.fn();
  await i18n.changeLanguage("en");
  vi.spyOn(api, "storeDomains").mockImplementation(async () => [...rows]);
  vi.spyOn(api, "addStoreDomain").mockResolvedValue(pending);
  vi.spyOn(api, "verifyStoreDomain").mockImplementation(async () => { rows = [primary, verified]; return verified; });
  vi.spyOn(api, "makeStoreDomainPrimary").mockImplementation(async () => {
    rows = [{ ...primary, isPrimary: false }, { ...verified, isPrimary: true }]; return rows[1];
  });
  vi.spyOn(api, "removeStoreDomain").mockImplementation(async () => { rows = [primary]; });
  vi.spyOn(api, "startStoreDomainZone").mockImplementation(async (_store, _domain, name) => {
    rows = [primary, { ...pending, zone: { ...zone, name: name ?? "example.cz" } }]; return { operationId: zoneId };
  });
  vi.spyOn(api, "storeReadiness").mockResolvedValue({ isReady: false, checks: [{ check: "store.primary-domain", isReady: false, problem: "No proof", weight: "required" }] });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

describe("domain onboarding", () => {
  it("shows exact DNS proof, ownership-only wording and primary protection", async () => {
    route(); await ready();
    expect(screen.getByRole("textbox", { name: "TXT record name" })).toHaveProperty("value", pending.challengeName);
    expect(screen.getByRole("textbox", { name: "TXT record value" })).toHaveProperty("value", pending.challengeValue);
    expect(screen.getByText(i18n.t("domains:edgeHint"))).toBeTruthy();
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
    expect(api.addStoreDomain).toHaveBeenLastCalledWith("store-a", "shop.new.cz", expect.any(AbortSignal));
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

describe("domain edge progress", () => {
  it("keeps ownership proof, manual edge records and registrar delegation separate", async () => {
    rows = [primary, { ...pending, edge, zone }]; route(); await ready();
    const card = row(), edgePanel = card.getByRole("region", { name: "Edge connection" }), zonePanel = card.getByRole("region", { name: "Managed DNS zone" });
    expect(card.getByRole("textbox", { name: "TXT record value" })).toHaveProperty("value", pending.challengeValue);
    expect(within(edgePanel).getByText(/<script>literal<\/script>/).textContent).toBe(edge.records[0].value); expect(within(edgePanel).getByText("pendingRevalidation")).toBeTruthy(); expect(within(edgePanel).getByText(/CNAME flattening or ALIAS/)).toBeTruthy();
    expect(within(zonePanel).getByText(zone.name)).toBeTruthy(); expect(within(zonePanel).getByText(zone.nameServers[0])).toBeTruthy(); expect(within(zonePanel).getByText(/domain registrar/)).toBeTruthy();
    expect(card.getByRole("link", { name: /View edge setup process/ }).getAttribute("href")).toBe(`/stores/store-a/setup/${operationId}`);
    expect(card.getByRole("link", { name: /View DNS zone process/ }).getAttribute("href")).toBe(`/stores/store-a/setup/${zoneId}`);
    expect(document.querySelector("script, [style]")).toBeNull();
  });
  it("shows nulls, absent operation IDs and empty platform-managed records without inventing readiness or instructions", async () => {
    rows = [primary, { ...verified, edge: { ...edge, state: "bound", operationId: null, validation: null, recordsByPlatform: true, records: [] }, zone: { ...zone, state: "active", nameServers: [], operationId: null } }];
    route(); await ready(); expect(row(primary.hostName).getByText(/No edge view was returned/)).toBeTruthy();
    expect(row().getByText("Edge binding recorded")).toBeTruthy(); expect(row().getByText("No manual edge records were returned. The platform publishes them.")).toBeTruthy();
    expect(row().getByText(/Nameservers have not been supplied/)).toBeTruthy(); expect(row().queryAllByRole("link", { name: /View .* process/ })).toHaveLength(0);
    expect(row().queryByText("CNAME")).toBeNull(); expect(row().getByText(/do not confirm store ownership proof/)).toBeTruthy();
  });
  it.each(["unproved", "waiting", "binding", "bound", "unbinding"])("renders the known %s state as recorded progress, independently of the ownership flag", async state => {
    rows = [primary, { ...verified, edge: { ...edge, state } }]; route(); await ready();
    expect(row().getByText(i18n.t(`domainEdge:states.${state as "unproved" | "waiting" | "binding" | "bound" | "unbinding"}`))).toBeTruthy();
    expect(row().getByRole("button", { name: /Make .* primary/ })).toHaveProperty("disabled", false); expect(api.makeStoreDomainPrimary).not.toHaveBeenCalled();
  });
  it("keeps unknown states/purposes and empty strings inert, without adding edge actions or gating primary on guessed edge states", async () => {
    rows = [primary, { ...verified, edge: { ...edge, state: "future", validation: "future-provider", operationId: null, records: [{ type: "", name: "", value: "", purpose: "__proto__" }] }, zone: { ...zone, state: "future" } }]; route(); await ready();
    expect(row().getAllByText("Unrecognised status: future")).toHaveLength(2); expect(row().getByText("future-provider")).toBeTruthy(); expect(row().getByText(/Other purpose: __proto__/)).toBeTruthy(); expect(row().getAllByText("Empty string")).toHaveLength(3);
    expect(row().queryByRole("button", { name: /Bind|Revalidate|Set up DNS/ })).toBeNull(); expect(row().getByRole("button", { name: /Make .* primary/ })).toHaveProperty("disabled", false);
  });
  it("copies exact edge values and registrar nameservers with a manual fallback", async () => {
    const user = userEvent.setup(); rows = [primary, { ...verified, edge, zone }]; route(); await ready();
    const copy = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    await user.click(row().getByRole("button", { name: `Copy ${verified.hostName} edge record 1: Record value` })); expect(copy).toHaveBeenCalledWith(edge.records[0].value);
    copy.mockRejectedValueOnce(new Error("permission")); await user.click(row().getByRole("button", { name: `Copy ${verified.hostName} nameserver 1` }));
    expect(copy).toHaveBeenLastCalledWith(zone.nameServers[0]); await screen.findByText("Could not copy. Select and copy the value shown.");
  });
  it("reads waiting/binding after successful ownership proof without claiming live HTTPS or blocking a proved primary choice", async () => {
    const user = userEvent.setup(); vi.mocked(api.verifyStoreDomain).mockImplementationOnce(async () => { rows = [primary, { ...verified, edge: { ...edge, state: "waiting", operationId: null, records: [] } }]; return rows[1]; });
    route(); await ready(); await user.click(row().getByRole("button", { name: /Check ownership/ }));
    await screen.findByText("Waiting for edge setup"); expect(row().getByText("Ownership verified")).toBeTruthy(); expect(row().getByRole("button", { name: /Make .* primary/ })).toHaveProperty("disabled", false);
    expect(row().queryByText("Edge binding recorded")).toBeNull(); expect(row().getByText(/No edge operation ID was supplied/)).toBeTruthy(); expect(api.verifyStoreDomain).toHaveBeenCalledTimes(1);
  });
  it("requires a new review after the recorded edge snapshot changes during primary confirmation", async () => {
    const user = userEvent.setup(); rows = [primary, { ...verified, edge }]; route(); await ready(); await user.click(row().getByRole("button", { name: /Make .* primary/ }));
    rows = [primary, { ...verified, edge: { ...edge, validation: "approved" } }];
    await user.click(screen.getByRole("button", { name: "Refresh addresses" })); await waitFor(() => expect(screen.queryByRole("group", { name: /primary address/ })).toBeNull());
    await user.click(row().getByRole("button", { name: /Make .* primary/ })); await user.click(screen.getByRole("button", { name: "Rename store" }));
    expect(row().getByRole("button", { name: "Confirm change" })).toHaveProperty("disabled", true); expect(api.makeStoreDomainPrimary).not.toHaveBeenCalled();
  });
  it.each([401, 403, 404])("hides stale edge records after a %i refresh and never retries a write", async status => {
    const user = userEvent.setup(); rows = [primary, { ...verified, edge, zone }]; route(); await ready(); vi.mocked(api.storeDomains).mockRejectedValueOnce(new HttpError("http", status));
    await user.click(screen.getByRole("button", { name: "Refresh addresses" })); await screen.findByRole("alert");
    expect(screen.queryByText(edge.records[0].value)).toBeNull(); expect(screen.queryByRole("link", { name: /View edge setup process/ })).toBeNull(); expect(screen.queryByText(i18n.t("domains:empty"))).toBeNull(); expect(api.removeStoreDomain).not.toHaveBeenCalled();
  });
  it("keeps failed edge refreshes visibly stale and recovers by GET only", async () => {
    const user = userEvent.setup(); rows = [primary, { ...verified, edge }]; route(); await ready(); vi.mocked(api.storeDomains).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Refresh addresses" })); await screen.findByRole("alert"); expect(row().getByText(/<script>literal<\/script>/).textContent).toBe(edge.records[0].value); expect(row().getByRole("button", { name: /Make .* primary/ })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(row().getByRole("button", { name: /Make .* primary/ })).toHaveProperty("disabled", false)); expect(api.makeStoreDomainPrimary).not.toHaveBeenCalled();
  });
  it("retains an uncertain write across a failed recovery read and never replays it with Try again", async () => {
    const user = userEvent.setup(); rows = [primary, { ...verified, edge }]; route(); await ready();
    vi.mocked(api.removeStoreDomain).mockRejectedValueOnce(new HttpError("network", null)); vi.mocked(api.storeDomains).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(row().getByRole("button", { name: /Remove/ })); await user.click(row().getByRole("button", { name: "Confirm change" })); await screen.findByText(/change is unconfirmed and may have been applied/);
    expect(screen.getByRole("button", { name: "Finish review of current addresses" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect(screen.getByRole("button", { name: "Finish review of current addresses" })).toHaveProperty("disabled", false));
    expect(row().getByRole("button", { name: /Remove/ })).toHaveProperty("disabled", true); await user.click(screen.getByRole("button", { name: "Finish review of current addresses" }));
    await user.click(row().getByRole("button", { name: /Remove/ })); expect(row().getByRole("group", { name: `Remove ${verified.hostName}?` })).toBeTruthy(); expect(api.removeStoreDomain).toHaveBeenCalledTimes(1);
  });
  it("keeps a 204 claim removal separate from a still-running unbind process and links its actual ID from Setup progress", async () => {
    const user = userEvent.setup(); rows = [primary, { ...verified, edge }];
    const summary = { id: zoneId, kind: "domain.unbind", subject: verified.hostName, state: "running", currentStep: "traffic", createdAt: "2026-10-24T07:00:00Z", finishedAt: null };
    vi.spyOn(setupProgressApi, "list").mockResolvedValue([summary]); vi.spyOn(setupProgressApi, "detail").mockResolvedValue({ ...summary, stepRunning: true, changedAt: summary.createdAt, nextCheckAt: null, actions: ["cancel"], steps: [] });
    route(); await ready(); await user.click(row().getByRole("button", { name: /Remove/ })); await user.click(row().getByRole("button", { name: "Confirm change" }));
    await waitFor(() => expect(screen.queryByRole("heading", { name: verified.hostName })).toBeNull()); expect(screen.getByText(/Edge cleanup may still be running/)).toBeTruthy();
    expect(screen.queryByRole("link", { name: /View edge setup process/ })).toBeNull(); await user.click(screen.getAllByRole("link", { name: "Open Setup progress" })[0]);
    await user.click(await screen.findByRole("link", { name: "Disconnect domain" })); await screen.findByRole("heading", { name: "Setup details" });
    expect(setupProgressApi.detail).toHaveBeenCalledExactlyOnceWith("store-a", zoneId, expect.any(AbortSignal)); expect(screen.getByText("In progress")).toBeTruthy(); expect(api.removeStoreDomain).toHaveBeenCalledTimes(1);
  });
  it.each(["store", "role", "session", "unmount"])("aborts an in-flight ownership change and discards its late reply on %s departure", async departure => {
    const user = userEvent.setup(), held = deferred<StoreDomain>(); const view = route(); await ready(); vi.mocked(api.verifyStoreDomain).mockReturnValueOnce(held.promise);
    await user.click(row().getByRole("button", { name: /Check ownership/ }));
    if (departure === "unmount") view.unmount(); else await user.click(screen.getByRole("button", { name: departure === "store" ? "Other store" : departure === "role" ? "New role" : "New session" }));
    expect(vi.mocked(api.verifyStoreDomain).mock.calls[0][2]?.aborted).toBe(true); const reads = vi.mocked(api.storeDomains).mock.calls.length;
    await act(async () => held.resolve({ ...verified, edge })); expect(screen.queryByText(/Ownership verified. Live HTTPS/)).toBeNull(); expect(api.storeDomains).toHaveBeenCalledTimes(reads); expect(reload).not.toHaveBeenCalled();
  });
  it.each(["en", "cs"])("renders %s delegation controls without offering another start for an existing zone", async language => {
    await i18n.changeLanguage(language); rows = [primary, { ...pending, edge, zone }]; route(); await ready();
    expect(row().getByRole("region", { name: i18n.t("domainEdge:edgeTitle") })).toBeTruthy(); expect(row().getByText(i18n.t("domainEdge:delegationHint"))).toBeTruthy();
    expect(row().queryByRole("button", { name: i18n.t("managedDns:open", { host: pending.hostName }) })).toBeNull();
  });
});

async function zoneReview(user: ReturnType<typeof userEvent.setup>, value = "") {
  await user.click(row().getByRole("button", { name: i18n.t("managedDns:open", { host: pending.hostName }) }));
  if (value) await user.type(screen.getByRole("textbox", { name: i18n.t("managedDns:zoneLabel") }), value);
  await user.click(screen.getByRole("button", { name: i18n.t("managedDns:review") }));
  return screen.getByRole("form", { name: i18n.t("managedDns:reviewTitle") });
}
async function acknowledgeZone(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("checkbox", { name: i18n.t("managedDns:acknowledge") }));
  return screen.getByRole("button", { name: i18n.t("managedDns:start") });
}

describe("managed DNS delegation", () => {
  it.each(["retry", "cancel"] as const)("opens the returned zone operation and reuses guarded %s recovery", async action => {
    const user = userEvent.setup(), at = "2026-10-24T19:00:00Z";
    const detail: SetupDetail = { id: zoneId, kind: "domain.zone", subject: "zone:example.cz", state: action === "retry" ? "failed" : "running",
      createdAt: at, finishedAt: null, changedAt: at, nextCheckAt: null, stepRunning: false, actions: [action],
      steps: [{ name: "delegation", status: action === "retry" ? "failed" : "waiting", resourceId: null, created: false, requestId: null, keepsSecret: false,
        attempts: 1, checks: 1, waitingSince: at, note: "Point the nameservers at one.example.test and two.example.test.", lastError: null,
        lastErrorAt: null, startedAt: at, finishedAt: null }] };
    const next = { ...detail, state: action === "retry" ? "running" : "cancelling", actions: [] };
    const read = vi.spyOn(setupProgressApi, "detail").mockResolvedValueOnce(detail).mockResolvedValue(next);
    const act = vi.spyOn(setupProgressApi, "act").mockResolvedValue(next);
    route(); await ready(); await zoneReview(user); await user.click(await acknowledgeZone(user));
    await user.click(await screen.findByRole("link", { name: /Open DNS setup process/ }));
    await screen.findByRole("heading", { name: "Setup details" });
    expect(await screen.findByText(detail.steps[0].note!)).toBeTruthy();
    expect(read).toHaveBeenCalledExactlyOnceWith("store-a", zoneId, expect.any(AbortSignal));
    await user.click(screen.getByRole("button", { name: i18n.t(`setup:recovery.actions.${action}.label`) }));
    expect(act).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: i18n.t(`setup:recovery.actions.${action}.confirm`) }));
    await screen.findByText(i18n.t("setup:recovery.accepted"));
    expect(act).toHaveBeenCalledExactlyOnceWith("store-a", zoneId, action, expect.any(AbortSignal));
    expect(api.startStoreDomainZone).toHaveBeenCalledOnce();
  });

  it.each(["en", "cs"])("reviews the default zone, requires acknowledgment and shows accepted progress in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); route(); await ready();
    const form = await zoneReview(user);
    expect(within(form).getByText("example.cz", { exact: true })).toBeTruthy();
    expect(within(form).getByText(pending.hostName, { exact: true })).toBeTruthy();
    expect(within(form).getByText(i18n.t("managedDns:noMigration"))).toBeTruthy();
    expect(document.activeElement).toBe(within(form).getByRole("heading"));
    fireEvent.submit(form); expect(api.startStoreDomainZone).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: i18n.t("managedDns:start") })).toHaveProperty("disabled", true);
    await user.click(await acknowledgeZone(user));
    const link = await screen.findByRole("link", { name: new RegExp(i18n.t("managedDns:operation")) });
    expect(link.getAttribute("href")).toBe(`/stores/store-a/setup/${zoneId}`);
    expect(api.startStoreDomainZone).toHaveBeenCalledExactlyOnceWith("store-a", pending.id, null, expect.any(AbortSignal));
    expect(await screen.findByText(zone.nameServers[0])).toBeTruthy();
    expect(row().getByText(i18n.t("domains:unverified"))).toBeTruthy();
    expect(screen.getByText(i18n.t("managedDns:acceptedHint"))).toBeTruthy();
    expect(screen.queryByRole("textbox", { name: i18n.t("managedDns:zoneLabel") })).toBeNull();
    expect(reload).toHaveBeenCalledOnce();
  });

  it("validates a containing zone, normalizes an explicit zone and serializes the command with ownership writes", async () => {
    const user = userEvent.setup(); route(); await ready();
    await user.click(row().getByRole("button", { name: "Set up DNS for shop.example.cz" }));
    const input = screen.getByRole("textbox", { name: "DNS zone (optional)" });
    expect(document.activeElement).toBe(input);
    await user.type(input, "other.cz"); await user.click(screen.getByRole("button", { name: "Review DNS setup" }));
    expect(input.getAttribute("aria-invalid")).toBe("true"); expect(document.activeElement).toBe(input);
    expect(api.startStoreDomainZone).not.toHaveBeenCalled();
    await user.clear(input); await user.type(input, " SHOP.Example.CZ. ");
    await user.click(screen.getByRole("button", { name: "Review DNS setup" }));
    const form = screen.getByRole("form", { name: "Review the domain and DNS zone" });
    expect(within(form).getAllByText(pending.hostName, { exact: true })).toHaveLength(2);
    await acknowledgeZone(user);
    const held = deferred<{ operationId: string }>(); vi.mocked(api.startStoreDomainZone).mockReturnValueOnce(held.promise);
    act(() => { fireEvent.submit(form); fireEvent.submit(form); });
    expect(api.startStoreDomainZone).toHaveBeenCalledExactlyOnceWith("store-a", pending.id, "shop.example.cz", expect.any(AbortSignal));
    expect(row().getByRole("button", { name: /Check ownership/ })).toHaveProperty("disabled", true);
    expect(screen.getByRole("button", { name: "Close DNS setup" })).toHaveProperty("disabled", true);
    await user.keyboard("{Escape}"); expect(screen.getByRole("form", { name: "Review the domain and DNS zone" })).toBeTruthy();
    rows = [primary, { ...pending, zone }]; await act(async () => held.resolve({ operationId: zoneId }));
    await screen.findByText(i18n.t("managedDns:accepted")); expect(api.verifyStoreDomain).not.toHaveBeenCalled();
  });

  it("retains the zone draft through editing, Escape and cancellation without issuing a command", async () => {
    const user = userEvent.setup(); route(); await ready(); await zoneReview(user, "shop.example.cz");
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("form", { name: "Review the domain and DNS zone" })).toBeNull();
    expect(screen.getByRole("textbox", { name: "DNS zone (optional)" })).toHaveProperty("value", "shop.example.cz");
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Review DNS setup" })));
    await user.keyboard("{Escape}");
    await waitFor(() => expect(document.activeElement).toBe(row().getByRole("button", { name: "Set up DNS for shop.example.cz" })));
    expect(screen.queryByRole("textbox", { name: "DNS zone (optional)" })).toBeNull(); expect(api.startStoreDomainZone).not.toHaveBeenCalled();
    expect(row().getByRole("textbox", { name: "TXT record value" })).toHaveProperty("value", pending.challengeValue);
  });

  it.each(["zone", "domainId"])("retains a refused draft and focuses the mapped %s error", async field => {
    const user = userEvent.setup(); route(); await ready(); await zoneReview(user, " SHOP.Example.CZ. ");
    vi.mocked(api.startStoreDomainZone).mockRejectedValueOnce(new HttpError("http", 400, { errors: { [field]: ["Backend validation"] } }));
    await user.click(await acknowledgeZone(user)); await screen.findByRole("alert");
    const input = screen.getByRole("textbox", { name: "DNS zone (optional)" });
    expect(input).toHaveProperty("value", " SHOP.Example.CZ. ");
    expect(screen.queryByRole("form", { name: "Review the domain and DNS zone" })).toBeNull();
    await user.click(within(screen.getByRole("alert")).getByRole("button"));
    expect(document.activeElement).toBe(document.getElementById(field === "zone" ? "zone-name" : "zone-domain"));
    await user.click(screen.getByRole("button", { name: "Review DNS setup" }));
    expect(screen.getByRole("checkbox", { name: i18n.t("managedDns:acknowledge") })).toHaveProperty("checked", false);
    expect(api.startStoreDomainZone).toHaveBeenCalledOnce();
  });

  it.each([
    [404, "Not Found", "domainMissing"], [409, "That zone is already taken", "taken"],
    [409, "Not here", "unavailable"], [409, "Not now", "busy"], [409, "Unknown conflict", null],
  ] as const)("shows the %i / %s refusal and never replays it on refresh", async (status, title, key) => {
    const user = userEvent.setup(); route(); await ready(); await zoneReview(user, "example.cz");
    vi.mocked(api.startStoreDomainZone).mockRejectedValueOnce(new HttpError("http", status, { title, detail: '<script>literal detail</script>' }));
    await user.click(await acknowledgeZone(user)); await screen.findByRole("alert");
    if (key) expect(screen.getByText(i18n.t(`managedDns:${key}`))).toBeTruthy();
    if (status === 409) expect(screen.getByText('<script>literal detail</script>').textContent).toBe('<script>literal detail</script>');
    expect(document.querySelector(".domain-zone-outcome script")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Refresh addresses" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Review DNS setup" })).toHaveProperty("disabled", false));
    expect(screen.getByRole("textbox", { name: "DNS zone (optional)" })).toHaveProperty("value", "example.cz");
    expect(api.startStoreDomainZone).toHaveBeenCalledOnce();
  });

  it.each([zoneId, "wrong"])("links only an actual checked conflict operation ID: %s", async operationId => {
    const user = userEvent.setup(); route(); await ready(); await zoneReview(user);
    vi.mocked(api.startStoreDomainZone).mockRejectedValueOnce(new HttpError("http", 409, { title: "Not now", operationId }));
    await user.click(await acknowledgeZone(user)); await screen.findByRole("alert");
    const link = screen.queryByRole("link", { name: /Open DNS setup process/ });
    expect(link !== null).toBe(operationId === zoneId);
    if (link) expect(link.getAttribute("href")).toBe(`/stores/store-a/setup/${zoneId}`);
  });

  it.each([new HttpError("network", null), new HttpError("invalid-response", 202), new HttpError("http", 503)])("requires GET recovery and another explicit review after an uncertain start: %j", async error => {
    const user = userEvent.setup(); route(); await ready(); await zoneReview(user, "example.cz");
    vi.mocked(api.startStoreDomainZone).mockRejectedValueOnce(error); vi.mocked(api.storeDomains).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(await acknowledgeZone(user)); await screen.findByText(i18n.t("managedDns:uncertain"));
    expect(screen.getByRole("button", { name: "Finish review of current addresses" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Finish review of current addresses" })).toHaveProperty("disabled", false));
    expect(screen.getByRole("button", { name: "Review DNS setup" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Finish review of current addresses" }));
    await user.click(screen.getByRole("button", { name: "Review DNS setup" }));
    expect(screen.getByRole("button", { name: "Start DNS setup" })).toHaveProperty("disabled", true);
    expect(api.startStoreDomainZone).toHaveBeenCalledOnce();
  });

  it.each(["proved", "zone", "removed", "store-name"])("requires a fresh review when %s changes during confirmation", async change => {
    const user = userEvent.setup(); route(); await ready(); await zoneReview(user, "example.cz"); await acknowledgeZone(user);
    if (change === "store-name") await user.click(screen.getByRole("button", { name: "Rename store" }));
    else {
      rows = change === "removed" ? [primary] : [primary, change === "proved" ? verified : { ...pending, zone }];
      await user.click(screen.getByRole("button", { name: "Refresh addresses" }));
    }
    await screen.findByText(i18n.t("managedDns:changed"));
    expect(screen.getByRole("button", { name: "Start DNS setup" })).toHaveProperty("disabled", true);
    fireEvent.submit(screen.getByRole("form", { name: "Review the domain and DNS zone" })); expect(api.startStoreDomainZone).not.toHaveBeenCalled();
    expect(screen.getByRole("textbox", { name: "DNS zone (optional)" })).toHaveProperty("value", "example.cz");
    await user.click(screen.getByRole("button", { name: "Close DNS setup" }));
    expect(screen.queryByRole("textbox", { name: "DNS zone (optional)" })).toBeNull();
  });

  it("rejects a submit racing a new read before the pending state can repaint", async () => {
    const user = userEvent.setup(); route(); await ready(); await zoneReview(user); await acknowledgeZone(user);
    const held = deferred<StoreDomain[]>(); vi.mocked(api.storeDomains).mockReturnValueOnce(held.promise);
    act(() => { fireEvent.click(screen.getByRole("button", { name: "Refresh addresses" })); fireEvent.submit(screen.getByRole("form", { name: "Review the domain and DNS zone" })); });
    expect(api.startStoreDomainZone).not.toHaveBeenCalled(); await act(async () => held.resolve(rows));
  });

  it.each(["store", "role", "session", "unmount"])("aborts zone setup and ignores its late reply after %s departure", async departure => {
    const user = userEvent.setup(), held = deferred<{ operationId: string }>(); const view = route(); await ready(); await zoneReview(user);
    vi.mocked(api.startStoreDomainZone).mockReturnValueOnce(held.promise); await user.click(await acknowledgeZone(user));
    if (departure === "unmount") view.unmount(); else await user.click(screen.getByRole("button", { name: departure === "store" ? "Other store" : departure === "role" ? "New role" : "New session" }));
    expect(vi.mocked(api.startStoreDomainZone).mock.calls[0][3]?.aborted).toBe(true);
    const count = vi.mocked(api.storeDomains).mock.calls.length;
    await act(async () => held.resolve({ operationId: zoneId }));
    expect(screen.queryByText(i18n.t("managedDns:accepted"))).toBeNull(); expect(reload).not.toHaveBeenCalled(); expect(api.storeDomains).toHaveBeenCalledTimes(count);
  });

  it.each([401, 403, 404])("hides the zone draft and private operation link after a %i read", async status => {
    const user = userEvent.setup(); route(); await ready(); await zoneReview(user, "example.cz");
    vi.mocked(api.storeDomains).mockRejectedValueOnce(new HttpError("http", status));
    await user.click(screen.getByRole("button", { name: "Refresh addresses" })); await screen.findByRole("alert");
    expect(screen.queryByRole("textbox", { name: "DNS zone (optional)" })).toBeNull();
    expect(screen.queryByRole("link", { name: /Open DNS setup process/ })).toBeNull(); expect(api.startStoreDomainZone).not.toHaveBeenCalled();
  });
});
