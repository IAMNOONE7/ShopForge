// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api, type CurrentUser } from "../api";
import { HttpError } from "../api/http";
import { staffTwoFactorApi, type StaffSecuritySession } from "../api/staffTwoFactor";
import { i18n, initializeI18n } from "../i18n";
import english from "../i18n/locales/en";
import { SessionContext } from "../session";
import { recoveryCodes, securityStaff, twoFactorSetup } from "../test/staffTwoFactorFixtures";
import { StaffTwoFactorPage } from "./StaffTwoFactorPage";

type Word = keyof typeof english.resources.staffTwoFactor;
const words = (key: Word) => i18n.t(`staffTwoFactor:${key}`);
const button = (key: Word) => screen.getByRole("button", { name: words(key) }) as HTMLButtonElement;
const input = (key: Word) => screen.getByLabelText(words(key), { exact: true }) as HTMLInputElement;
const form = () => screen.getByRole("form");
let actual: StaffSecuritySession;
function Shop({ user = securityStaff }: { user?: CurrentUser }) {
  return <SessionContext value={{ user, logout: async () => {}, logoutPending: false, logoutError: null }}><MemoryRouter initialEntries={["/account/two-factor"]}><Link to="/products">Leave security</Link><Routes><Route path="/account/two-factor" element={<StaffTwoFactorPage />} /><Route path="/account" element={<h1>Account destination</h1>} /><Route path="/products" element={<h1>Products destination</h1>} /></Routes></MemoryRouter></SessionContext>;
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
async function setup() { const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("start") })); await screen.findByRole("heading", { name: words("setupTitle") }); return user; }
async function confirm() { const user = userEvent.setup(); await user.click(input("added")); fireEvent.change(input("code"), { target: { value: "012345" } }); fireEvent.submit(form()); }
async function offForm() { const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("turnOff") })); await user.click(input("offAcknowledgment")); fireEvent.change(input("password"), { target: { value: " raw password " } }); return user; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); actual = { ...securityStaff };
  vi.spyOn(staffTwoFactorApi, "session").mockImplementation(async () => ({ ...actual })); vi.spyOn(staffTwoFactorApi, "begin").mockResolvedValue({ ...twoFactorSetup });
  vi.spyOn(staffTwoFactorApi, "confirm").mockImplementation(async () => { actual.isTwoFactorEnabled = true; return [...recoveryCodes]; });
  vi.spyOn(staffTwoFactorApi, "turnOff").mockImplementation(async () => { actual.isTwoFactorEnabled = false; }); HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

