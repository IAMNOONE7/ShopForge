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
import { operatorTwoFactorApi } from "../api/operatorTwoFactor";
import { i18n, initializeI18n } from "../i18n";
import english from "../i18n/locales/en";
import { recoveryCodes, securityOperator, twoFactorSetup } from "../test/operatorTwoFactorFixtures";
import { OperatorTwoFactorPage } from "./OperatorTwoFactorPage";

type Word = keyof typeof english.resources.operatorTwoFactor;
const words = (key: Word) => i18n.t(`operatorTwoFactor:${key}`);
const button = (key: Word) => screen.getByRole("button", { name: words(key) }) as HTMLButtonElement;
const input = (key: Word) => screen.getByLabelText(words(key), { exact: true }) as HTMLInputElement;
const form = () => screen.getByRole("form");
let actual: PlatformUser;
function Shop({ user = securityOperator, stale = false }: { user?: PlatformUser; stale?: boolean }) {
  return <MemoryRouter initialEntries={["/platform/account/two-factor"]}><Link to="/platform/tenants">Leave security</Link><Routes><Route path="/platform/account/two-factor" element={<OperatorTwoFactorPage user={user} sessionStale={stale} />} /><Route path="/platform/account" element={<h1>Account destination</h1>} /><Route path="/platform/tenants" element={<h1>Companies destination</h1>} /></Routes></MemoryRouter>;
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
async function setup() { const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("start") })); await screen.findByRole("heading", { name: words("setupTitle") }); return user; }
async function confirm() { const user = userEvent.setup(); await user.click(input("added")); fireEvent.change(input("code"), { target: { value: "012345" } }); fireEvent.submit(form()); }
async function offForm() { const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("turnOff") })); await user.click(input("offAcknowledgment")); fireEvent.change(input("password"), { target: { value: " raw password " } }); return user; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); actual = { ...securityOperator };
  vi.spyOn(operatorTwoFactorApi, "session").mockImplementation(async () => ({ ...actual })); vi.spyOn(operatorTwoFactorApi, "begin").mockResolvedValue({ ...twoFactorSetup });
  vi.spyOn(operatorTwoFactorApi, "confirm").mockImplementation(async () => { actual.isTwoFactorEnabled = true; return [...recoveryCodes]; });
  vi.spyOn(operatorTwoFactorApi, "turnOff").mockImplementation(async () => { actual.isTwoFactorEnabled = false; }); HTMLElement.prototype.scrollIntoView = vi.fn();
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); await i18n.changeLanguage("en"); });

it("reads authoritative settings on entry without starting or changing setup", async () => {
  render(<Shop />); await screen.findByRole("heading", { name: words("off") }); expect(operatorTwoFactorApi.session).toHaveBeenCalledTimes(1); expect(operatorTwoFactorApi.begin).not.toHaveBeenCalled(); expect(operatorTwoFactorApi.confirm).not.toHaveBeenCalled(); expect(operatorTwoFactorApi.turnOff).not.toHaveBeenCalled();
});
it("opens the actual operator two-factor route and navigation without company reads", async () => {
  vi.spyOn(platformApi, "me").mockResolvedValue(securityOperator); const staffRead = vi.spyOn(api, "me"), stores = vi.spyOn(api, "stores"), tenants = vi.spyOn(platformApi, "tenants");
  window.history.replaceState(null, "", "/platform/account/two-factor"); render(<App />);
  await screen.findByRole("heading", { name: words("off") }); expect(staffRead).not.toHaveBeenCalled(); expect(stores).not.toHaveBeenCalled(); expect(tenants).not.toHaveBeenCalled();
  expect(screen.getByRole("link", { name: "Operator security" }).getAttribute("aria-current")).toBe("page"); expect(document.title).toBe("Two-factor authentication · ShopForge Platform");
  expect(screen.getByRole("link", { name: words("back") }).getAttribute("href")).toBe("/platform/account"); expect(operatorTwoFactorApi.begin).not.toHaveBeenCalled();
});

