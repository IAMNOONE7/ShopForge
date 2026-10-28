// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { platformApi, type PlatformUser } from "../api/platform";
import { HttpError } from "../api/http";
import { operatorAccountApi } from "../api/operatorAccount";
import { i18n, initializeI18n } from "../i18n";
import english from "../i18n/locales/en";
import { OperatorAccountPage } from "./OperatorAccountPage";

const operator: PlatformUser = { id: "00000000-0000-0000-0000-000000000001", email: "operator@example.test", isTwoFactorEnabled: true };
const secret = " new password ";
function Shop({ user = operator, stale = false }: { user?: PlatformUser; stale?: boolean }) {
  return <MemoryRouter initialEntries={["/platform/account"]}><Link to="/platform/tenants">Leave account</Link><Routes><Route path="/platform/account" element={<OperatorAccountPage user={user} sessionStale={stale} />} /><Route path="/platform/tenants" element={<h1>Companies destination</h1>} /></Routes></MemoryRouter>;
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
type Word = keyof typeof english.resources.operatorAccount;
const words = (key: Word) => String(i18n.t(`operatorAccount:${key}`));
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
  vi.spyOn(operatorAccountApi, "session").mockResolvedValue({ ...operator }); vi.spyOn(operatorAccountApi, "password").mockResolvedValue(); vi.spyOn(operatorAccountApi, "endOtherSessions").mockResolvedValue();
  HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

it("checks the account on entry and never writes without an explicit action", async () => {
  render(<Shop />); await fill(); expect(operatorAccountApi.session).toHaveBeenCalledTimes(1); expect(operatorAccountApi.password).not.toHaveBeenCalled(); expect(operatorAccountApi.endOtherSessions).not.toHaveBeenCalled(); expect(screen.queryByText(/two-factor:/i)).toBeNull();
});
it("opens the actual operator route and its navigation without reading a company session", async () => {
  vi.spyOn(platformApi, "me").mockResolvedValue(operator); const staffRead = vi.spyOn(api, "me"), stores = vi.spyOn(api, "stores"), tenants = vi.spyOn(platformApi, "tenants");
  window.history.replaceState(null, "", "/platform/account"); render(<App />);
  await screen.findByRole("form", { name: words("passwordForm") });
  await waitFor(() => expect(operatorAccountApi.session).toHaveBeenCalledTimes(1));
  expect(screen.getByRole("link", { name: "Operator security" }).getAttribute("aria-current")).toBe("page");
  expect(document.title).toBe("Operator security · ShopForge Platform");
  expect(staffRead).not.toHaveBeenCalled(); expect(stores).not.toHaveBeenCalled(); expect(tenants).not.toHaveBeenCalled();
});
it("validates current password, length and exact confirmation with field focus", async () => {
  const user = userEvent.setup(); render(<Shop />); await fill(); fireEvent.change(input("current"), { target: { value: "" } }); fireEvent.change(input("password"), { target: { value: "short" } }); fireEvent.change(input("confirmation"), { target: { value: "different" } }); fireEvent.submit(screen.getByRole("form"));
  for (const [issue, field] of [["currentRequired", "current"], ["passwordInvalid", "password"], ["confirmationInvalid", "confirmation"]] as const) { await user.click(button(issue)); expect(document.activeElement).toBe(input(field)); }
  expect(operatorAccountApi.password).not.toHaveBeenCalled();
});
it("sends untrimmed secrets once, clears fields before the preflight and verifies the reissued session", async () => {
  const held = deferred<PlatformUser>(); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("showPasswords")); expect(input("current").type).toBe("text");
  vi.mocked(operatorAccountApi.session).mockReturnValueOnce(held.promise); fireEvent.submit(screen.getByRole("form")); fireEvent.submit(screen.getByRole("form")); expectCleared(); expect(operatorAccountApi.password).not.toHaveBeenCalled();
  await act(async () => held.resolve(operator)); await screen.findByText(words("passwordSaved")); expect(operatorAccountApi.password).toHaveBeenCalledExactlyOnceWith(" old password ", secret, expect.any(AbortSignal)); expect(operatorAccountApi.session).toHaveBeenCalledTimes(3); expectCleared();
});
it.each([400, 403, 429])("clears all secrets on known password refusal %s and requires fresh input for deliberate retry", async code => {
  vi.mocked(operatorAccountApi.password).mockRejectedValueOnce(new HttpError("http", code)); render(<Shop />); await submit();
  await screen.findAllByText(words(code === 400 ? "passwordRefused" : code === 403 ? "wrongCurrent" : "limited")); expectCleared();
  fireEvent.submit(screen.getByRole("form")); expect(operatorAccountApi.password).toHaveBeenCalledTimes(1); await submit(); await screen.findByText(words("passwordSaved")); expect(operatorAccountApi.password).toHaveBeenCalledTimes(2);
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("http", 409), new HttpError("invalid-response", 200)])("does not replay or infer an unknown password outcome from a successful session read: %j", async error => {
  vi.mocked(operatorAccountApi.password).mockRejectedValue(error); const user = userEvent.setup(); render(<Shop />); await submit(); await screen.findByText(words("passwordUncertain")); expectCleared(); expect(input("current").disabled).toBe(true);
  await user.click(button("checkSession")); await screen.findByText(words("sessionChecked")); expect(input("current").disabled).toBe(true); expect(operatorAccountApi.password).toHaveBeenCalledTimes(1); expect(screen.queryByText(words("passwordSaved"))).toBeNull(); expect(screen.getByRole("link", { name: words("reload") }).getAttribute("href")).toMatch(/\/account$/);
});
it("reconciles a confirmed save with GET only after a failed follow-up session read", async () => {
  vi.mocked(operatorAccountApi.session).mockResolvedValueOnce(operator).mockResolvedValueOnce(operator).mockRejectedValueOnce(new HttpError("network", null)).mockResolvedValue(operator);
  const user = userEvent.setup(); render(<Shop />); await submit(); await screen.findByText(words("confirmedCheckFailed")); expectCleared(); expect(screen.queryByText(words("passwordSaved"))).toBeNull();
  await user.click(button("checkSession")); await screen.findByText(words("passwordSaved")); expect(operatorAccountApi.password).toHaveBeenCalledTimes(1); expect(input("current").disabled).toBe(false);
});
it.each(["id", "email", "isTwoFactorEnabled"] as const)("refuses to submit for a changed preflight %s", async field => {
  render(<Shop />); await fill(); vi.mocked(operatorAccountApi.session).mockResolvedValueOnce({ ...operator, [field]: field === "isTwoFactorEnabled" ? false : "different" }); fireEvent.submit(screen.getByRole("form"));
  await screen.findByText(words("changed")); expect(operatorAccountApi.password).not.toHaveBeenCalled(); expect(screen.queryByRole("form")).toBeNull();
});
it("stops after a post-write identity change without claiming checked success", async () => {
  vi.mocked(operatorAccountApi.session).mockResolvedValueOnce(operator).mockResolvedValueOnce(operator).mockResolvedValueOnce({ ...operator, id: "someone-else" }); render(<Shop />); await submit(); await screen.findByText(words("changed")); expect(screen.queryByText(words("passwordSaved"))).toBeNull(); expect(screen.queryByRole("form")).toBeNull();
});
it.each([401, 403, 429, 503])("offers a read-only recovery or closes after initial read failure %s", async code => {
  vi.mocked(operatorAccountApi.session).mockRejectedValueOnce(new HttpError("http", code)); const user = userEvent.setup(); render(<Shop />);
  if (code === 401 || code === 403) { await screen.findByText(words(code === 401 ? "expired" : "changed")); expect(screen.queryByRole("form")).toBeNull(); }
  else { await screen.findByText(words("readFailed")); await user.click(button("checkSession")); await fill(); }
  expect(operatorAccountApi.password).not.toHaveBeenCalled(); expect(operatorAccountApi.endOtherSessions).not.toHaveBeenCalled();
});
it("preserves a session confirmation after preflight failure and retries only the read", async () => {
  const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); vi.mocked(operatorAccountApi.session).mockRejectedValueOnce(new HttpError("network", null)); await user.click(button("confirmSessions"));
  await screen.findByText(words("notSubmitted")); expectCleared(); expect(operatorAccountApi.endOtherSessions).not.toHaveBeenCalled();
  await user.click(button("checkSession")); expect(screen.getByRole("region", { name: words("confirmTitle") })).toBeTruthy(); expect(operatorAccountApi.endOtherSessions).not.toHaveBeenCalled();
});
it("requires a separate session confirmation, clears password draft, and restores trigger focus on Escape", async () => {
  const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); expectCleared(); const panel = screen.getByRole("region", { name: words("confirmTitle") }); expect(document.activeElement).toBe(panel); expect(operatorAccountApi.endOtherSessions).not.toHaveBeenCalled();
  await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(button("endSessions"))); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words("sessionsEnded")); expect(operatorAccountApi.endOtherSessions).toHaveBeenCalledTimes(1); expect(operatorAccountApi.password).not.toHaveBeenCalled(); expect(operatorAccountApi.session).toHaveBeenCalledTimes(3);
});
it.each([400, 429])("retains session confirmation after a known refusal %s", async code => {
  vi.mocked(operatorAccountApi.endOtherSessions).mockRejectedValueOnce(new HttpError("http", code)); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words(code === 429 ? "limited" : "refused")); expect(screen.getByRole("region", { name: words("confirmTitle") })).toBeTruthy(); await user.click(button("confirmSessions")); await screen.findByText(words("sessionsEnded")); expect(operatorAccountApi.endOtherSessions).toHaveBeenCalledTimes(2);
});
it("does not claim remote session revocation from the current session after a lost response", async () => {
  vi.mocked(operatorAccountApi.endOtherSessions).mockRejectedValueOnce(new HttpError("network", null)); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words("sessionsUncertain")); await user.click(button("checkSession")); await screen.findByText(words("sessionChecked")); expect(button("endSessions").disabled).toBe(true); expect(operatorAccountApi.endOtherSessions).toHaveBeenCalledTimes(1); expect(screen.queryByText(words("sessionsEnded"))).toBeNull();
});
it.each([401, 403])("closes on session command refusal %s", async code => {
  vi.mocked(operatorAccountApi.endOtherSessions).mockRejectedValueOnce(new HttpError("http", code)); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words(code === 401 ? "expired" : "changed")); expect(screen.queryByRole("form")).toBeNull();
});
it("aborts pending preflight on departure and ignores its late result", async () => {
  const held = deferred<PlatformUser>(); const user = userEvent.setup(); render(<Shop />); await fill(); vi.mocked(operatorAccountApi.session).mockReturnValueOnce(held.promise); fireEvent.submit(screen.getByRole("form")); const request = vi.mocked(operatorAccountApi.session).mock.calls.at(-1)![0]; await user.click(screen.getByRole("link", { name: "Leave account" })); expect(request.aborted).toBe(true); await act(async () => held.resolve(operator)); expect(operatorAccountApi.password).not.toHaveBeenCalled();
});
it("aborts a later pending write on departure and never follows it with a session read", async () => {
  const held = deferred<void>(); vi.mocked(operatorAccountApi.password).mockReturnValueOnce(held.promise); const user = userEvent.setup(); render(<Shop />); await submit(); await waitFor(() => expect(operatorAccountApi.password).toHaveBeenCalledTimes(1)); const request = vi.mocked(operatorAccountApi.password).mock.calls[0][2]; await user.click(screen.getByRole("link", { name: "Leave account" })); expect(request.aborted).toBe(true); await act(async () => held.resolve()); expect(operatorAccountApi.session).toHaveBeenCalledTimes(2); expect(screen.queryByText(words("passwordSaved"))).toBeNull();
});
it("discards the old form and pending answer when operator identity or security state changes", async () => {
  const held = deferred<void>(); vi.mocked(operatorAccountApi.password).mockReturnValueOnce(held.promise); const page = render(<Shop />); await submit(); await waitFor(() => expect(operatorAccountApi.password).toHaveBeenCalledTimes(1)); const next = { ...operator, isTwoFactorEnabled: false }; vi.mocked(operatorAccountApi.session).mockResolvedValue(next); page.rerender(<Shop user={next} />); await waitFor(() => expect(input("current").disabled).toBe(false)); expect(vi.mocked(operatorAccountApi.password).mock.calls[0][2].aborted).toBe(true); await act(async () => held.resolve()); expectCleared(); expect(screen.queryByText(words("passwordSaved"))).toBeNull();
});
it("retains draft and confirmation during UI language changes without API activity", async () => {
  render(<Shop />); await fill(); await act(async () => { await i18n.changeLanguage("cs"); }); expect(input("password").value).toBe(secret); expect(operatorAccountApi.session).toHaveBeenCalledTimes(1); expect(operatorAccountApi.password).not.toHaveBeenCalled();
});
it("finishes checked reads under StrictMode", async () => { render(<StrictMode><Shop /></StrictMode>); await fill(); expect(operatorAccountApi.password).not.toHaveBeenCalled(); });

