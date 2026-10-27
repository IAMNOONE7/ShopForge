// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import * as account from "../account";
import { consentsApi, type Consent } from "../api/consents";
import { customerEmailApi } from "../api/customerEmail";
import { HttpError } from "../api/http";
import { CustomerProvider } from "../components/CustomerProvider";
import { useCustomer } from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import { StoreContext } from "../storeContext";
import type { Store } from "../store";
import { AccountConsentsPage } from "./AccountConsentsPage";
const store: Store = { id: "store-a", name: "Workshop", currency: "CZK", culture: "cs-CZ", logoUrl: null, providerKeys: [], theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 6 } };
const customer: account.Customer = { email: "ada@example.test", firstName: "Ada", lastName: "Lovelace", phone: null };
const unanswered: Consent = { purpose: "Marketing", isGranted: false, statement: 'Original store wording. <em>Literal</em>\nPříliš žluťoučký.', decidedAt: null };
const granted: Consent = { ...unanswered, isGranted: true, decidedAt: "2026-10-27T10:00:00Z" };
let actual: account.Customer | null, rows: Consent[];
function current() { if (actual) return structuredClone(actual); throw new HttpError("http", 401); }
function Probe() { const session = useCustomer(); return <><p data-testid="session">{session.status}:{session.customer?.email ?? "none"}</p><button onClick={() => { actual = { ...customer, email: "other@example.test" }; session.apply(actual); }}>Switch account</button></>; }
function Shop({ storeId = store.id }: { storeId?: string }) {
  return <StoreContext value={{ ...store, id: storeId }}><CustomerProvider><MemoryRouter initialEntries={["/account/consents"]}><Probe /><Link to="/account">Leave page</Link><Routes>
    <Route path="/account/consents" element={<AccountConsentsPage />} /><Route path="/account" element={<h1>Account destination</h1>} />
  </Routes></MemoryRouter></CustomerProvider></StoreContext>;
}
const yes = () => screen.getByRole("radio", { name: "Allow marketing email from this store" });
const no = () => screen.getByRole("radio", { name: "Do not allow marketing email from this store" });
const save = () => screen.getByRole("button", { name: "Save choice" });
async function choose(user: ReturnType<typeof userEvent.setup>, value = true) { await screen.findByRole("button", { name: "Save choice" }); await user.click(value ? yes() : no()); }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); actual = structuredClone(customer); rows = [structuredClone(unanswered)];
  vi.spyOn(account, "getProfile").mockImplementation(async () => current()); vi.spyOn(customerEmailApi, "profile").mockImplementation(async () => current());
  vi.spyOn(consentsApi, "list").mockImplementation(async () => structuredClone(rows));
  vi.spyOn(consentsApi, "setMarketing").mockImplementation(async isGranted => { const row = { ...unanswered, isGranted, decidedAt: "2026-10-27T11:00:00Z" }; rows = [row]; return structuredClone(row); });
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.sessionStorage.clear(); await i18n.changeLanguage("en"); });
it("shows unanswered as distinct from refusal, keeps original wording inert and does not default or save a grant", async () => {
  render(<Shop />); await screen.findByText("Not answered — consent has not been given");
  expect(screen.getByText("Account: ada@example.test. Store: Workshop.")).toBeTruthy(); expect(yes()).toHaveProperty("checked", false); expect(no()).toHaveProperty("checked", false); expect(save()).toHaveProperty("disabled", true);
  expect(document.querySelector('.consent-statement')?.textContent).toBe(unanswered.statement); expect(document.querySelector('.consent-statement em')).toBeNull(); expect(screen.getByText("No decision recorded")).toBeTruthy();
  expect(consentsApi.setMarketing).not.toHaveBeenCalled(); fireEvent.submit(screen.getByRole("form")); expect(consentsApi.setMarketing).not.toHaveBeenCalled();
});
it.each([true, false])("requires explicit submission to record an initial %s choice and shows only the returned timestamp/wording", async value => {
  const user = userEvent.setup(); render(<Shop />); await choose(user, value); expect(consentsApi.setMarketing).not.toHaveBeenCalled(); expect(screen.getByText("Your selection is not saved yet.")).toBeTruthy();
  await user.click(save()); await screen.findByText("The store confirmed your saved choice."); expect(consentsApi.setMarketing).toHaveBeenCalledExactlyOnceWith(value, expect.any(AbortSignal));
  expect(document.querySelector("time")).toHaveProperty("dateTime", "2026-10-27T11:00:00Z"); expect(screen.getByText(value ? "Allowed" : "Not allowed", { exact: true })).toBeTruthy(); expect(save()).toHaveProperty("disabled", true);
  expect(consentsApi.list).toHaveBeenCalledTimes(2); expect(customerEmailApi.profile).toHaveBeenCalledTimes(5); expect(window.sessionStorage.length).toBe(0);
});
it("starts with the server's existing grant and withdraws only after an explicit change and save", async () => {
  rows = [structuredClone(granted)]; const user = userEvent.setup(); render(<Shop />); await screen.findByText("Allowed", { exact: true }); expect(yes()).toHaveProperty("checked", true); expect(save()).toHaveProperty("disabled", true);
  await choose(user, false); await user.click(save()); await screen.findByText("The store confirmed your saved choice."); expect(consentsApi.setMarketing).toHaveBeenCalledExactlyOnceWith(false, expect.any(AbortSignal));
});
it("keeps a recorded no distinct from an unanswered choice and allows a later grant", async () => {
  rows = [{ ...granted, isGranted: false }]; const user = userEvent.setup(); render(<Shop />); await screen.findByText("Not allowed", { exact: true }); expect(no()).toHaveProperty("checked", true); expect(screen.queryByText("No decision recorded")).toBeNull();
  await choose(user); await user.click(save()); await screen.findByText("The store confirmed your saved choice."); expect(consentsApi.setMarketing).toHaveBeenCalledWith(true, expect.any(AbortSignal));
});
it("discards a draft locally without saving and prevents reload from silently dropping it", async () => {
  const user = userEvent.setup(); render(<Shop />); await choose(user); expect(screen.getByRole("button", { name: "Reload saved choices" })).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Discard change" })); expect(yes()).toHaveProperty("checked", false); expect(no()).toHaveProperty("checked", false); await waitFor(() => expect(document.activeElement).toBe(yes())); expect(consentsApi.setMarketing).not.toHaveBeenCalled();
});
it("gates guests with a fixed consent return path", async () => {
  actual = null; render(<Shop />); expect((await screen.findByRole("link", { name: "Sign in" })).getAttribute("href")).toBe("/account/sign-in?returnTo=%2Faccount%2Fconsents"); expect(consentsApi.list).not.toHaveBeenCalled(); expect(consentsApi.setMarketing).not.toHaveBeenCalled();
});
it("retries a failed initial read without granting anything or showing old facts", async () => {
  vi.mocked(consentsApi.list).mockRejectedValueOnce(new HttpError("network", null)); const user = userEvent.setup(); render(<Shop />); await user.click(await screen.findByRole("button", { name: "Reload saved choices" })); await screen.findByText("Saved choices reloaded. Review the current record."); await screen.findByRole("radio", { name: "Allow marketing email from this store" }); expect(consentsApi.setMarketing).not.toHaveBeenCalled();
});
it.each([400, 403, 404, 429])("retains an explicit unsaved choice after known refusal %s, with no automatic replay", async status => {
  vi.mocked(consentsApi.setMarketing).mockRejectedValue(new HttpError("http", status)); const user = userEvent.setup(); render(<Shop />); await choose(user); await user.click(save()); await screen.findByRole("alert");
  expect(yes()).toHaveProperty("checked", true); expect(save()).toHaveProperty("disabled", false); expect(screen.getByText("Not answered — consent has not been given")).toBeTruthy(); expect(consentsApi.setMarketing).toHaveBeenCalledTimes(1); expect(screen.queryByText("The store confirmed your saved choice.")).toBeNull();
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("http", 409), new HttpError("invalid-response", 200)])("blocks uncertain writes until GET-only review and never infers that the earlier save succeeded: %j", async error => {
  vi.mocked(consentsApi.setMarketing).mockImplementation(async () => { rows = [structuredClone(granted)]; throw error; }); const user = userEvent.setup(); render(<Shop />); await choose(user); await user.click(save()); await screen.findByText(/Your choice may have been saved/);
  expect(screen.queryByRole("radio")).toBeNull(); expect(screen.queryByText("Not answered — consent has not been given")).toBeNull();
  await user.click(screen.getByRole("button", { name: "Review current saved choices" })); await screen.findByText(/This read does not confirm what happened/); expect(yes()).toHaveProperty("checked", true); expect(save()).toHaveProperty("disabled", true); expect(consentsApi.setMarketing).toHaveBeenCalledTimes(1); expect(screen.queryByText("The store confirmed your saved choice.")).toBeNull();
});
it("keeps a failed uncertain recovery read-only until another explicit GET succeeds", async () => {
  vi.mocked(consentsApi.setMarketing).mockRejectedValue(new HttpError("network", null)); const user = userEvent.setup(); render(<Shop />); await choose(user); await user.click(save()); await screen.findByText(/Your choice may have been saved/);
  vi.mocked(consentsApi.list).mockRejectedValueOnce(new HttpError("network", null)); await user.click(screen.getByRole("button", { name: "Review current saved choices" })); await screen.findByText(/Your choice may have been saved/); expect(screen.queryByRole("radio")).toBeNull();
  await user.click(screen.getByRole("button", { name: "Review current saved choices" })); await screen.findByText(/This read does not confirm what happened/); expect(consentsApi.setMarketing).toHaveBeenCalledTimes(1);
});
it("treats a failed preflight as no submitted write and preserves the draft for explicit retry", async () => {
  const user = userEvent.setup(); render(<Shop />); await choose(user); vi.mocked(consentsApi.list).mockRejectedValueOnce(new HttpError("network", null)); await user.click(save()); await screen.findByText(/was not submitted after the failed/); expect(consentsApi.setMarketing).not.toHaveBeenCalled(); expect(yes()).toHaveProperty("checked", true);
  await user.click(save()); await screen.findByText("The store confirmed your saved choice."); expect(consentsApi.setMarketing).toHaveBeenCalledTimes(1);
});
it.each(["decision", "wording", "missing"] as const)("reviews an observed stale %s and drops the old draft without writing", async kind => {
  const user = userEvent.setup(); render(<Shop />); await choose(user); rows = kind === "missing" ? [] : [kind === "wording" ? { ...unanswered, statement: "Revised wording" } : { ...granted, isGranted: false }];
  await user.click(save()); await screen.findByText(/The saved choice or its wording changed/); expect(consentsApi.setMarketing).not.toHaveBeenCalled();
  if (kind === "missing") expect(screen.queryByRole("radio")).toBeNull(); else expect(yes()).toHaveProperty("checked", false);
});
it("rejects an observed different account before saving and remounts with its saved facts", async () => {
  const user = userEvent.setup(); render(<Shop />); await choose(user); actual = { ...customer, email: "other@example.test" }; await user.click(save()); await screen.findByText(/The signed-in account changed/);
  expect(consentsApi.setMarketing).not.toHaveBeenCalled(); await waitFor(() => expect(yes()).toHaveProperty("checked", false)); expect(screen.getByTestId("session").textContent).toBe("authenticated:other@example.test");
});
it("drops data if the post-read session check observes another account", async () => {
  vi.mocked(consentsApi.list).mockImplementationOnce(async () => { actual = { ...customer, email: "other@example.test" }; return [structuredClone(granted)]; }); render(<Shop />);
  await screen.findByText(/The signed-in account changed/); await screen.findByText("Not answered — consent has not been given"); expect(screen.queryByText("Allowed", { exact: true })).toBeNull();
});
it("treats 401 as expired sign-in, not a confirmed preference", async () => {
  vi.mocked(consentsApi.setMarketing).mockRejectedValue(new HttpError("http", 401)); const user = userEvent.setup(); render(<Shop />); await choose(user); await user.click(save()); await screen.findByRole("link", { name: "Sign in" }); expect(screen.queryByText("The store confirmed your saved choice.")).toBeNull();
});
it("does not report a confirmed save when the follow-up session read fails", async () => {
  vi.mocked(consentsApi.setMarketing).mockImplementation(async isGranted => { vi.mocked(customerEmailApi.profile).mockRejectedValueOnce(new HttpError("network", null)); return { ...granted, isGranted }; });
  const user = userEvent.setup(); render(<Shop />); await choose(user); await user.click(save()); await screen.findByText(/Your choice may have been saved/); expect(screen.queryByText("The store confirmed your saved choice.")).toBeNull(); expect(consentsApi.setMarketing).toHaveBeenCalledTimes(1);
});
it("preserves unknown purposes read-only and never invents a missing marketing choice", async () => {
  rows = [{ ...granted, purpose: "FuturePurpose" }]; render(<Shop />); await screen.findByRole("heading", { name: "FuturePurpose" }); expect(document.querySelector('.consent-statement')?.textContent).toBe(unanswered.statement); expect(screen.queryByRole("radio")).toBeNull(); expect(consentsApi.setMarketing).not.toHaveBeenCalled();
});
it("locks double submissions immediately and ignores a late save after leaving the route", async () => {
  const held = deferred<Consent>(); vi.mocked(consentsApi.setMarketing).mockReturnValue(held.promise); const user = userEvent.setup(); render(<Shop />); await choose(user); fireEvent.submit(screen.getByRole("form")); fireEvent.submit(screen.getByRole("form")); await waitFor(() => expect(consentsApi.setMarketing).toHaveBeenCalledTimes(1));
  await user.click(screen.getByRole("link", { name: "Leave page" })); expect(vi.mocked(consentsApi.setMarketing).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(granted)); expect(screen.queryByText("The store confirmed your saved choice.")).toBeNull();
});
it.each(["store", "customer"] as const)("drops pending reads after a %s scope change", async kind => {
  const held = deferred<Consent[]>(); vi.mocked(consentsApi.list).mockReturnValueOnce(held.promise); const user = userEvent.setup(); const view = render(<Shop />); await waitFor(() => expect(consentsApi.list).toHaveBeenCalledTimes(1));
  if (kind === "store") view.rerender(<Shop storeId="store-b" />); else await user.click(screen.getByRole("button", { name: "Switch account" }));
  expect(vi.mocked(consentsApi.list).mock.calls[0][0].aborted).toBe(true); await act(async () => held.resolve([granted])); await screen.findByText("Not answered — consent has not been given"); expect(screen.queryByText("Allowed", { exact: true })).toBeNull();
});
it("retains a draft and the exact store wording when the UI language changes", async () => {
  const user = userEvent.setup(); render(<Shop />); await choose(user); const reads = vi.mocked(consentsApi.list).mock.calls.length; await act(async () => { await i18n.changeLanguage("cs"); });
  expect(screen.getByRole("radio", { name: i18n.t("consents:grant") })).toHaveProperty("checked", true); expect(document.querySelector('.consent-statement')?.textContent).toBe(unanswered.statement); expect(consentsApi.list).toHaveBeenCalledTimes(reads); expect(consentsApi.setMarketing).not.toHaveBeenCalled();
});
it("completes the initial read in StrictMode without duplicating writes or getting stuck behind an abandoned lock", async () => {
  render(<StrictMode><Shop /></StrictMode>); await screen.findByText("Not answered — consent has not been given"); expect(consentsApi.setMarketing).not.toHaveBeenCalled();
});