it("reads authoritative settings on entry without starting or changing setup", async () => {
  render(<Shop />); await screen.findByRole("heading", { name: words("off") }); expect(staffTwoFactorApi.session).toHaveBeenCalledTimes(1); expect(staffTwoFactorApi.begin).not.toHaveBeenCalled(); expect(staffTwoFactorApi.confirm).not.toHaveBeenCalled(); expect(staffTwoFactorApi.turnOff).not.toHaveBeenCalled();
});
it.each(["Owner", "Admin", "CatalogManager", "OrderManager", "Warehouse", "Support", "FutureRole"])("opens the actual staff two-factor route for %s", async role => {
  actual.role = role; vi.spyOn(api, "me").mockResolvedValue(actual); vi.spyOn(api, "stores").mockResolvedValue([]); window.history.replaceState(null, "", "/account/two-factor"); render(<App />);
  await screen.findByRole("heading", { name: words("off") }); await waitFor(() => expect(staffTwoFactorApi.session).toHaveBeenCalledTimes(1)); expect(staffTwoFactorApi.begin).not.toHaveBeenCalled();
});
it("reveals and selects a manual key explicitly without exposing its URI or opening another origin", async () => {
  render(<Shop />); const user = await setup(); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(document.body.textContent).not.toContain(twoFactorSetup.secret); expect(document.body.innerHTML).not.toContain("otpauth:");
  await user.click(button("showKey")); expect(input("key").value).toBe(twoFactorSetup.secret); expect(input("key").readOnly).toBe(true); await user.click(button("selectKey")); expect(document.activeElement).toBe(input("key")); expect(input("key").selectionEnd).toBe(32); await user.click(button("hideKey")); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(staffTwoFactorApi.session).toHaveBeenCalledTimes(3);
});
it("requires authenticator enrollment and six ASCII digits, preserving leading zeroes", async () => {
  render(<Shop />); const user = await setup(); fireEvent.submit(form()); await screen.findByText(words("addedRequired")); await user.click(button("goToField")); expect(document.activeElement).toBe(input("added")); await user.click(input("added"));
  for (const value of ["", "12345", "１２３４５６", "ABCDEF", " 12345"]) { fireEvent.change(input("code"), { target: { value } }); fireEvent.submit(form()); expect(await screen.findAllByText(words("codeInvalid"))).toBeTruthy(); }
  expect(staffTwoFactorApi.confirm).not.toHaveBeenCalled(); fireEvent.change(input("code"), { target: { value: "012345" } }); fireEvent.submit(form()); await screen.findByRole("heading", { name: words("recoveryTitle") }); expect(staffTwoFactorApi.confirm).toHaveBeenCalledExactlyOnceWith("012345", expect.any(AbortSignal));
});
it("clears the code and hides the key before preflight, suppressing duplicate confirmation", async () => {
  render(<Shop />); const user = await setup(); await user.click(button("showKey")); const held = deferred<StaffSecuritySession>(); vi.mocked(staffTwoFactorApi.session).mockReturnValueOnce(held.promise); await confirm(); fireEvent.submit(form()); expect(input("code").value).toBe(""); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(staffTwoFactorApi.confirm).not.toHaveBeenCalled(); await act(async () => held.resolve(securityStaff)); await screen.findByRole("heading", { name: words("recoveryTitle") }); expect(staffTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
it("keeps recovery codes hidden until explicit reveal and requires saving before clearing them", async () => {
  render(<Shop />); const user = await setup(); await confirm(); await screen.findByRole("heading", { name: words("recoveryTitle") }); expect(screen.queryByLabelText(words("codes"))).toBeNull(); expect(document.body.innerHTML).not.toContain(twoFactorSetup.secret); expect(button("finish").disabled).toBe(true); expect(input("saved").disabled).toBe(true);
  await user.click(button("showCodes")); const codes = screen.getByLabelText(words("codes")) as HTMLTextAreaElement; expect(codes.value).toBe(recoveryCodes.join("\n")); await user.click(button("selectCodes")); expect(document.activeElement).toBe(codes); expect(codes.selectionEnd).toBe(codes.value.length); await user.click(input("saved")); await user.click(button("finish")); await screen.findByText(words("finished")); expect(screen.queryByLabelText(words("codes"))).toBeNull(); for (const code of recoveryCodes) expect(document.body.innerHTML).not.toContain(code); expect(staffTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
it("discards local setup without inventing a cancel endpoint or claiming backend rollback", async () => {
  render(<Shop />); const user = await setup(); await user.click(button("showKey")); await user.click(button("discard")); await screen.findByText(words("discarded")); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(staffTwoFactorApi.begin).toHaveBeenCalledTimes(1); expect(staffTwoFactorApi.turnOff).not.toHaveBeenCalled(); await user.click(button("start")); expect(staffTwoFactorApi.begin).toHaveBeenCalledTimes(2);
});
it.each([400, 403, 429])("requires a fresh code after known confirmation refusal %s, keeping the original setup", async status => {
  vi.mocked(staffTwoFactorApi.confirm).mockRejectedValueOnce(new HttpError("http", status)); render(<Shop />); await setup(); await confirm(); await screen.findAllByText(words(status === 403 ? "wrongCode" : status === 429 ? "limited" : "refused")); expect(input("code").value).toBe(""); expect(input("added").checked).toBe(true); expect(screen.queryByLabelText(words("key"))).toBeNull(); fireEvent.submit(form()); expect(staffTwoFactorApi.confirm).toHaveBeenCalledTimes(1); fireEvent.change(input("code"), { target: { value: "012345" } }); fireEvent.submit(form()); await screen.findByRole("heading", { name: words("recoveryTitle") }); expect(staffTwoFactorApi.begin).toHaveBeenCalledTimes(1);
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 200)])("never repeats unknown confirmation or recovers codes by reading an enabled flag: %j", async error => {
  vi.mocked(staffTwoFactorApi.confirm).mockImplementationOnce(async () => { actual.isTwoFactorEnabled = true; throw error; }); render(<Shop />); const user = await setup(); await confirm(); await screen.findByText(words("confirmUncertain")); expect(screen.queryByRole("form")).toBeNull(); expect(screen.queryByLabelText(words("key"))).toBeNull(); await user.click(button("checkStatus")); await screen.findByText(words("observedOn")); expect(screen.queryByLabelText(words("codes"))).toBeNull(); expect(screen.queryByRole("button", { name: words("confirm") })).toBeNull(); expect(staffTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
it("does not repeat a lost begin even when the current flag stays off", async () => {
  vi.mocked(staffTwoFactorApi.begin).mockRejectedValueOnce(new HttpError("network", null)); render(<Shop />); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("start") })); await screen.findByText(words("beginUncertain")); await user.click(button("checkStatus")); await screen.findByText(words("observedOff")); expect(screen.queryByRole("button", { name: words("start") })).toBeNull(); expect(staffTwoFactorApi.begin).toHaveBeenCalledTimes(1);
});
it.each(["begin", "confirm"] as const)("keeps %s response material hidden during failed follow-up reads and recovers with GET only", async action => {
  if (action === "begin") vi.mocked(staffTwoFactorApi.begin).mockImplementationOnce(async () => { vi.mocked(staffTwoFactorApi.session).mockRejectedValueOnce(new HttpError("network", null)); return twoFactorSetup; });
  else vi.mocked(staffTwoFactorApi.confirm).mockImplementationOnce(async () => { actual.isTwoFactorEnabled = true; vi.mocked(staffTwoFactorApi.session).mockRejectedValueOnce(new HttpError("network", null)); return [...recoveryCodes]; });
  render(<Shop />); const user = userEvent.setup();
  if (action === "begin") await user.click(await screen.findByRole("button", { name: words("start") })); else { await setup(); await confirm(); }
  await screen.findByText(words("materialCheckFailed")); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(screen.queryByLabelText(words("codes"))).toBeNull(); await user.click(button("checkStatus")); await screen.findByRole("heading", { name: words(action === "begin" ? "setupTitle" : "recoveryTitle") }); expect(staffTwoFactorApi[action]).toHaveBeenCalledTimes(1);
});
it.each(["id", "tenantId", "role", "email"] as const)("does not begin for changed preflight %s", async field => {
  render(<Shop />); const user = userEvent.setup(); await screen.findByRole("heading", { name: words("off") }); actual = { ...actual, [field]: "different" }; await user.click(button("start")); await screen.findByText(words("changed")); expect(staffTwoFactorApi.begin).not.toHaveBeenCalled();
});
it("does not confirm when another tab already enabled protection", async () => {
  render(<Shop />); await setup(); actual.isTwoFactorEnabled = true; await confirm(); await screen.findByText(words("settingsChanged")); expect(staffTwoFactorApi.confirm).not.toHaveBeenCalled(); expect(screen.queryByLabelText(words("key"))).toBeNull();
});
it("drops recovery codes when the follow-up read belongs to another account", async () => {
  vi.mocked(staffTwoFactorApi.confirm).mockImplementationOnce(async () => { actual = { ...actual, id: "another-person", isTwoFactorEnabled: true }; return [...recoveryCodes]; }); render(<Shop />); await setup(); await confirm(); await screen.findByText(words("changed")); expect(screen.queryByLabelText(words("codes"))).toBeNull(); expect(screen.queryByRole("button", { name: words("showCodes") })).toBeNull();
});
it("refuses recovered disable success when protection is on again", async () => {
  actual.isTwoFactorEnabled = true; vi.mocked(staffTwoFactorApi.turnOff).mockImplementationOnce(async () => { vi.mocked(staffTwoFactorApi.session).mockRejectedValueOnce(new HttpError("network", null)); }); render(<Shop />); const user = await offForm(); fireEvent.submit(form()); await screen.findByText(words("readFailed")); await user.click(button("checkStatus")); await screen.findByText(words("settingsChanged")); expect(screen.queryByText(words("disabled"))).toBeNull();
});
it("handles a setup conflict by discarding old material and reading current status, never repeating confirmation", async () => {
  vi.mocked(staffTwoFactorApi.confirm).mockRejectedValueOnce(new HttpError("http", 409)); render(<Shop />); const user = await setup(); await confirm(); await screen.findByText(words("conflict")); expect(screen.queryByRole("form")).toBeNull(); await user.click(button("checkStatus")); await screen.findByRole("heading", { name: words("off") }); expect(staffTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
it.each([401, 403, 429, 503])("closes or retries only a read after initial status %s", async status => {
  vi.mocked(staffTwoFactorApi.session).mockRejectedValueOnce(new HttpError("http", status)); render(<Shop />); const user = userEvent.setup();
  if (status === 401 || status === 403) { await screen.findByText(words(status === 401 ? "expired" : "changed")); expect(screen.queryByRole("button", { name: words("start") })).toBeNull(); }
  else { await screen.findByText(words("readFailed")); await user.click(button("checkStatus")); await screen.findByRole("heading", { name: words("off") }); }
  expect(staffTwoFactorApi.begin).not.toHaveBeenCalled();
});
it("preserves the non-secret disable acknowledgment after a failed preflight, with a fresh password required", async () => {
  actual.isTwoFactorEnabled = true; render(<Shop />); const user = await offForm(); vi.mocked(staffTwoFactorApi.session).mockRejectedValueOnce(new HttpError("network", null)); fireEvent.submit(form()); await screen.findByText(words("notSubmitted")); expect(staffTwoFactorApi.turnOff).not.toHaveBeenCalled(); await user.click(button("checkStatus")); expect(input("offAcknowledgment").checked).toBe(true); expect(input("password").value).toBe("");
});
it("requires an explicit password and acknowledgment before disabling, and Escape restores focus", async () => {
  actual.isTwoFactorEnabled = true; render(<Shop />); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("turnOff") })); fireEvent.submit(form()); await screen.findByText(words("offRequired")); await user.click(input("offAcknowledgment")); fireEvent.submit(form()); await screen.findAllByText(words("passwordRequired")); expect(staffTwoFactorApi.turnOff).not.toHaveBeenCalled(); await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(button("turnOff"))); expect(screen.queryByLabelText(words("password"))).toBeNull();
});
it("clears raw disable password and reveal state before the preflight, then verifies the flag", async () => {
  actual.isTwoFactorEnabled = true; render(<Shop />); const user = await offForm(); await user.click(button("showPassword")); const held = deferred<StaffSecuritySession>(); vi.mocked(staffTwoFactorApi.session).mockReturnValueOnce(held.promise); fireEvent.submit(form()); fireEvent.submit(form()); expect(input("password").value).toBe(""); expect(input("password").type).toBe("password"); await act(async () => held.resolve({ ...actual })); await screen.findByText(words("disabled")); expect(staffTwoFactorApi.turnOff).toHaveBeenCalledExactlyOnceWith(" raw password ", expect.any(AbortSignal)); expect(staffTwoFactorApi.session).toHaveBeenCalledTimes(3);
});
it.each([403, 429])("requires a new password after disable refusal %s", async status => {
  actual.isTwoFactorEnabled = true; vi.mocked(staffTwoFactorApi.turnOff).mockRejectedValueOnce(new HttpError("http", status)); render(<Shop />); await offForm(); fireEvent.submit(form()); await screen.findAllByText(words(status === 403 ? "wrongPassword" : "limited")); expect(input("password").value).toBe(""); expect(input("offAcknowledgment").checked).toBe(true); fireEvent.submit(form()); expect(staffTwoFactorApi.turnOff).toHaveBeenCalledTimes(1); fireEvent.change(input("password"), { target: { value: " raw password " } }); fireEvent.submit(form()); await screen.findByText(words("disabled"));
});
it("does not replay a lost disable or label its current flag as confirmed completion", async () => {
  actual.isTwoFactorEnabled = true; vi.mocked(staffTwoFactorApi.turnOff).mockImplementationOnce(async () => { actual.isTwoFactorEnabled = false; throw new HttpError("network", null); }); render(<Shop />); const user = await offForm(); fireEvent.submit(form()); await screen.findByText(words("offUncertain")); await user.click(button("checkStatus")); await screen.findByText(words("observedOff")); expect(screen.queryByText(words("disabled"))).toBeNull(); expect(staffTwoFactorApi.turnOff).toHaveBeenCalledTimes(1); expect(screen.queryByRole("form")).toBeNull();
});
it("aborts a departing begin and never reveals a late setup key or issues a follow-up read", async () => {
  const held = deferred<typeof twoFactorSetup>(); vi.mocked(staffTwoFactorApi.begin).mockReturnValueOnce(held.promise); render(<Shop />); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("start") })); await waitFor(() => expect(staffTwoFactorApi.begin).toHaveBeenCalledTimes(1)); await user.click(screen.getByRole("link", { name: "Leave security" })); expect(vi.mocked(staffTwoFactorApi.begin).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(twoFactorSetup)); expect(staffTwoFactorApi.session).toHaveBeenCalledTimes(2); expect(screen.queryByText(words("setupTitle"))).toBeNull();
});
it("aborts a departing confirmation and never reveals a late recovery-code response", async () => {
  const held = deferred<string[]>(); vi.mocked(staffTwoFactorApi.confirm).mockReturnValueOnce(held.promise); render(<Shop />); const user = await setup(); await confirm(); await waitFor(() => expect(staffTwoFactorApi.confirm).toHaveBeenCalledTimes(1)); await user.click(screen.getByRole("link", { name: "Leave security" })); expect(vi.mocked(staffTwoFactorApi.confirm).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(recoveryCodes)); expect(staffTwoFactorApi.session).toHaveBeenCalledTimes(4); expect(screen.queryByText(words("recoveryTitle"))).toBeNull();
});
it("discards secrets and late answers on caller/company movement", async () => {
  const held = deferred<string[]>(); vi.mocked(staffTwoFactorApi.confirm).mockReturnValueOnce(held.promise); const page = render(<Shop />); await setup(); await confirm(); await waitFor(() => expect(staffTwoFactorApi.confirm).toHaveBeenCalledTimes(1)); actual = { ...actual, tenantId: "company-b" }; page.rerender(<Shop user={actual} />); await screen.findByRole("heading", { name: words("off") }); expect(vi.mocked(staffTwoFactorApi.confirm).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(recoveryCodes)); expect(screen.queryByText(words("recoveryTitle"))).toBeNull();
});
it("keeps setup and a code draft across language changes without extra requests", async () => {
  render(<Shop />); await setup(); fireEvent.change(input("code"), { target: { value: "012" } }); await act(async () => { await i18n.changeLanguage("cs"); }); expect(input("code").value).toBe("012"); expect(staffTwoFactorApi.session).toHaveBeenCalledTimes(3); expect(staffTwoFactorApi.begin).toHaveBeenCalledTimes(1); expect(staffTwoFactorApi.confirm).not.toHaveBeenCalled();
});
it("performs only reads under StrictMode until an explicit setup action", async () => { render(<StrictMode><Shop /></StrictMode>); await screen.findByRole("heading", { name: words("off") }); expect(staffTwoFactorApi.begin).not.toHaveBeenCalled(); });
