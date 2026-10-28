// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api, type CurrentUser } from "../api";
import { HttpError } from "../api/http";
import { staffAccountApi } from "../api/staffAccount";
import { i18n, initializeI18n } from "../i18n";
import english from "../i18n/locales/en";
import { SessionContext } from "../session";
import { StaffAccountPage } from "./StaffAccountPage";

const staff = { id: "staff-a", email: "staff@example.test", role: "Support", tenantId: "company-a" };
const secret = " new password ";
function Shop({ user = staff }: { user?: CurrentUser }) {
  return <SessionContext value={{ user, logout: async () => {}, logoutPending: false, logoutError: null }}><MemoryRouter initialEntries={["/account"]}><Link to="/products">Leave account</Link><Routes><Route path="/account" element={<StaffAccountPage />} /><Route path="/products" element={<h1>Products destination</h1>} /></Routes></MemoryRouter></SessionContext>;
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
type Word = keyof typeof english.resources.staffAccount;
const words = (key: Word) => String(i18n.t(`staffAccount:${key}`));
const input = (key: Word) => screen.getByLabelText(words(key), { exact: true }) as HTMLInputElement;
async function fill() {
  await waitFor(() => expect(input("current").disabled).toBe(false));
  fireEvent.change(input("current"), { target: { value: " old password " } }); fireEvent.change(input("password"), { target: { value: secret } }); fireEvent.change(input("confirmation"), { target: { value: secret } });
}
const button = (key: Word) => screen.getByRole("button", { name: words(key) }) as HTMLButtonElement;
async function submit() { await fill(); fireEvent.submit(screen.getByRole("form")); }
function expectCleared() { for (const key of ["current", "password", "confirmation"] as const) { expect(input(key).value).toBe(""); expect(input(key).type).toBe("password"); } }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  vi.spyOn(staffAccountApi, "session").mockResolvedValue({ ...staff }); vi.spyOn(staffAccountApi, "password").mockResolvedValue(); vi.spyOn(staffAccountApi, "endOtherSessions").mockResolvedValue();
  HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

it("checks the account on entry and never writes without an explicit action", async () => {
  render(<Shop />); await fill(); expect(staffAccountApi.session).toHaveBeenCalledTimes(1); expect(staffAccountApi.password).not.toHaveBeenCalled(); expect(staffAccountApi.endOtherSessions).not.toHaveBeenCalled(); expect(screen.queryByText(/two-factor:/i)).toBeNull();
});
it("links to the separate two-factor flow without creating setup material", async () => {
  render(<Shop />); await fill(); expect(screen.getByRole("link", { name: i18n.t("staffAccount:twoFactor") }).getAttribute("href")).toBe("/account/two-factor"); expect(staffAccountApi.password).not.toHaveBeenCalled();
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "FutureRole"])("opens the actual account route and navigation for %s without store-management permission", async role => {
  const user = { ...staff, role }; vi.spyOn(api, "me").mockResolvedValue(user); vi.spyOn(api, "stores").mockResolvedValue([]); vi.mocked(staffAccountApi.session).mockResolvedValue(user);
  window.history.replaceState(null, "", "/account"); render(<App />);
  await screen.findByRole("form", { name: words("passwordForm") }); await waitFor(() => expect(staffAccountApi.session).toHaveBeenCalledTimes(1));
  expect(screen.getByRole("link", { name: words("title") }).getAttribute("href")).toBe("/account"); expect(staffAccountApi.password).not.toHaveBeenCalled();
});
it("validates current password, length and exact confirmation with field focus", async () => {
  const user = userEvent.setup(); render(<Shop />); await fill(); fireEvent.change(input("current"), { target: { value: "" } }); fireEvent.change(input("password"), { target: { value: "short" } }); fireEvent.change(input("confirmation"), { target: { value: "different" } }); fireEvent.submit(screen.getByRole("form"));
  for (const [issue, field] of [["currentRequired", "current"], ["passwordInvalid", "password"], ["confirmationInvalid", "confirmation"]] as const) { await user.click(button(issue)); expect(document.activeElement).toBe(input(field)); }
  expect(staffAccountApi.password).not.toHaveBeenCalled();
});
it("sends untrimmed secrets once, clears fields before the preflight and verifies the reissued session", async () => {
  const held = deferred<CurrentUser>(); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("showPasswords")); expect(input("current").type).toBe("text");
  vi.mocked(staffAccountApi.session).mockReturnValueOnce(held.promise); fireEvent.submit(screen.getByRole("form")); fireEvent.submit(screen.getByRole("form")); expectCleared(); expect(staffAccountApi.password).not.toHaveBeenCalled();
  await act(async () => held.resolve(staff)); await screen.findByText(words("passwordSaved")); expect(staffAccountApi.password).toHaveBeenCalledExactlyOnceWith(" old password ", secret, expect.any(AbortSignal)); expect(staffAccountApi.session).toHaveBeenCalledTimes(3); expectCleared();
});
it.each([400, 403, 429])("clears all secrets on known password refusal %s and requires fresh input for deliberate retry", async code => {
  vi.mocked(staffAccountApi.password).mockRejectedValueOnce(new HttpError("http", code)); render(<Shop />); await submit();
  await screen.findAllByText(words(code === 400 ? "passwordRefused" : code === 403 ? "wrongCurrent" : "limited")); expectCleared();
  fireEvent.submit(screen.getByRole("form")); expect(staffAccountApi.password).toHaveBeenCalledTimes(1); await submit(); await screen.findByText(words("passwordSaved")); expect(staffAccountApi.password).toHaveBeenCalledTimes(2);
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("http", 409), new HttpError("invalid-response", 200)])("does not replay or infer an unknown password outcome from a successful session read: %j", async error => {
  vi.mocked(staffAccountApi.password).mockRejectedValue(error); const user = userEvent.setup(); render(<Shop />); await submit(); await screen.findByText(words("passwordUncertain")); expectCleared(); expect(input("current").disabled).toBe(true);
  await user.click(button("checkSession")); await screen.findByText(words("sessionChecked")); expect(input("current").disabled).toBe(true); expect(staffAccountApi.password).toHaveBeenCalledTimes(1); expect(screen.queryByText(words("passwordSaved"))).toBeNull(); expect(screen.getByRole("link", { name: words("reload") }).getAttribute("href")).toMatch(/\/account$/);
});
it("reconciles a confirmed save with GET only after a failed follow-up session read", async () => {
  vi.mocked(staffAccountApi.session).mockResolvedValueOnce(staff).mockResolvedValueOnce(staff).mockRejectedValueOnce(new HttpError("network", null)).mockResolvedValue(staff);
  const user = userEvent.setup(); render(<Shop />); await submit(); await screen.findByText(words("confirmedCheckFailed")); expectCleared(); expect(screen.queryByText(words("passwordSaved"))).toBeNull();
  await user.click(button("checkSession")); await screen.findByText(words("passwordSaved")); expect(staffAccountApi.password).toHaveBeenCalledTimes(1); expect(input("current").disabled).toBe(false);
});
it.each(["id", "tenantId", "role", "email"] as const)("refuses to submit for a changed preflight %s", async field => {
  render(<Shop />); await fill(); vi.mocked(staffAccountApi.session).mockResolvedValueOnce({ ...staff, [field]: "different" }); fireEvent.submit(screen.getByRole("form"));
  await screen.findByText(words("changed")); expect(staffAccountApi.password).not.toHaveBeenCalled(); expect(screen.queryByRole("form")).toBeNull();
});
it("stops after a post-write identity change without claiming checked success", async () => {
  vi.mocked(staffAccountApi.session).mockResolvedValueOnce(staff).mockResolvedValueOnce(staff).mockResolvedValueOnce({ ...staff, id: "someone-else" }); render(<Shop />); await submit(); await screen.findByText(words("changed")); expect(screen.queryByText(words("passwordSaved"))).toBeNull(); expect(screen.queryByRole("form")).toBeNull();
});
it.each([401, 403, 429, 503])("offers a read-only recovery or closes after initial read failure %s", async code => {
  vi.mocked(staffAccountApi.session).mockRejectedValueOnce(new HttpError("http", code)); const user = userEvent.setup(); render(<Shop />);
  if (code === 401 || code === 403) { await screen.findByText(words(code === 401 ? "expired" : "changed")); expect(screen.queryByRole("form")).toBeNull(); }
  else { await screen.findByText(words("readFailed")); await user.click(button("checkSession")); await fill(); }
  expect(staffAccountApi.password).not.toHaveBeenCalled(); expect(staffAccountApi.endOtherSessions).not.toHaveBeenCalled();
});
it("preserves a session confirmation after preflight failure and retries only the read", async () => {
  const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); vi.mocked(staffAccountApi.session).mockRejectedValueOnce(new HttpError("network", null)); await user.click(button("confirmSessions"));
  await screen.findByText(words("notSubmitted")); expectCleared(); expect(staffAccountApi.endOtherSessions).not.toHaveBeenCalled();
  await user.click(button("checkSession")); expect(screen.getByRole("region", { name: words("confirmTitle") })).toBeTruthy(); expect(staffAccountApi.endOtherSessions).not.toHaveBeenCalled();
});
it("requires a separate session confirmation, clears password draft, and restores trigger focus on Escape", async () => {
  const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); expectCleared(); const panel = screen.getByRole("region", { name: words("confirmTitle") }); expect(document.activeElement).toBe(panel); expect(staffAccountApi.endOtherSessions).not.toHaveBeenCalled();
  await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(button("endSessions"))); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words("sessionsEnded")); expect(staffAccountApi.endOtherSessions).toHaveBeenCalledTimes(1); expect(staffAccountApi.password).not.toHaveBeenCalled(); expect(staffAccountApi.session).toHaveBeenCalledTimes(3);
});
it.each([400, 429])("retains session confirmation after a known refusal %s", async code => {
  vi.mocked(staffAccountApi.endOtherSessions).mockRejectedValueOnce(new HttpError("http", code)); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words(code === 429 ? "limited" : "refused")); expect(screen.getByRole("region", { name: words("confirmTitle") })).toBeTruthy(); await user.click(button("confirmSessions")); await screen.findByText(words("sessionsEnded")); expect(staffAccountApi.endOtherSessions).toHaveBeenCalledTimes(2);
});
it("does not claim remote session revocation from the current session after a lost response", async () => {
  vi.mocked(staffAccountApi.endOtherSessions).mockRejectedValueOnce(new HttpError("network", null)); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words("sessionsUncertain")); await user.click(button("checkSession")); await screen.findByText(words("sessionChecked")); expect(button("endSessions").disabled).toBe(true); expect(staffAccountApi.endOtherSessions).toHaveBeenCalledTimes(1); expect(screen.queryByText(words("sessionsEnded"))).toBeNull();
});
it.each([401, 403])("closes on session command refusal %s", async code => {
  vi.mocked(staffAccountApi.endOtherSessions).mockRejectedValueOnce(new HttpError("http", code)); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words(code === 401 ? "expired" : "changed")); expect(screen.queryByRole("form")).toBeNull();
});
it("aborts pending preflight on departure and ignores its late result", async () => {
  const held = deferred<CurrentUser>(); const user = userEvent.setup(); render(<Shop />); await fill(); vi.mocked(staffAccountApi.session).mockReturnValueOnce(held.promise); fireEvent.submit(screen.getByRole("form")); const request = vi.mocked(staffAccountApi.session).mock.calls.at(-1)![0]; await user.click(screen.getByRole("link", { name: "Leave account" })); expect(request.aborted).toBe(true); await act(async () => held.resolve(staff)); expect(staffAccountApi.password).not.toHaveBeenCalled();
});
it("aborts a later pending write on departure and never follows it with a session read", async () => {
  const held = deferred<void>(); vi.mocked(staffAccountApi.password).mockReturnValueOnce(held.promise); const user = userEvent.setup(); render(<Shop />); await submit(); await waitFor(() => expect(staffAccountApi.password).toHaveBeenCalledTimes(1)); const request = vi.mocked(staffAccountApi.password).mock.calls[0][2]; await user.click(screen.getByRole("link", { name: "Leave account" })); expect(request.aborted).toBe(true); await act(async () => held.resolve()); expect(staffAccountApi.session).toHaveBeenCalledTimes(2); expect(screen.queryByText(words("passwordSaved"))).toBeNull();
});
it("discards the old form and pending answer when caller or company context changes", async () => {
  const held = deferred<void>(); vi.mocked(staffAccountApi.password).mockReturnValueOnce(held.promise); const page = render(<Shop />); await submit(); await waitFor(() => expect(staffAccountApi.password).toHaveBeenCalledTimes(1)); const next = { ...staff, tenantId: "company-b" }; vi.mocked(staffAccountApi.session).mockResolvedValue(next); page.rerender(<Shop user={next} />); await waitFor(() => expect(input("current").disabled).toBe(false)); expect(vi.mocked(staffAccountApi.password).mock.calls[0][2].aborted).toBe(true); await act(async () => held.resolve()); expectCleared(); expect(screen.queryByText(words("passwordSaved"))).toBeNull();
});
it("retains draft and confirmation during UI language changes without API activity", async () => {
  render(<Shop />); await fill(); await act(async () => { await i18n.changeLanguage("cs"); }); expect(input("password").value).toBe(secret); expect(staffAccountApi.session).toHaveBeenCalledTimes(1); expect(staffAccountApi.password).not.toHaveBeenCalled();
});
it("finishes checked reads under StrictMode", async () => { render(<StrictMode><Shop /></StrictMode>); await fill(); expect(staffAccountApi.password).not.toHaveBeenCalled(); });
