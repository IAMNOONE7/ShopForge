// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { operatorRecoveryApi } from "../api/operatorRecovery";
import { HttpError } from "../api/http";
import { platformApi } from "../api/platform";
import { captureStaffRecoveryEntry } from "../staffRecoveryEntry";
import { captureOperatorRecoveryEntry, type OperatorResetProof } from "./operatorRecoveryEntry";
import { i18n, initializeI18n } from "../i18n";
import { ForgotOperatorPasswordPage, ResetOperatorPasswordPage } from "./OperatorPasswordPages";
const token = ["fixture", "operator", "proof"].join("-"), secret = " raw password ";
function proof() { history.replaceState(null, "", "/platform/reset-password?token=" + token); const entry = captureOperatorRecoveryEntry(); if (entry?.kind !== "reset") throw new Error("Expected reset proof"); return entry.proof; }
function Forgot() { return <MemoryRouter initialEntries={["/platform/forgot-password"]}><ForgotOperatorPasswordPage /></MemoryRouter>; }
function Reset({ entry }: { entry: OperatorResetProof | null }) { return <MemoryRouter initialEntries={["/platform/reset-password"]}><Routes><Route path="/platform/reset-password" element={<ResetOperatorPasswordPage proof={entry} />} /><Route path="/platform/forgot-password" element={<ForgotOperatorPasswordPage />} /></Routes></MemoryRouter>; }
async function fill(user: ReturnType<typeof userEvent.setup>, value = secret) { await user.type(await screen.findByLabelText("New password"), value); await user.type(screen.getByLabelText("Repeat new password"), value); }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); vi.spyOn(operatorRecoveryApi, "forgot").mockResolvedValue(); vi.spyOn(operatorRecoveryApi, "reset").mockResolvedValue(); vi.spyOn(operatorRecoveryApi, "session").mockResolvedValue(null); vi.spyOn(operatorRecoveryApi, "signOut").mockResolvedValue();
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); history.replaceState(null, "", "/"); localStorage.clear(); sessionStorage.clear(); await i18n.changeLanguage("en"); });
it("requests recovery only after explicit valid input and gives non-enumerating acceptance", async () => {
  const user = userEvent.setup(); render(<Forgot />); await user.click(screen.getByRole("button", { name: "Request reset link" })); expect(operatorRecoveryApi.forgot).not.toHaveBeenCalled(); await user.click(screen.getByRole("button", { name: "Enter a valid operator email address." })); expect(document.activeElement).toBe(screen.getByRole("textbox"));
  await user.type(screen.getByRole("textbox"), " Operator@example.test "); await user.click(screen.getByRole("button", { name: "Request reset link" })); await screen.findByRole("heading", { name: "Check your email" }); expect(operatorRecoveryApi.forgot).toHaveBeenCalledExactlyOnceWith("Operator@example.test", expect.any(AbortSignal)); expect(screen.getByText(/If that address belongs to an active operator account/)).toBeTruthy(); expect(operatorRecoveryApi.session).not.toHaveBeenCalled();
});
it.each([400, 429])("keeps a known recovery-request refusal %s explicit and preserves the address", async status => {
  vi.mocked(operatorRecoveryApi.forgot).mockRejectedValueOnce(new HttpError("http", status)); const user = userEvent.setup(); render(<Forgot />); await user.type(screen.getByRole("textbox"), "operator@example.test"); await user.click(screen.getByRole("button", { name: "Request reset link" })); await screen.findByRole("alert"); expect(screen.getByRole("textbox")).toHaveProperty("value", "operator@example.test"); expect(operatorRecoveryApi.forgot).toHaveBeenCalledTimes(1);
});
it("blocks another request on an uncertain page without claiming delivery or account existence", async () => {
  vi.mocked(operatorRecoveryApi.forgot).mockRejectedValue(new HttpError("network", null)); const user = userEvent.setup(); render(<Forgot />); await user.type(screen.getByRole("textbox"), "operator@example.test"); await user.click(screen.getByRole("button", { name: "Request reset link" })); await screen.findByText(/This page will not send another email request/); expect(screen.queryByRole("form")).toBeNull(); expect(operatorRecoveryApi.forgot).toHaveBeenCalledTimes(1);
});
it.each(["forgot", "reset"] as const)("routes public %s before staff or operator gates and scrubs credentials before reads", async kind => {
  history.replaceState(null, "", `/platform/${kind === "forgot" ? "forgot" : "reset"}-password?token=${token}&returnTo=https://foreign.test#private`); const entry = captureOperatorRecoveryEntry(), me = vi.spyOn(api, "me"), stores = vi.spyOn(api, "stores");
  const platformMe = vi.spyOn(platformApi, "me"), tenants = vi.spyOn(platformApi, "tenants");
  vi.mocked(operatorRecoveryApi.session).mockImplementation(async () => { expect(location.search + location.hash).toBe(""); return null; }); render(<App operatorRecoveryEntry={entry} />); await screen.findByRole("heading", { name: kind === "forgot" ? "Recover operator access" : "Choose a new operator password" }); expect(me).not.toHaveBeenCalled(); expect(stores).not.toHaveBeenCalled(); expect(platformMe).not.toHaveBeenCalled(); expect(tenants).not.toHaveBeenCalled(); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled();
});
it("rejects a staff-purpose proof without consuming it or reading either session", async () => {
  history.replaceState(null, "", "/reset-password?token=staff-purpose-proof"); const entry = captureStaffRecoveryEntry(); if (entry?.kind !== "reset") throw new Error("Expected staff proof"); const staffRead = vi.spyOn(api, "me");
  render(<Reset entry={entry.proof as unknown as OperatorResetProof} />); await screen.findByRole("heading", { name: "Operator reset link unavailable" }); expect(entry.proof.available()).toBe(true); expect(operatorRecoveryApi.session).not.toHaveBeenCalled(); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled(); expect(staffRead).not.toHaveBeenCalled();
});
it("closes missing proof without calling an API or exposing account facts", async () => {
  render(<Reset entry={null} />); await screen.findByRole("heading", { name: "Operator reset link unavailable" }); expect(operatorRecoveryApi.session).not.toHaveBeenCalled(); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled();
});
it("resets explicitly with raw matching passwords, clears/re-hides them and never signs the user in", async () => {
  const login = vi.spyOn(api, "login"), user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); await user.click(screen.getByRole("button", { name: "Show passwords" })); await user.click(screen.getByRole("button", { name: "Reset operator password" }));
  await screen.findByRole("heading", { name: "Operator password reset" }); expect(operatorRecoveryApi.reset).toHaveBeenCalledExactlyOnceWith(token, secret, expect.any(AbortSignal)); expect(entry.available()).toBe(false); expect(screen.queryByLabelText("New password")).toBeNull(); expect(login).not.toHaveBeenCalled(); expect(screen.getByText(/you still need an authenticator or recovery code/)).toBeTruthy(); expect(screen.getByRole("link", { name: "Continue to operator sign-in" }).getAttribute("href")).toBe("/platform/tenants"); expect(document.body.innerHTML).not.toContain(token); expect(document.body.innerHTML).not.toContain(secret);
});
it.each([9, 129])("validates the actual %s-character password bound before claiming the proof", async length => {
  const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await screen.findByLabelText("New password"); for (const label of ["New password", "Repeat new password"]) fireEvent.change(screen.getByLabelText(label), { target: { value: "x".repeat(length) } }); await user.click(screen.getByRole("button", { name: "Reset operator password" }));
  await user.click(within(screen.getByRole("alert")).getByRole("button", { name: "Use a password between 10 and 128 characters." })); expect(document.activeElement).toBe(screen.getByLabelText("New password")); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled(); expect(entry.available()).toBe(true);
});
it.each([10, 128])("accepts the exact %s-character password boundary", async length => {
  const user = userEvent.setup(); render(<Reset entry={proof()} />); await fill(user, "x".repeat(length)); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByRole("heading", { name: "Operator password reset" }); expect(vi.mocked(operatorRecoveryApi.reset).mock.calls[0][1]).toHaveLength(length);
});
it("requires exact confirmation and focuses its field without spending proof", async () => {
  const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); await user.type(screen.getByLabelText("Repeat new password"), "x"); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await user.click(within(screen.getByRole("alert")).getByRole("button", { name: "Repeat the exact same password." })); expect(document.activeElement).toBe(screen.getByLabelText("Repeat new password")); expect(entry.available()).toBe(true); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled();
});
it.each([400, 429])("allows known refusal %s to be retried only on the current page with fresh hidden passwords", async status => {
  vi.mocked(operatorRecoveryApi.reset).mockRejectedValueOnce(new HttpError("http", status)); const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); await user.click(screen.getByRole("button", { name: "Show passwords" })); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByRole("alert");
  expect(screen.getByLabelText("New password")).toHaveProperty("value", ""); expect(screen.getByLabelText("New password")).toHaveProperty("type", "password"); expect(screen.getByLabelText("Repeat new password")).toHaveProperty("value", ""); expect(entry.available()).toBe(false); await fill(user, "another password"); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByRole("heading", { name: "Operator password reset" }); expect(operatorRecoveryApi.reset).toHaveBeenCalledTimes(2);
});
it("does not restore a refused proof after the page unmounts", async () => {
  vi.mocked(operatorRecoveryApi.reset).mockRejectedValueOnce(new HttpError("http", 429)); const user = userEvent.setup(), entry = proof(), view = render(<Reset entry={entry} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByRole("alert"); view.unmount(); render(<Reset entry={entry} />); await screen.findByRole("heading", { name: "Operator reset link unavailable" }); expect(operatorRecoveryApi.reset).toHaveBeenCalledTimes(1);
});
it("closes a server-rejected used/expired link without claiming a successful reset", async () => {
  vi.mocked(operatorRecoveryApi.reset).mockRejectedValue(new HttpError("http", 404)); const user = userEvent.setup(); render(<Reset entry={proof()} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByRole("heading", { name: "Operator reset link unavailable" }); expect(screen.queryByLabelText("New password")).toBeNull(); expect(screen.queryByRole("heading", { name: "Operator password reset" })).toBeNull();
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("http", 409), new HttpError("http", 401), new HttpError("invalid-response", 200)])("never replays an uncertain reset, including a remount with the same entry: %j", async error => {
  vi.mocked(operatorRecoveryApi.reset).mockRejectedValue(error); const user = userEvent.setup(), entry = proof(), view = render(<Reset entry={entry} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByText(/This page will not submit this link again/); expect(screen.queryByRole("form")).toBeNull(); expect(operatorRecoveryApi.reset).toHaveBeenCalledTimes(1); view.unmount(); render(<Reset entry={entry} />); await screen.findByRole("heading", { name: "Operator reset link unavailable" }); expect(operatorRecoveryApi.reset).toHaveBeenCalledTimes(1);
});
it("requires explicit operator logout and observes guest before enabling password fields", async () => {
  vi.mocked(operatorRecoveryApi.session).mockResolvedValue({ email: "other@example.test" }); const user = userEvent.setup(); render(<Reset entry={proof()} />); await screen.findByText(/Currently signed in as other@example.test/); expect(operatorRecoveryApi.signOut).not.toHaveBeenCalled(); expect(screen.queryByLabelText("New password")).toBeNull(); await user.click(screen.getByRole("button", { name: "Sign out of operator account before resetting" })); await screen.findByText(/still has an operator session/); expect(screen.queryByLabelText("New password")).toBeNull();
  vi.mocked(operatorRecoveryApi.session).mockResolvedValue(null); await user.click(screen.getByRole("button", { name: "Sign out of operator account before resetting" })); await screen.findByLabelText("New password"); expect(operatorRecoveryApi.signOut).toHaveBeenCalledTimes(2); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled();
});
it("retries only GET after a failed logout-follow-up read", async () => {
  vi.mocked(operatorRecoveryApi.session).mockResolvedValueOnce({ email: "other@example.test" }).mockRejectedValueOnce(new HttpError("network", null)).mockResolvedValue(null); const user = userEvent.setup(); render(<Reset entry={proof()} />); await user.click(await screen.findByRole("button", { name: "Sign out of operator account before resetting" })); await user.click(await screen.findByRole("button", { name: "Check operator sign-in" })); await screen.findByLabelText("New password"); expect(operatorRecoveryApi.signOut).toHaveBeenCalledTimes(1);
});
it("detects another-tab operator login before resetting without spending proof or keeping passwords", async () => {
  const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); vi.mocked(operatorRecoveryApi.session).mockResolvedValue({ email: "other@example.test" }); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByText(/Currently signed in as other@example.test/); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled(); expect(entry.available()).toBe(true); expect(screen.queryByLabelText("New password")).toBeNull();
});
it("keeps failed preflight distinct from a submitted reset and permits GET-only recovery", async () => {
  const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); vi.mocked(operatorRecoveryApi.session).mockRejectedValueOnce(new HttpError("network", null)); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByText(/No password reset was submitted after this failed check/); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled(); expect(entry.available()).toBe(true); await user.click(screen.getByRole("button", { name: "Check operator sign-in" })); expect(await screen.findByLabelText("New password")).toHaveProperty("value", "");
});
it("locks duplicate submissions, keeps a pending form safely blank and discards late reset answers on departure", async () => {
  const held = deferred<void>(); vi.mocked(operatorRecoveryApi.reset).mockReturnValue(held.promise); const user = userEvent.setup(), entry = proof(), view = render(<Reset entry={entry} />); await fill(user); fireEvent.submit(screen.getByRole("form")); fireEvent.submit(screen.getByRole("form")); await waitFor(() => expect(operatorRecoveryApi.reset).toHaveBeenCalledTimes(1)); expect(screen.getByLabelText("New password")).toHaveProperty("value", ""); expect(screen.getByLabelText("New password")).toHaveProperty("disabled", true); view.unmount(); expect(vi.mocked(operatorRecoveryApi.reset).mock.calls[0][2].aborted).toBe(true); await act(async () => held.resolve()); render(<Reset entry={entry} />); await screen.findByRole("heading", { name: "Operator reset link unavailable" });
});
it("scopes a new proof to a fresh form and discards old in-flight work", async () => {
  const held = deferred<void>(); vi.mocked(operatorRecoveryApi.reset).mockReturnValue(held.promise); const user = userEvent.setup(), view = render(<Reset entry={proof()} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await waitFor(() => expect(operatorRecoveryApi.reset).toHaveBeenCalledTimes(1)); view.rerender(<Reset entry={proof()} />); expect(vi.mocked(operatorRecoveryApi.reset).mock.calls[0][2].aborted).toBe(true); await act(async () => held.resolve()); expect(await screen.findByLabelText("New password")).toHaveProperty("value", ""); expect(screen.queryByRole("heading", { name: "Operator password reset" })).toBeNull();
});
it("does not leak echoed server credentials, references, confirmation or URL destinations", async () => {
  vi.mocked(operatorRecoveryApi.reset).mockRejectedValue(new HttpError("http", 503, { detail: token, traceId: token, errors: { [token]: [secret] } }, token)); const user = userEvent.setup(); render(<Reset entry={proof()} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset operator password" })); await screen.findByText(/This page will not submit this link again/); expect(document.body.innerHTML).not.toContain(token); expect(document.body.innerHTML).not.toContain(secret); expect(Object.values({ ...localStorage, ...sessionStorage }).join(" ")).not.toContain(token); expect(Object.values({ ...localStorage, ...sessionStorage }).join(" ")).not.toContain(secret);
});
it("preserves local valid secrets on a language change without replaying any request", async () => {
  const user = userEvent.setup(); render(<Reset entry={proof()} />); await fill(user); const reads = vi.mocked(operatorRecoveryApi.session).mock.calls.length; await act(async () => { await i18n.changeLanguage("cs"); }); expect(screen.getByLabelText(i18n.t("operatorRecovery:password"))).toHaveProperty("value", secret); expect(operatorRecoveryApi.session).toHaveBeenCalledTimes(reads); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled();
});
it("completes StrictMode reads without spending proof or resetting automatically", async () => { render(<StrictMode><Reset entry={proof()} /></StrictMode>); await screen.findByLabelText("New password"); expect(operatorRecoveryApi.reset).not.toHaveBeenCalled(); });
it("does not start another session read after departing during logout", async () => {
  const held = deferred<void>(); vi.mocked(operatorRecoveryApi.session).mockResolvedValue({ email: "other@example.test" }); vi.mocked(operatorRecoveryApi.signOut).mockReturnValue(held.promise); const user = userEvent.setup(), view = render(<Reset entry={proof()} />); await user.click(await screen.findByRole("button", { name: "Sign out of operator account before resetting" })); const reads = vi.mocked(operatorRecoveryApi.session).mock.calls.length; view.unmount(); expect(vi.mocked(operatorRecoveryApi.signOut).mock.calls[0][0].aborted).toBe(true); await act(async () => held.resolve()); expect(operatorRecoveryApi.session).toHaveBeenCalledTimes(reads);
});