it.each([9, 129])("refuses a new password of %s characters before reading or writing", async length => {
  render(<Shop />); await fill(); fireEvent.change(input("password"), { target: { value: "x".repeat(length) } }); fireEvent.change(input("confirmation"), { target: { value: "x".repeat(length) } }); fireEvent.submit(screen.getByRole("form"));
  await screen.findByRole("button", { name: words("passwordInvalid") }); expect(operatorAccountApi.session).toHaveBeenCalledTimes(1); expect(operatorAccountApi.password).not.toHaveBeenCalled();
});
it.each([10, 128])("accepts the actual %s-character password boundary", async length => {
  render(<Shop />); await fill(); for (const field of ["password", "confirmation"] as const) fireEvent.change(input(field), { target: { value: "x".repeat(length) } }); fireEvent.submit(screen.getByRole("form"));
  await screen.findByText(words("passwordSaved")); expect(vi.mocked(operatorAccountApi.password).mock.calls[0][1]).toHaveLength(length);
});
it.each([401, 403])("closes after a denied preflight %s without a command", async code => {
  render(<Shop />); await fill(); vi.mocked(operatorAccountApi.session).mockRejectedValueOnce(new HttpError("http", code)); fireEvent.submit(screen.getByRole("form"));
  await screen.findByText(words(code === 401 ? "expired" : "changed")); expect(screen.queryByRole("form")).toBeNull(); expect(operatorAccountApi.password).not.toHaveBeenCalled();
});
it.each([new HttpError("http", 503), new HttpError("http", 409), new HttpError("invalid-response", 200)])("blocks both commands after an unknown session-change answer: %j", async error => {
  vi.mocked(operatorAccountApi.endOtherSessions).mockRejectedValueOnce(error); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); await user.click(button("confirmSessions")); await screen.findByText(words("sessionsUncertain"));
  await user.click(button("checkSession")); await screen.findByText(words("sessionChecked")); expect(input("current").disabled).toBe(true); expect(button("endSessions").disabled).toBe(true); expect(operatorAccountApi.endOtherSessions).toHaveBeenCalledTimes(1); expect(operatorAccountApi.password).not.toHaveBeenCalled();
});
it("pauses the page during parent logout, aborts pending work and discards its late result", async () => {
  const held = deferred<void>(); vi.mocked(operatorAccountApi.password).mockReturnValueOnce(held.promise); const view = render(<Shop />); await submit(); await waitFor(() => expect(operatorAccountApi.password).toHaveBeenCalledTimes(1));
  view.rerender(<Shop stale />); expect(vi.mocked(operatorAccountApi.password).mock.calls[0][2].aborted).toBe(true); expect(screen.queryByRole("form")).toBeNull(); await act(async () => held.resolve()); expect(operatorAccountApi.session).toHaveBeenCalledTimes(2);
  view.rerender(<Shop />); await fill(); expect(screen.queryByText(words("passwordSaved"))).toBeNull();
});
it("does not read or render credential controls while the parent session is unresolved", async () => {
  render(<Shop stale />); expect(screen.queryByRole("form")).toBeNull(); expect(operatorAccountApi.session).not.toHaveBeenCalled(); expect(operatorAccountApi.password).not.toHaveBeenCalled();
});
it("locks duplicate confirmed session commands synchronously", async () => {
  const held = deferred<void>(); vi.mocked(operatorAccountApi.endOtherSessions).mockReturnValueOnce(held.promise); const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions"));
  const confirm = button("confirmSessions"); fireEvent.click(confirm); fireEvent.click(confirm); await waitFor(() => expect(operatorAccountApi.endOtherSessions).toHaveBeenCalledTimes(1)); await act(async () => held.resolve()); await screen.findByText(words("sessionsEnded"));
});
it("keeps session confirmation on a language change without calling either command", async () => {
  const user = userEvent.setup(); render(<Shop />); await fill(); await user.click(button("endSessions")); const reads = vi.mocked(operatorAccountApi.session).mock.calls.length; await act(async () => { await i18n.changeLanguage("cs"); });
  expect(screen.getByRole("region", { name: words("confirmTitle") })).toBeTruthy(); expect(operatorAccountApi.session).toHaveBeenCalledTimes(reads); expect(operatorAccountApi.endOtherSessions).not.toHaveBeenCalled();
});
it("uses fixed operator-only recovery and reload destinations after an unknown password answer", async () => {
  vi.mocked(operatorAccountApi.password).mockRejectedValueOnce(new HttpError("network", null)); render(<Shop />); await submit(); await screen.findByText(words("passwordUncertain"));
  expect(screen.getByRole("link", { name: words("recoverPassword") }).getAttribute("href")).toBe("/platform/forgot-password"); expect(screen.getByRole("link", { name: words("reload") }).getAttribute("href")).toBe("/platform/account");
});
