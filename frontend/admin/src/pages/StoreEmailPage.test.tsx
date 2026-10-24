// @vitest-environment jsdom
import { useState } from "react";
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes, useNavigate } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api, type AdminStore } from "../api";
import * as domains from "../api/domains";
import { HttpError } from "../api/http";
import { setupProgressApi } from "../api/setupProgress";
import { storeEmailApi, type SendingDomain, type StoreEmail } from "../api/storeEmail";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { StoreEmailPage } from "./StoreEmailPage";
import { StoreSetupDetailPage } from "./StoreSetupPage";

const id = "11111111-1111-1111-1111-111111111111";
const store: AdminStore = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", status: "published", logoUrl: null, company: null, primaryHostName: "shop.localhost", returnWindowDays: 14, theme: { primaryColor: "#123456", secondaryColor: "#eef4ff", borderRadius: 6 } };
const address: domains.StoreDomain = { edge: null, zone: null, id: "own", hostName: "shop.cz", isPrimary: true, isVerified: true, verifiedAt: "2026-10-24T07:00:00Z", challengeName: null, challengeValue: null, claimExpiresAt: null };
const fallback: StoreEmail = { sendsFromOwnDomain: false, platformAddress: "platform@example.test", domain: null };
const domain: SendingDomain = { domain: "mail.shop.cz", state: "awaitingDns", senderAddress: "orders@shop.cz", replyTo: null, region: "eu", operationId: id, verifiedAt: null,
  records: [{ purpose: "sending", type: "TXT", name: "mail.shop.cz", value: '<script>alert(1)</script>\nexact TXT', priority: null, valid: "unknown" }, { purpose: "receiving", type: "MX", name: null, value: "mxa.example.test", priority: "0", valid: "valid" }],
  recommended: [{ purpose: "dmarc", type: "TXT", name: "_dmarc.shop.cz", value: "v=DMARC1; p=none", priority: null, valid: null }] };
function Shell({ role: initialRole = "Owner" }: { role?: string }) {
  const [selected, setSelected] = useState(store), [role, setRole] = useState(initialRole), [staff, setStaff] = useState("staff"), [storeRefreshing, setStoreRefreshing] = useState(false);
  const navigate = useNavigate();
  return <SessionContext value={{ user: { id: staff, email: "staff@example.test", role, tenantId: "tenant" }, logout: vi.fn(), logoutPending: false, logoutError: null }}>
    <button onClick={() => { setSelected({ ...store, id: "store-b", name: "Other shop" }); navigate("/stores/store-b/email"); }}>Other store</button>
    <button onClick={() => setRole("Support")}>New role</button><button onClick={() => setStaff("new-staff")}>New session</button>
    <button onClick={() => setStoreRefreshing(value => !value)}>Toggle store refresh</button>
    <Outlet context={{ store: selected, storeRefreshing, storeRefreshError: null, reloadStores: vi.fn() }} />
  </SessionContext>;
}
function app(role = "Owner") { return <MemoryRouter initialEntries={["/stores/store-a/email"]}><Routes><Route element={<Shell role={role} />}>
  <Route path="/stores/:storeId/email" element={<StoreEmailPage />} /><Route path="/stores/:storeId/setup/:operationId" element={<StoreSetupDetailPage />} />
</Route></Routes></MemoryRouter>; }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  Element.prototype.scrollIntoView = vi.fn();
  window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en");
  vi.spyOn(storeEmailApi, "get").mockResolvedValue(fallback); vi.spyOn(storeEmailApi, "connect").mockResolvedValue({ operationId: id }); vi.spyOn(storeEmailApi, "remove").mockResolvedValue({ operationId: id });
  vi.spyOn(domains, "getStoreDomains").mockResolvedValue([address]);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