it("reveals and selects a manual key explicitly without exposing its URI or opening another origin", async () => {
  render(<Shop />); const user = await setup(); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(document.body.textContent).not.toContain(twoFactorSetup.secret); expect(document.body.innerHTML).not.toContain("otpauth:");
  await user.click(button("showKey")); expect(input("key").value).toBe(twoFactorSetup.secret); expect(input("key").readOnly).toBe(true); await user.click(button("selectKey")); expect(document.activeElement).toBe(input("key")); expect(input("key").selectionEnd).toBe(32); await user.click(button("hideKey")); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(operatorTwoFactorApi.session).toHaveBeenCalledTimes(3);
});
it("requires authenticator enrollment and six ASCII digits, preserving leading zeroes", async () => {
  render(<Shop />); const user = await setup(); fireEvent.submit(form()); await screen.findByText(words("addedRequired")); await user.click(button("goToField")); expect(document.activeElement).toBe(input("added")); await user.click(input("added"));
  for (const value of ["", "12345", "１２３４５６", "ABCDEF", " 12345"]) { fireEvent.change(input("code"), { target: { value } }); fireEvent.submit(form()); expect(await screen.findAllByText(words("codeInvalid"))).toBeTruthy(); }
  expect(operatorTwoFactorApi.confirm).not.toHaveBeenCalled(); fireEvent.change(input("code"), { target: { value: "012345" } }); fireEvent.submit(form()); await screen.findByRole("heading", { name: words("recoveryTitle") }); expect(operatorTwoFactorApi.confirm).toHaveBeenCalledExactlyOnceWith("012345", expect.any(AbortSignal));
});
it("clears the code and hides the key before preflight, suppressing duplicate confirmation", async () => {
  render(<Shop />); const user = await setup(); await user.click(button("showKey")); const held = deferred<PlatformUser>(); vi.mocked(operatorTwoFactorApi.session).mockReturnValueOnce(held.promise); await confirm(); fireEvent.submit(form()); expect(input("code").value).toBe(""); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(operatorTwoFactorApi.confirm).not.toHaveBeenCalled(); await act(async () => held.resolve(securityOperator)); await screen.findByRole("heading", { name: words("recoveryTitle") }); expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
it("keeps recovery codes hidden until explicit reveal and requires saving before clearing them", async () => {
  render(<Shop />); const user = await setup(); await confirm(); await screen.findByRole("heading", { name: words("recoveryTitle") }); expect(screen.queryByLabelText(words("codes"))).toBeNull(); expect(document.body.innerHTML).not.toContain(twoFactorSetup.secret); expect(button("finish").disabled).toBe(true); expect(input("saved").disabled).toBe(true);
  await user.click(button("showCodes")); const codes = screen.getByLabelText(words("codes")) as HTMLTextAreaElement; expect(codes.value).toBe(recoveryCodes.join("\n")); await user.click(button("selectCodes")); expect(document.activeElement).toBe(codes); expect(codes.selectionEnd).toBe(codes.value.length); await user.click(input("saved")); await user.click(button("finish")); await screen.findByText(words("finished")); expect(screen.queryByLabelText(words("codes"))).toBeNull(); for (const code of recoveryCodes) expect(document.body.innerHTML).not.toContain(code); expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
it("discards local setup without inventing a cancel endpoint or claiming backend rollback", async () => {
  render(<Shop />); const user = await setup(); await user.click(button("showKey")); await user.click(button("discard")); await screen.findByText(words("discarded")); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(operatorTwoFactorApi.begin).toHaveBeenCalledTimes(1); expect(operatorTwoFactorApi.turnOff).not.toHaveBeenCalled(); await user.click(button("start")); expect(operatorTwoFactorApi.begin).toHaveBeenCalledTimes(2);
});
it.each([400, 403, 429])("requires a fresh code after known confirmation refusal %s, keeping the original setup", async status => {
  vi.mocked(operatorTwoFactorApi.confirm).mockRejectedValueOnce(new HttpError("http", status)); render(<Shop />); await setup(); await confirm(); await screen.findAllByText(words(status === 403 ? "wrongCode" : status === 429 ? "limited" : "refused")); expect(input("code").value).toBe(""); expect(input("added").checked).toBe(true); expect(screen.queryByLabelText(words("key"))).toBeNull(); fireEvent.submit(form()); expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1); fireEvent.change(input("code"), { target: { value: "012345" } }); fireEvent.submit(form()); await screen.findByRole("heading", { name: words("recoveryTitle") }); expect(operatorTwoFactorApi.begin).toHaveBeenCalledTimes(1);
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 200)])("never repeats unknown confirmation or recovers codes by reading an enabled flag: %j", async error => {
  vi.mocked(operatorTwoFactorApi.confirm).mockImplementationOnce(async () => { actual.isTwoFactorEnabled = true; throw error; }); render(<Shop />); const user = await setup(); await confirm(); await screen.findByText(words("confirmUncertain")); expect(screen.queryByRole("form")).toBeNull(); expect(screen.queryByLabelText(words("key"))).toBeNull(); await user.click(button("checkStatus")); await screen.findByText(words("observedOn")); expect(screen.queryByLabelText(words("codes"))).toBeNull(); expect(screen.queryByRole("button", { name: words("confirm") })).toBeNull(); expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
it("does not repeat a lost begin even when the current flag stays off", async () => {
  vi.mocked(operatorTwoFactorApi.begin).mockRejectedValueOnce(new HttpError("network", null)); render(<Shop />); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("start") })); await screen.findByText(words("beginUncertain")); await user.click(button("checkStatus")); await screen.findByText(words("observedOff")); expect(screen.queryByRole("button", { name: words("start") })).toBeNull(); expect(operatorTwoFactorApi.begin).toHaveBeenCalledTimes(1);
});
it.each(["begin", "confirm"] as const)("keeps %s response material hidden during failed follow-up reads and recovers with GET only", async action => {
  if (action === "begin") vi.mocked(operatorTwoFactorApi.begin).mockImplementationOnce(async () => { vi.mocked(operatorTwoFactorApi.session).mockRejectedValueOnce(new HttpError("network", null)); return twoFactorSetup; });
  else vi.mocked(operatorTwoFactorApi.confirm).mockImplementationOnce(async () => { actual.isTwoFactorEnabled = true; vi.mocked(operatorTwoFactorApi.session).mockRejectedValueOnce(new HttpError("network", null)); return [...recoveryCodes]; });
  render(<Shop />); const user = userEvent.setup();
  if (action === "begin") await user.click(await screen.findByRole("button", { name: words("start") })); else { await setup(); await confirm(); }
  await screen.findByText(words("materialCheckFailed")); expect(screen.queryByLabelText(words("key"))).toBeNull(); expect(screen.queryByLabelText(words("codes"))).toBeNull(); await user.click(button("checkStatus")); await screen.findByRole("heading", { name: words(action === "begin" ? "setupTitle" : "recoveryTitle") }); expect(operatorTwoFactorApi[action]).toHaveBeenCalledTimes(1);
});
it.each(["id", "email"] as const)("does not begin for changed preflight %s", async field => {
  render(<Shop />); const user = userEvent.setup(); await screen.findByRole("heading", { name: words("off") }); actual = { ...actual, [field]: "different" }; await user.click(button("start")); await screen.findByText(words("changed")); expect(operatorTwoFactorApi.begin).not.toHaveBeenCalled();
});
it("does not confirm when another tab already enabled protection", async () => {
  render(<Shop />); await setup(); actual.isTwoFactorEnabled = true; await confirm(); await screen.findByText(words("settingsChanged")); expect(operatorTwoFactorApi.confirm).not.toHaveBeenCalled(); expect(screen.queryByLabelText(words("key"))).toBeNull();
});
it("drops recovery codes when the follow-up read belongs to another account", async () => {
  vi.mocked(operatorTwoFactorApi.confirm).mockImplementationOnce(async () => { actual = { ...actual, id: "another-person", isTwoFactorEnabled: true }; return [...recoveryCodes]; }); render(<Shop />); await setup(); await confirm(); await screen.findByText(words("changed")); expect(screen.queryByLabelText(words("codes"))).toBeNull(); expect(screen.queryByRole("button", { name: words("showCodes") })).toBeNull();
});
it("refuses recovered disable success when protection is on again", async () => {
  actual.isTwoFactorEnabled = true; vi.mocked(operatorTwoFactorApi.turnOff).mockImplementationOnce(async () => { vi.mocked(operatorTwoFactorApi.session).mockRejectedValueOnce(new HttpError("network", null)); }); render(<Shop />); const user = await offForm(); fireEvent.submit(form()); await screen.findByText(words("readFailed")); await user.click(button("checkStatus")); await screen.findByText(words("settingsChanged")); expect(screen.queryByText(words("disabled"))).toBeNull();
});
it("handles a setup conflict by discarding old material and reading current status, never repeating confirmation", async () => {
  vi.mocked(operatorTwoFactorApi.confirm).mockRejectedValueOnce(new HttpError("http", 409)); render(<Shop />); const user = await setup(); await confirm(); await screen.findByText(words("conflict")); expect(screen.queryByRole("form")).toBeNull(); await user.click(button("checkStatus")); await screen.findByRole("heading", { name: words("off") }); expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
it.each([401, 403, 429, 503])("closes or retries only a read after initial status %s", async status => {
  vi.mocked(operatorTwoFactorApi.session).mockRejectedValueOnce(new HttpError("http", status)); render(<Shop />); const user = userEvent.setup();
  if (status === 401 || status === 403) { await screen.findByText(words(status === 401 ? "expired" : "changed")); expect(screen.queryByRole("button", { name: words("start") })).toBeNull(); }
  else { await screen.findByText(words("readFailed")); await user.click(button("checkStatus")); await screen.findByRole("heading", { name: words("off") }); }
  expect(operatorTwoFactorApi.begin).not.toHaveBeenCalled();
});
it("preserves the non-secret disable acknowledgment after a failed preflight, with a fresh password required", async () => {
  actual.isTwoFactorEnabled = true; render(<Shop />); const user = await offForm(); vi.mocked(operatorTwoFactorApi.session).mockRejectedValueOnce(new HttpError("network", null)); fireEvent.submit(form()); await screen.findByText(words("notSubmitted")); expect(operatorTwoFactorApi.turnOff).not.toHaveBeenCalled(); await user.click(button("checkStatus")); expect(input("offAcknowledgment").checked).toBe(true); expect(input("password").value).toBe("");
});
it("requires an explicit password and acknowledgment before disabling, and Escape restores focus", async () => {
  actual.isTwoFactorEnabled = true; render(<Shop />); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("turnOff") })); fireEvent.submit(form()); await screen.findByText(words("offRequired")); await user.click(input("offAcknowledgment")); fireEvent.submit(form()); await screen.findAllByText(words("passwordRequired")); expect(operatorTwoFactorApi.turnOff).not.toHaveBeenCalled(); await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(button("turnOff"))); expect(screen.queryByLabelText(words("password"))).toBeNull();
});
it("clears raw disable password and reveal state before the preflight, then verifies the flag", async () => {
  actual.isTwoFactorEnabled = true; render(<Shop />); const user = await offForm(); await user.click(button("showPassword")); const held = deferred<PlatformUser>(); vi.mocked(operatorTwoFactorApi.session).mockReturnValueOnce(held.promise); fireEvent.submit(form()); fireEvent.submit(form()); expect(input("password").value).toBe(""); expect(input("password").type).toBe("password"); await act(async () => held.resolve({ ...actual })); await screen.findByText(words("disabled")); expect(operatorTwoFactorApi.turnOff).toHaveBeenCalledExactlyOnceWith(" raw password ", expect.any(AbortSignal)); expect(operatorTwoFactorApi.session).toHaveBeenCalledTimes(3);
});
it.each([403, 429])("requires a new password after disable refusal %s", async status => {
  actual.isTwoFactorEnabled = true; vi.mocked(operatorTwoFactorApi.turnOff).mockRejectedValueOnce(new HttpError("http", status)); render(<Shop />); await offForm(); fireEvent.submit(form()); await screen.findAllByText(words(status === 403 ? "wrongPassword" : "limited")); expect(input("password").value).toBe(""); expect(input("offAcknowledgment").checked).toBe(true); fireEvent.submit(form()); expect(operatorTwoFactorApi.turnOff).toHaveBeenCalledTimes(1); fireEvent.change(input("password"), { target: { value: " raw password " } }); fireEvent.submit(form()); await screen.findByText(words("disabled"));
});
it("does not replay a lost disable or label its current flag as confirmed completion", async () => {
  actual.isTwoFactorEnabled = true; vi.mocked(operatorTwoFactorApi.turnOff).mockImplementationOnce(async () => { actual.isTwoFactorEnabled = false; throw new HttpError("network", null); }); render(<Shop />); const user = await offForm(); fireEvent.submit(form()); await screen.findByText(words("offUncertain")); await user.click(button("checkStatus")); await screen.findByText(words("observedOff")); expect(screen.queryByText(words("disabled"))).toBeNull(); expect(operatorTwoFactorApi.turnOff).toHaveBeenCalledTimes(1); expect(screen.queryByRole("form")).toBeNull();
});
it("aborts a departing begin and never reveals a late setup key or issues a follow-up read", async () => {
  const held = deferred<typeof twoFactorSetup>(); vi.mocked(operatorTwoFactorApi.begin).mockReturnValueOnce(held.promise); render(<Shop />); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("start") })); await waitFor(() => expect(operatorTwoFactorApi.begin).toHaveBeenCalledTimes(1)); await user.click(screen.getByRole("link", { name: "Leave security" })); expect(vi.mocked(operatorTwoFactorApi.begin).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(twoFactorSetup)); expect(operatorTwoFactorApi.session).toHaveBeenCalledTimes(2); expect(screen.queryByText(words("setupTitle"))).toBeNull();
});
it("aborts a departing confirmation and never reveals a late recovery-code response", async () => {
  const held = deferred<string[]>(); vi.mocked(operatorTwoFactorApi.confirm).mockReturnValueOnce(held.promise); render(<Shop />); const user = await setup(); await confirm(); await waitFor(() => expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1)); await user.click(screen.getByRole("link", { name: "Leave security" })); expect(vi.mocked(operatorTwoFactorApi.confirm).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(recoveryCodes)); expect(operatorTwoFactorApi.session).toHaveBeenCalledTimes(4); expect(screen.queryByText(words("recoveryTitle"))).toBeNull();
});
it("discards secrets and late answers on operator movement", async () => {
  const held = deferred<string[]>(); vi.mocked(operatorTwoFactorApi.confirm).mockReturnValueOnce(held.promise); const page = render(<Shop />); await setup(); await confirm(); await waitFor(() => expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1)); actual = { ...actual, id: "00000000-0000-0000-0000-000000000002" }; page.rerender(<Shop user={actual} />); await screen.findByRole("heading", { name: words("off") }); expect(vi.mocked(operatorTwoFactorApi.confirm).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(recoveryCodes)); expect(screen.queryByText(words("recoveryTitle"))).toBeNull();
});
it("keeps setup and a code draft across language changes without extra requests", async () => {
  render(<Shop />); await setup(); fireEvent.change(input("code"), { target: { value: "012" } }); await act(async () => { await i18n.changeLanguage("cs"); }); expect(input("code").value).toBe("012"); expect(operatorTwoFactorApi.session).toHaveBeenCalledTimes(3); expect(operatorTwoFactorApi.begin).toHaveBeenCalledTimes(1); expect(operatorTwoFactorApi.confirm).not.toHaveBeenCalled();
});
it("performs only reads under StrictMode until an explicit setup action", async () => { render(<StrictMode><Shop /></StrictMode>); await screen.findByRole("heading", { name: words("off") }); expect(operatorTwoFactorApi.begin).not.toHaveBeenCalled(); });

