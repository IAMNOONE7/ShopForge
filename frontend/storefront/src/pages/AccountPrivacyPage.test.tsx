// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import * as account from "../account";
import * as cart from "../cart";
import { customerEmailApi } from "../api/customerEmail";
import { privacyApi, type PrivacyDownload } from "../api/privacy";
import * as http from "../api/http";
import { CustomerProvider } from "../components/CustomerProvider";
import { CartProvider } from "../components/CartProvider";
import { useCustomer } from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import { StoreContext } from "../storeContext";
import type { Store } from "../store";
import { AccountPrivacyPage } from "./AccountPrivacyPage";
const store: Store = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", logoUrl: null, providerKeys: [], theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 } };
const original: account.Customer = { email: "ada@example.test", firstName: "Ada", lastName: "Lovelace", phone: null };
const download: PrivacyDownload = { blob: new Blob(['{"fixture":true}'], { type: "application/json" }), filename: "shopforge-data.json", exportedAt: "2026-10-27T10:00:00Z", sections: [{ name: "account", entries: 1 }, { name: "wishlist", entries: 0 }, { name: "future-module", entries: 2 }] };
let actual: account.Customer | null;
function current() { if (actual) return structuredClone(actual); throw new http.HttpError("http", 401); }
function Probe() { const session = useCustomer(); return <><p data-testid="session">{session.status}:{session.customer?.email ?? "none"}</p><button onClick={() => session.apply({ ...original, email: "other@example.test" })}>Switch identity</button></>; }
function Shop({ storeId = store.id }: { storeId?: string }) {
  return <StoreContext value={{ ...store, id: storeId }}><CustomerProvider><CartProvider><MemoryRouter initialEntries={["/account/privacy"]}><Probe /><Link to="/account">Leave page</Link><Routes>
    <Route path="/account/privacy" element={<AccountPrivacyPage />} /><Route path="/account" element={<h1>Account destination</h1>} />
  </Routes></MemoryRouter></CartProvider></CustomerProvider></StoreContext>;
}
const confirmation = () => screen.getByRole("form", { name: "Confirm account deletion" });
async function open(user: ReturnType<typeof userEvent.setup>) { await user.click(await screen.findByRole("button", { name: "Start account deletion" })); }
async function fill(user: ReturnType<typeof userEvent.setup>) { await open(user); await user.type(screen.getByLabelText("Current password"), " raw password "); await user.click(screen.getByRole("checkbox")); }
async function submit(user: ReturnType<typeof userEvent.setup>) { await user.click(screen.getByRole("button", { name: "Permanently delete this account" })); }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); actual = structuredClone(original);
  vi.spyOn(account, "getProfile").mockImplementation(async () => current()); vi.spyOn(customerEmailApi, "profile").mockImplementation(async () => current());
  vi.spyOn(cart, "getCart").mockResolvedValue({ items: [], count: 0, itemsTotal: 0, vatTotal: 0, changed: false, discount: null, discountProblem: null });
  vi.spyOn(privacyApi, "export").mockResolvedValue(download); vi.spyOn(http, "saveDownload").mockImplementation(() => {});
  vi.spyOn(privacyApi, "erase").mockImplementation(async () => { actual = null; });
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.sessionStorage.clear(); await i18n.changeLanguage("en"); });
it("names the account and store, explains retained records, and makes no private request on entry", async () => {
  render(<Shop />); await screen.findByRole("button", { name: "Download JSON copy" });
  expect(screen.getByText("Account: ada@example.test. Store: Workshop.")).toBeTruthy(); expect(screen.getByText(/does not cancel purchases/)).toBeTruthy();
  expect(privacyApi.export).not.toHaveBeenCalled(); expect(privacyApi.erase).not.toHaveBeenCalled(); expect(customerEmailApi.profile).not.toHaveBeenCalled();
});
it("gates guests with a fixed private return destination", async () => {
  actual = null; render(<Shop />); expect((await screen.findByRole("link", { name: "Sign in" })).getAttribute("href")).toBe("/account/sign-in?returnTo=%2Faccount%2Fprivacy");
  expect(screen.queryByRole("button", { name: "Download JSON copy" })).toBeNull(); expect(privacyApi.erase).not.toHaveBeenCalled();
});
it("keeps a failed initial session read retryable without exposing private actions", async () => {
  vi.mocked(account.getProfile).mockRejectedValueOnce(new http.HttpError("network", null)); const user = userEvent.setup(); render(<Shop />); await user.click(await screen.findByRole("button", { name: "Try again" }));
  await screen.findByRole("button", { name: "Download JSON copy" }); expect(privacyApi.export).not.toHaveBeenCalled();
});
it("checks the same customer before and after the export, releases original bytes, and shows only a safe section summary", async () => {
  const user = userEvent.setup(); render(<Shop />); await user.click(await screen.findByRole("button", { name: "Download JSON copy" })); await screen.findByText(/is ready for your browser/);
  expect(customerEmailApi.profile).toHaveBeenCalledTimes(2); expect(privacyApi.export).toHaveBeenCalledWith(original.email, expect.any(AbortSignal)); expect(http.saveDownload).toHaveBeenCalledExactlyOnceWith(download);
  await user.click(screen.getByText("Sections in this file")); expect(screen.getByText("future-module")).toBeTruthy(); expect(screen.getByText("Wishlist")).toBeTruthy(); expect(screen.getByText("0")).toBeTruthy();
  expect(screen.queryByText('{"fixture":true}')).toBeNull(); expect(window.sessionStorage.length).toBe(0);
});
it.each(["before", "after"] as const)("discards an observed account change %s export and does not hand off its data", async when => {
  vi.mocked(customerEmailApi.profile).mockImplementation(async () => current());
  if (when === "before") actual = { ...original, email: "other@example.test" };
  else vi.mocked(privacyApi.export).mockImplementation(async () => { actual = { ...original, email: "other@example.test" }; return download; });
  const user = userEvent.setup(); render(<Shop />);
  if (when === "before") { await screen.findByRole("button", { name: "Download JSON copy" }); actual = { ...original, email: "changed@example.test" }; }
  await user.click(await screen.findByRole("button", { name: "Download JSON copy" })); await screen.findByText(/A different account is signed in now/);
  expect(http.saveDownload).not.toHaveBeenCalled(); expect(privacyApi.export).toHaveBeenCalledTimes(when === "before" ? 0 : 1);
});
it("keeps an export refusal explicitly retryable without triggering automatic downloads", async () => {
  vi.mocked(privacyApi.export).mockRejectedValueOnce(new http.HttpError("http", 429)); const user = userEvent.setup(); render(<Shop />);
  await user.click(await screen.findByRole("button", { name: "Download JSON copy" })); await screen.findByRole("alert"); expect(privacyApi.export).toHaveBeenCalledTimes(1); expect(http.saveDownload).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Download JSON copy" })); await screen.findByText(/is ready for your browser/); expect(privacyApi.export).toHaveBeenCalledTimes(2);
});
it("stops a pending export and ignores a late answer without allowing concurrent actions", async () => {
  const held = deferred<PrivacyDownload>(); vi.mocked(privacyApi.export).mockReturnValue(held.promise); const user = userEvent.setup(); render(<Shop />);
  await user.click(await screen.findByRole("button", { name: "Download JSON copy" })); await waitFor(() => expect(privacyApi.export).toHaveBeenCalledTimes(1));
  expect(screen.getByRole("button", { name: "Start account deletion" })).toHaveProperty("disabled", true); await user.click(screen.getByRole("button", { name: "Stop download" }));
  expect(vi.mocked(privacyApi.export).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(download)); await screen.findByText(/stopped on this page/); expect(http.saveDownload).not.toHaveBeenCalled();
});
it("requires password and explicit acknowledgment, focuses each issue, and supports Escape without a mutation", async () => {
  const user = userEvent.setup(); render(<Shop />); await open(user); expect(document.activeElement).toBe(screen.getByLabelText("Current password")); await submit(user);
  await user.click(await screen.findByRole("button", { name: "Enter your current password." })); expect(document.activeElement).toBe(screen.getByLabelText("Current password"));
  await user.click(screen.getByRole("button", { name: "Confirm that you understand this action is permanent." })); expect(document.activeElement).toBe(screen.getByRole("checkbox"));
  await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("button", { name: "Start account deletion" }))); expect(privacyApi.erase).not.toHaveBeenCalled();
  await open(user); expect(screen.getByLabelText("Current password")).toHaveProperty("value", ""); expect(screen.getByRole("checkbox")).toHaveProperty("checked", false);
});
it.each([400, 403, 404, 429])("retains confirmation but clears and re-hides the password after a known %s refusal", async status => {
  vi.mocked(privacyApi.erase).mockRejectedValue(new http.HttpError("http", status)); const user = userEvent.setup(); render(<Shop />); await fill(user); await user.click(screen.getByRole("button", { name: "Show password" })); await submit(user); await screen.findByRole("alert");
  expect(screen.getByLabelText("Current password")).toHaveProperty("value", ""); expect(screen.getByLabelText("Current password")).toHaveProperty("type", "password"); expect(screen.getByRole("checkbox")).toHaveProperty("checked", true);
  expect(screen.getByRole("button", { name: "Permanently delete this account" })).toHaveProperty("disabled", false); expect(privacyApi.erase).toHaveBeenCalledExactlyOnceWith(" raw password ", expect.any(AbortSignal));
  expect(screen.getByTestId("session").textContent).toBe("authenticated:ada@example.test");
});
it("clears private context and auth return only after confirmed deletion, refreshes the real cart and does not replay on navigation", async () => {
  window.sessionStorage.setItem("shopforge.auth.returnTo", "/checkout"); window.sessionStorage.setItem("unrelated", "keep"); const user = userEvent.setup(); render(<Shop />); await fill(user); await submit(user);
  await screen.findByRole("heading", { name: "Account deleted" }); expect(screen.getByTestId("session").textContent).toBe("guest:none"); expect(screen.queryByText(/ada@example.test/)).toBeNull();
  expect(window.sessionStorage.getItem("shopforge.auth.returnTo")).toBeNull(); expect(window.sessionStorage.getItem("unrelated")).toBe("keep"); await waitFor(() => expect(cart.getCart).toHaveBeenCalledTimes(2));
  await user.click(screen.getByRole("link", { name: "Leave page" })); await screen.findByRole("heading", { name: "Account destination" }); expect(privacyApi.erase).toHaveBeenCalledTimes(1);
});
it.each([new http.HttpError("network", null), new http.HttpError("http", 500), new http.HttpError("http", 409), new http.HttpError("invalid-response", 200)])("hides private data after an uncertain deletion and permits only read-only session review: %j", async failure => {
  vi.mocked(privacyApi.erase).mockRejectedValue(failure); const user = userEvent.setup(); render(<Shop />); await fill(user); await submit(user);
  await screen.findByRole("heading", { name: "Deletion outcome not confirmed" }); expect(screen.getByTestId("session").textContent).toBe("guest:none"); expect(screen.queryByText(/ada@example.test/)).toBeNull();
  expect(screen.queryByRole("button", { name: "Permanently delete this account" })).toBeNull(); await user.click(screen.getByRole("button", { name: "Check current sign-in" }));
  await screen.findByText("Currently signed in as ada@example.test."); await screen.findByText(/does not prove whether the earlier deletion succeeded/); expect(privacyApi.erase).toHaveBeenCalledTimes(1); expect(customerEmailApi.profile).toHaveBeenCalledTimes(2);
});
it("does not treat an observed guest during uncertain recovery as proof of deletion", async () => {
  vi.mocked(privacyApi.erase).mockImplementation(async () => { actual = null; throw new http.HttpError("network", null); }); const user = userEvent.setup(); render(<Shop />); await fill(user); await submit(user); await user.click(await screen.findByRole("button", { name: "Check current sign-in" }));
  await screen.findByText("This browser is currently signed out."); expect(screen.queryByRole("heading", { name: "Account deleted" })).toBeNull(); expect(privacyApi.erase).toHaveBeenCalledTimes(1);
});
it("keeps a failed preflight distinct from a submitted deletion and requires a freshly typed secret", async () => {
  vi.mocked(customerEmailApi.profile).mockRejectedValueOnce(new http.HttpError("network", null)); const user = userEvent.setup(); render(<Shop />); await fill(user); await submit(user); await screen.findByText(/No deletion was submitted/);
  expect(privacyApi.erase).not.toHaveBeenCalled(); expect(screen.getByLabelText("Current password")).toHaveProperty("value", ""); await user.type(screen.getByLabelText("Current password"), "new password"); await submit(user); await screen.findByRole("heading", { name: "Account deleted" });
});
it("drops an expired preflight without a mutation or success claim", async () => {
  vi.mocked(customerEmailApi.profile).mockRejectedValueOnce(new http.HttpError("http", 401)); const user = userEvent.setup(); render(<Shop />); await fill(user); await submit(user); await screen.findByRole("link", { name: "Sign in" }); expect(privacyApi.erase).not.toHaveBeenCalled(); expect(screen.queryByRole("heading", { name: "Account deleted" })).toBeNull();
});
it("drops the deletion draft when a fresh check observes another customer", async () => {
  const user = userEvent.setup(); render(<Shop />); await fill(user); actual = { ...original, email: "other@example.test" }; await submit(user); await screen.findByText(/A different account is signed in now/);
  expect(privacyApi.erase).not.toHaveBeenCalled(); expect(screen.queryByLabelText("Current password")).toBeNull(); expect(screen.getByText("Account: other@example.test. Store: Workshop.")).toBeTruthy();
});
it("locks duplicate submissions synchronously and ignores a deletion answer after departure", async () => {
  const held = deferred<void>(); vi.mocked(privacyApi.erase).mockReturnValue(held.promise); const user = userEvent.setup(); render(<Shop />); await fill(user);
  fireEvent.submit(confirmation()); fireEvent.submit(confirmation()); await waitFor(() => expect(privacyApi.erase).toHaveBeenCalledTimes(1));
  await user.click(screen.getByRole("link", { name: "Leave page" })); await screen.findByRole("heading", { name: "Account destination" }); expect(vi.mocked(privacyApi.erase).mock.calls[0][1].aborted).toBe(true);
  await act(async () => held.resolve()); expect(screen.queryByRole("heading", { name: "Account deleted" })).toBeNull(); expect(screen.getByTestId("session").textContent).toBe("authenticated:ada@example.test");
});
it.each(["customer", "store"] as const)("ignores pending export work after a %s scope change", async scope => {
  const held = deferred<PrivacyDownload>(); vi.mocked(privacyApi.export).mockReturnValue(held.promise); const user = userEvent.setup(); const view = render(<Shop />); await user.click(await screen.findByRole("button", { name: "Download JSON copy" })); await waitFor(() => expect(privacyApi.export).toHaveBeenCalledTimes(1));
  if (scope === "customer") await user.click(screen.getByRole("button", { name: "Switch identity" })); else view.rerender(<Shop storeId="store-b" />);
  expect(vi.mocked(privacyApi.export).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(download)); expect(http.saveDownload).not.toHaveBeenCalled();
});
it("localizes the confirmation and retained-record guidance in Czech without changing request bodies", async () => {
  await i18n.changeLanguage("cs"); const user = userEvent.setup(); render(<Shop />); await user.click(await screen.findByRole("button", { name: i18n.t("privacy:beginDelete") }));
  const form = screen.getByRole("form", { name: i18n.t("privacy:deleteConfirmation") }); expect(within(form).getByLabelText(i18n.t("privacy:currentPassword"))).toBeTruthy(); expect(privacyApi.erase).not.toHaveBeenCalled();
});