async function page(value: StoreEmail = fallback, role = "Owner") {
  vi.mocked(storeEmailApi.get).mockResolvedValue(value); const user = userEvent.setup(), view = render(app(role));
  await screen.findByRole("heading", { name: "Current mail sender" }); return { user, ...view };
}
async function fill(user: ReturnType<typeof userEvent.setup>) {
  await waitFor(() => expect((screen.getByRole("button", { name: "Review connection" }) as HTMLButtonElement).disabled).toBe(false));
  await user.type(screen.getByLabelText("Sending domain"), " Mail.Shop.CZ. "); await user.type(screen.getByLabelText("Sender address"), " Orders@Shop.CZ ");
}
async function review(user: ReturnType<typeof userEvent.setup>) { await fill(user); await user.click(screen.getByRole("button", { name: "Review connection" })); }
it("keeps platform fallback distinct from a missing connection and explains eligibility without certifying platform hosts", async () => {
  await page(); expect(screen.getByText(/server reports that mail uses the platform sender/)).toBeTruthy(); expect(screen.getAllByText("platform@example.test")).toHaveLength(2);
  expect(screen.getByRole("heading", { name: "No sending domain is configured" })).toBeTruthy(); expect(screen.getByText(/address list does not distinguish them/)).toBeTruthy();
  expect(screen.getByRole("link", { name: "Manage storefront domains" }).getAttribute("href")).toBe("/stores/store-a/domains"); expect(storeEmailApi.connect).not.toHaveBeenCalled();
});
it.each(["CatalogManager", "OrderManager", "Warehouse", "Support", "futureRole"])("allows %s to read email truth without ownership reads or mutations", async role => {
  await page({ ...fallback, domain }, role); expect(screen.getByRole("heading", { name: "mail.shop.cz" })).toBeTruthy(); expect(domains.getStoreDomains).not.toHaveBeenCalled();
  expect(screen.queryByRole("button", { name: /Review connection|Review domain removal/ })).toBeNull();
});
it("shows exact Mailgun records, domain-root receiving semantics and separate platform advice as inert text", async () => {
  await page({ ...fallback, domain }); const provider = screen.getByRole("region", { name: "DNS records from Mailgun" }), advice = screen.getByRole("region", { name: "Platform DMARC recommendation" });
  expect(within(provider).getByText(/<script>alert/)).toBeTruthy(); expect(within(provider).getByText("0")).toBeTruthy(); expect(within(provider).getByText("valid")).toBeTruthy();
  expect(within(provider).getByText(/omitted name means the sending domain itself/)).toBeTruthy(); expect(within(advice).getByText("v=DMARC1; p=none")).toBeTruthy();
  expect(within(provider).queryByText("v=DMARC1; p=none")).toBeNull(); expect(document.querySelector("script, [style]")).toBeNull();
});
it("copies the exact provider value and offers selectable fallback when clipboard is denied", async () => {
  const { user } = await page({ ...fallback, domain }), copy = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
  await user.click(screen.getByRole("button", { name: "Copy Mailgun record 1: Value" })); expect(copy).toHaveBeenCalledWith(domain.records[0].value); await screen.findByText("Copied.");
  copy.mockRejectedValueOnce(new Error("denied")); await user.click(screen.getByRole("button", { name: "Copy domain name for receiving record 2" }));
  await screen.findByText(/Could not copy. Select and copy/); expect(copy).toHaveBeenLastCalledWith(domain.domain);
});
it("preserves unknown and empty provider facts without enabling writes", async () => {
  await page({ ...fallback, domain: { ...domain, state: "future", region: "future", operationId: null, records: [{ ...domain.records[0], purpose: "future", name: "", value: "", valid: "false" }], recommended: [] } });
  expect(screen.getByText("Unrecognised status: future")).toBeTruthy(); expect(screen.getByText("Unrecognised region: future")).toBeTruthy(); expect(screen.getByText(/Other purpose: future/)).toBeTruthy(); expect(screen.getAllByText("Empty string")).toHaveLength(2);
  expect((screen.getByRole("button", { name: "Review domain removal" }) as HTMLButtonElement).disabled).toBe(true); expect(screen.queryByRole("link", { name: /Open setup process/ })).toBeNull();
});
it.each(["Owner", "Admin"])("lets %s review the exact normalized target, close with Escape and restore focus without writing", async role => {
  const { user } = await page(fallback, role); await review(user); const form = screen.getByRole("form", { name: "Review sending-domain connection" });
  expect(within(form).getByText("Workshop")).toBeTruthy(); expect(within(form).getByText("mail.shop.cz")).toBeTruthy(); expect(within(form).getByText("orders@shop.cz")).toBeTruthy();
  expect(document.activeElement).toBe(within(form).getByRole("heading")); await user.keyboard("{Escape}");
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Review connection" }))); expect(storeEmailApi.connect).not.toHaveBeenCalled();
});
it("focuses local field errors and never reviews an invalid or parent-domain draft", async () => {
  const { user } = await page(); await user.click(screen.getByRole("button", { name: "Review connection" })); expect(document.activeElement).toBe(screen.getByLabelText("Sending domain"));
  await user.type(screen.getByLabelText("Sending domain"), "shop.cz"); await user.type(screen.getByLabelText("Sender address"), "orders@shop.cz");
  await user.click(screen.getByRole("button", { name: "Review connection" })); expect(screen.queryByRole("form", { name: "Review sending-domain connection" })).toBeNull(); expect(storeEmailApi.connect).not.toHaveBeenCalled();
});
it("submits a connection once, links the actual process and waits for authoritative email reads", async () => {
  const { user } = await page(); await review(user); const held = deferred<{ operationId: string }>(), after = deferred<StoreEmail>(); vi.mocked(storeEmailApi.connect).mockReturnValueOnce(held.promise); vi.mocked(storeEmailApi.get).mockReturnValueOnce(after.promise);
  const form = screen.getByRole("form", { name: "Review sending-domain connection" }); act(() => { form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); form.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })); });
  expect(storeEmailApi.connect).toHaveBeenCalledExactlyOnceWith("store-a", { domain: "mail.shop.cz", senderAddress: "orders@shop.cz" }, expect.any(AbortSignal));
  expect((screen.getByRole("button", { name: "Refresh email status" }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => held.resolve({ operationId: id })); await screen.findByText(/server accepted a setup process/);
  expect(screen.queryByText("Active")).toBeNull(); expect(screen.getByRole("link", { name: /Open setup process/ }).getAttribute("href")).toBe(`/stores/store-a/setup/${id}`);
  await act(async () => after.resolve({ ...fallback, domain })); await screen.findByText("Waiting for DNS");
  expect(screen.getByText(/server reports that mail uses the platform sender/)).toBeTruthy(); expect(storeEmailApi.connect).toHaveBeenCalledTimes(1);
});
it("reviews removal and reads removing then absent without claiming completed cleanup", async () => {
  const { user } = await page({ ...fallback, sendsFromOwnDomain: true, domain: { ...domain, state: "active" } });
  await user.click(screen.getByRole("button", { name: "Review domain removal" })); const form = screen.getByRole("form", { name: "Review sending-domain removal" });
  expect(within(form).getByText("mail.shop.cz")).toBeTruthy(); vi.mocked(storeEmailApi.get).mockResolvedValueOnce({ ...fallback, domain: { ...domain, state: "removing" } });
  await user.click(within(form).getByRole("button", { name: "Start removal" })); await screen.findByText("Removing");
  expect(storeEmailApi.remove).toHaveBeenCalledExactlyOnceWith("store-a", expect.any(AbortSignal)); expect((screen.getByRole("button", { name: "Review domain removal" }) as HTMLButtonElement).disabled).toBe(true);
  vi.mocked(storeEmailApi.get).mockResolvedValueOnce(fallback); await user.click(screen.getByRole("button", { name: "Refresh email status" })); await screen.findByText(/absence|alone does not confirm/);
  expect(storeEmailApi.remove).toHaveBeenCalledTimes(1);
});
it("retains drafts, maps real 400 fields and rechecks ownership without resending", async () => {
  const { user } = await page(); await review(user); vi.mocked(storeEmailApi.connect).mockRejectedValueOnce(new HttpError("http", 400, { errors: { domain: ["Prove an own domain"], senderAddress: ["Not aligned"], replyTo: ["Invalid"] } }));
  await user.click(screen.getByRole("button", { name: "Start connection" })); await screen.findByText(/server refused the request/);
  expect((screen.getByLabelText("Sending domain") as HTMLInputElement).value).toBe(" Mail.Shop.CZ. "); expect(screen.getByLabelText("Sending domain").getAttribute("aria-invalid")).toBe("true");
  await user.click(screen.getByRole("button", { name: /Enter a subdomain of a verified address/ })); expect(document.activeElement).toBe(screen.getByLabelText("Sending domain"));
  expect(domains.getStoreDomains).toHaveBeenCalledTimes(2); expect(storeEmailApi.connect).toHaveBeenCalledTimes(1);
});
it.each([id, null, "not-an-id"])("shows a 409 explanation and only a valid actual operation link (%s)", async operationId => {
  const { user } = await page(); await review(user); vi.mocked(storeEmailApi.connect).mockRejectedValueOnce(new HttpError("http", 409, { title: "Not now", detail: "A busy store <script>literal</script>", ...(operationId ? { operationId } : {}) }));
  await user.click(screen.getByRole("button", { name: "Start connection" })); await screen.findByText(/server refused the request/); await user.click(screen.getByText("Server explanation"));
  expect(screen.getByText(/busy store <script>/)).toBeTruthy(); expect(document.querySelector("script")).toBeNull(); expect(screen.queryAllByRole("link", { name: /Open setup process/ })).toHaveLength(operationId === id ? 1 : 0); expect(storeEmailApi.connect).toHaveBeenCalledTimes(1);
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 202)])("locks an uncertain start (%j) until a fresh read and explicit review", async error => {
  const { user } = await page(); await review(user); const held = deferred<StoreEmail>(); vi.mocked(storeEmailApi.get).mockReturnValueOnce(held.promise); vi.mocked(storeEmailApi.connect).mockRejectedValueOnce(error);
  await user.click(screen.getByRole("button", { name: "Start connection" })); await screen.findByText(/request is unconfirmed/);
  const finish = screen.getByRole("button", { name: "Finish review of current email status" }); expect((finish as HTMLButtonElement).disabled).toBe(true);
  await act(async () => held.resolve(fallback)); expect((finish as HTMLButtonElement).disabled).toBe(false); expect((screen.getByRole("button", { name: "Review connection" }) as HTMLButtonElement).disabled).toBe(true);
  await user.click(finish); await user.click(screen.getByRole("button", { name: "Review connection" })); expect(screen.getByRole("form", { name: "Review sending-domain connection" })).toBeTruthy(); expect(storeEmailApi.connect).toHaveBeenCalledTimes(1);
});
it("recovers a lost removal by GET without treating the absent domain as a completed operation", async () => {
  const { user } = await page({ ...fallback, domain }); vi.mocked(storeEmailApi.remove).mockRejectedValueOnce(new HttpError("network", null)); vi.mocked(storeEmailApi.get).mockResolvedValueOnce(fallback);
  await user.click(screen.getByRole("button", { name: "Review domain removal" })); await user.click(screen.getByRole("button", { name: "Start removal" }));
  await screen.findByText(/request is unconfirmed/); await screen.findByRole("heading", { name: "No sending domain is configured" }); expect(storeEmailApi.remove).toHaveBeenCalledTimes(1); expect(screen.getByText(/alone does not confirm/)).toBeTruthy();
});
it.each([403, 404])("refreshes after a %i removal refusal and hides inaccessible old records", async status => {
  const { user } = await page({ ...fallback, domain }); vi.mocked(storeEmailApi.remove).mockRejectedValueOnce(new HttpError("http", status)); vi.mocked(storeEmailApi.get).mockRejectedValueOnce(new HttpError("http", status));
  await user.click(screen.getByRole("button", { name: "Review domain removal" })); await user.click(screen.getByRole("button", { name: "Start removal" })); await screen.findByText(/server refused the request/);
  expect(screen.queryByRole("heading", { name: "mail.shop.cz" })).toBeNull(); expect(screen.queryByRole("button", { name: "Review connection" })).toBeNull(); expect(storeEmailApi.remove).toHaveBeenCalledTimes(1);
});
it("keeps email read errors distinct from no-domain truth and retries only GET", async () => {
  vi.mocked(storeEmailApi.get).mockRejectedValueOnce(new HttpError("http", 503)); const user = userEvent.setup(); render(app()); await screen.findByRole("alert");
  expect(screen.queryByText("No sending domain is configured")).toBeNull(); await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByRole("heading", { name: "No sending domain is configured" }); expect(storeEmailApi.connect).not.toHaveBeenCalled();
});
it("requires a new confirmation after authoritative email or ownership facts change", async () => {
  const { user } = await page(); await review(user); vi.mocked(domains.getStoreDomains).mockResolvedValueOnce([{ ...address, isPrimary: false }]);
  await user.click(screen.getByRole("button", { name: "Refresh storefront addresses" })); await screen.findByText(/Email, address or store details changed/);
  expect((screen.getByRole("button", { name: "Start connection" }) as HTMLButtonElement).disabled).toBe(true); expect(storeEmailApi.connect).not.toHaveBeenCalled();
});
it("blocks stale email, stale ownership and pending store metadata before sending", async () => {
  const { user } = await page(); await review(user); await user.click(screen.getByRole("button", { name: "Toggle store refresh" }));
  expect((screen.getByRole("button", { name: "Start connection" }) as HTMLButtonElement).disabled).toBe(true); await user.click(screen.getByRole("button", { name: "Toggle store refresh" }));
  vi.mocked(domains.getStoreDomains).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(screen.getByRole("button", { name: "Refresh storefront addresses" })); await screen.findByRole("alert");
  expect((screen.getByRole("button", { name: "Start connection" }) as HTMLButtonElement).disabled).toBe(true); await user.click(screen.getByRole("button", { name: "Try again" })); await waitFor(() => expect((screen.getByRole("button", { name: "Start connection" }) as HTMLButtonElement).disabled).toBe(false));
  vi.mocked(storeEmailApi.get).mockRejectedValueOnce(new HttpError("http", 503)); await user.click(screen.getByRole("button", { name: "Refresh email status" })); await screen.findByRole("alert");
  expect((screen.getByRole("button", { name: "Start connection" }) as HTMLButtonElement).disabled).toBe(true); expect(storeEmailApi.connect).not.toHaveBeenCalled();
});
it.each(["store", "role", "session", "unmount"])("aborts a pending start and ignores its late result on %s departure", async departure => {
  const { user, unmount } = await page(); await review(user); const held = deferred<{ operationId: string }>(); vi.mocked(storeEmailApi.connect).mockReturnValueOnce(held.promise);
  await user.click(screen.getByRole("button", { name: "Start connection" }));
  if (departure === "unmount") unmount(); else await user.click(screen.getByRole("button", { name: departure === "store" ? "Other store" : departure === "role" ? "New role" : "New session" }));
  expect(vi.mocked(storeEmailApi.connect).mock.calls[0][2]?.aborted).toBe(true); const count = vi.mocked(storeEmailApi.get).mock.calls.length;
  await act(async () => held.resolve({ operationId: id })); expect(screen.queryByText(/server accepted a setup process/)).toBeNull(); expect(storeEmailApi.get).toHaveBeenCalledTimes(count);
});
it("discards an old store's late initial email read", async () => {
  const held = deferred<StoreEmail>(); vi.mocked(storeEmailApi.get).mockReturnValueOnce(held.promise); const user = userEvent.setup(); render(app());
  await user.click(screen.getByRole("button", { name: "Other store" })); await screen.findByRole("heading", { name: "No sending domain is configured" });
  await act(async () => held.resolve({ ...fallback, domain })); expect(screen.queryByRole("heading", { name: "mail.shop.cz" })).toBeNull(); expect(vi.mocked(storeEmailApi.get).mock.calls[0][1]?.aborted).toBe(true);
});
it("uses the actual Setup progress detail for an email-operation link", async () => {
  vi.spyOn(setupProgressApi, "detail").mockResolvedValue({ id, kind: "email.domain", subject: domain.domain, state: "failed", stepRunning: false, createdAt: "2026-10-24T07:00:00Z", changedAt: "2026-10-24T07:00:00Z", finishedAt: null, nextCheckAt: null, actions: ["retry"], steps: [] });
  const { user } = await page({ ...fallback, domain }); await user.click(screen.getByRole("link", { name: /Open setup process/ })); await screen.findByRole("heading", { name: "Setup details" });
  expect(screen.getByRole("button", { name: "Retry setup" })).toBeTruthy(); expect(setupProgressApi.detail).toHaveBeenCalledExactlyOnceWith("store-a", id, expect.any(AbortSignal));
});
it.each(["en", "cs"])("routes the %s email page through the real merchant shell and navigation", async language => {
  await i18n.changeLanguage(language); window.history.replaceState(null, "", "/stores/store-a/email");
  vi.spyOn(api, "me").mockResolvedValue({ id: "staff", tenantId: "tenant", role: "Support", email: "staff@example.test" }); vi.spyOn(api, "stores").mockResolvedValue([store]);
  render(<App />); await screen.findByRole("heading", { name: i18n.t("storeEmail:title") });
  expect(screen.getByRole("navigation", { name: i18n.t("navigation:administration") }).querySelector('a[href="/stores/store-a/email"]')).toBeTruthy();
});