it("starts no read or mutation while the platform shell is unresolved", () => {
  render(<Shop stale />); expect(operatorTwoFactorApi.session).not.toHaveBeenCalled(); expect(operatorTwoFactorApi.begin).not.toHaveBeenCalled(); expect(screen.queryByRole("form")).toBeNull();
});
it.each(["setup", "recovery", "off"] as const)("drops local %s material when parent logout pauses the shell", async phase => {
  if (phase === "off") actual.isTwoFactorEnabled = true;
  const view = render(<Shop />); if (phase === "off") await offForm(); else { const user = await setup(); if (phase === "setup") await user.click(button("showKey")); else { await confirm(); await screen.findByRole("heading", { name: words("recoveryTitle") }); await user.click(button("showCodes")); } }
  view.rerender(<Shop stale />); expect(screen.queryByRole("form")).toBeNull(); expect(document.body.innerHTML).not.toContain(twoFactorSetup.secret); for (const code of recoveryCodes) expect(document.body.innerHTML).not.toContain(code);
  view.rerender(<Shop />); await screen.findByRole("heading", { name: words(actual.isTwoFactorEnabled ? "on" : "off") }); expect(screen.queryByRole("button", { name: words("showKey") })).toBeNull(); expect(screen.queryByRole("button", { name: words("showCodes") })).toBeNull();
});
it("aborts a confirming command when parent logout starts and never publishes late codes", async () => {
  const held = deferred<string[]>(); vi.mocked(operatorTwoFactorApi.confirm).mockReturnValueOnce(held.promise); const view = render(<Shop />); await setup(); await confirm(); await waitFor(() => expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1)); const reads = vi.mocked(operatorTwoFactorApi.session).mock.calls.length;
  view.rerender(<Shop stale />); expect(vi.mocked(operatorTwoFactorApi.confirm).mock.calls[0][1].aborted).toBe(true); await act(async () => held.resolve(recoveryCodes)); expect(operatorTwoFactorApi.session).toHaveBeenCalledTimes(reads); expect(screen.queryByRole("button", { name: words("showCodes") })).toBeNull();
});
it.each(["begin", "confirm", "off"] as const)("never displays echoed private material from a failed %s", async action => {
  const error = new HttpError("http", 503, { detail: twoFactorSetup.secret, errors: { code: recoveryCodes }, traceId: "private-reference" }, "private-reference");
  if (action === "off") actual.isTwoFactorEnabled = true;
  const view = render(<Shop />);
  if (action === "begin") { vi.mocked(operatorTwoFactorApi.begin).mockRejectedValueOnce(error); const user = userEvent.setup(); await user.click(await screen.findByRole("button", { name: words("start") })); }
  else if (action === "confirm") { await setup(); vi.mocked(operatorTwoFactorApi.confirm).mockRejectedValueOnce(error); await confirm(); }
  else { await offForm(); vi.mocked(operatorTwoFactorApi.turnOff).mockRejectedValueOnce(error); fireEvent.submit(form()); }
  await screen.findByText(words(action === "begin" ? "beginUncertain" : action === "confirm" ? "confirmUncertain" : "offUncertain"));
  expect(document.body.innerHTML).not.toContain(twoFactorSetup.secret); for (const code of recoveryCodes) expect(document.body.innerHTML).not.toContain(code); expect(document.body.innerHTML).not.toContain("private-reference"); expect(location.search + location.hash).toBe(""); view.unmount();
});
it("keeps recovery material and its saved acknowledgment through a language change without requests", async () => {
  render(<Shop />); const user = await setup(); await confirm(); await screen.findByRole("heading", { name: words("recoveryTitle") }); await user.click(button("showCodes")); await user.click(input("saved")); const reads = vi.mocked(operatorTwoFactorApi.session).mock.calls.length;
  await act(async () => { await i18n.changeLanguage("cs"); }); expect((screen.getByLabelText(words("codes")) as HTMLTextAreaElement).value).toBe(recoveryCodes.join("\n")); expect(input("saved").checked).toBe(true); expect(operatorTwoFactorApi.session).toHaveBeenCalledTimes(reads); expect(operatorTwoFactorApi.confirm).toHaveBeenCalledTimes(1);
});
