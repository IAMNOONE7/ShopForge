// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { staffRecoveryApi } from "../api/staffRecovery";
import { HttpError } from "../api/http";
import { captureStaffRecoveryEntry, type StaffResetProof } from "../staffRecoveryEntry";
import { captureOperatorRecoveryEntry } from "../platform/operatorRecoveryEntry";
import { i18n, initializeI18n } from "../i18n";
import { ForgotStaffPasswordPage, ResetStaffPasswordPage } from "./StaffPasswordPages";
const token = ["fixture", "staff", "proof"].join("-"), secret = " raw password ";
function proof() { history.replaceState(null, "", "/reset-password?token=" + token); const entry = captureStaffRecoveryEntry(); if (entry?.kind !== "reset") throw new Error("Expected reset proof"); return entry.proof; }
function Forgot() { return <MemoryRouter initialEntries={["/forgot-password"]}><ForgotStaffPasswordPage /></MemoryRouter>; }
function Reset({ entry }: { entry: StaffResetProof | null }) { return <MemoryRouter initialEntries={["/reset-password"]}><Routes><Route path="/reset-password" element={<ResetStaffPasswordPage proof={entry} />} /><Route path="/forgot-password" element={<ForgotStaffPasswordPage />} /></Routes></MemoryRouter>; }
async function fill(user: ReturnType<typeof userEvent.setup>, value = secret) { await user.type(await screen.findByLabelText("New password"), value); await user.type(screen.getByLabelText("Repeat new password"), value); }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
it("rejects an operator-purpose proof without consuming it or reading a staff session", async () => {
  history.replaceState(null, "", "/platform/reset-password?token=operator-purpose-proof");
  const entry = captureOperatorRecoveryEntry(); if (entry?.kind !== "reset") throw new Error("Expected operator proof");
  render(<Reset entry={entry.proof as unknown as StaffResetProof} />);
  await screen.findByRole("heading", { name: "Reset link unavailable" });
  expect(entry.proof.available()).toBe(true); expect(staffRecoveryApi.session).not.toHaveBeenCalled(); expect(staffRecoveryApi.reset).not.toHaveBeenCalled();
});
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); vi.spyOn(staffRecoveryApi, "forgot").mockResolvedValue(); vi.spyOn(staffRecoveryApi, "reset").mockResolvedValue(); vi.spyOn(staffRecoveryApi, "session").mockResolvedValue(null); vi.spyOn(staffRecoveryApi, "signOut").mockResolvedValue();
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); history.replaceState(null, "", "/"); localStorage.clear(); sessionStorage.clear(); await i18n.changeLanguage("en"); });
it("requests recovery only after explicit valid input and gives non-enumerating acceptance", async () => {
  const user = userEvent.setup(); render(<Forgot />); await user.click(screen.getByRole("button", { name: "Request reset link" })); expect(staffRecoveryApi.forgot).not.toHaveBeenCalled(); await user.click(screen.getByRole("button", { name: "Enter a valid staff email address." })); expect(document.activeElement).toBe(screen.getByRole("textbox"));
  await user.type(screen.getByRole("textbox"), " Staff@example.test "); await user.click(screen.getByRole("button", { name: "Request reset link" })); await screen.findByRole("heading", { name: "Check your email" }); expect(staffRecoveryApi.forgot).toHaveBeenCalledExactlyOnceWith("Staff@example.test", expect.any(AbortSignal)); expect(screen.getByText(/If that address belongs to an active staff account/)).toBeTruthy(); expect(staffRecoveryApi.session).not.toHaveBeenCalled();
});
it.each([400, 429])("keeps a known recovery-request refusal %s explicit and preserves the address", async status => {
  vi.mocked(staffRecoveryApi.forgot).mockRejectedValueOnce(new HttpError("http", status)); const user = userEvent.setup(); render(<Forgot />); await user.type(screen.getByRole("textbox"), "staff@example.test"); await user.click(screen.getByRole("button", { name: "Request reset link" })); await screen.findByRole("alert"); expect(screen.getByRole("textbox")).toHaveProperty("value", "staff@example.test"); expect(staffRecoveryApi.forgot).toHaveBeenCalledTimes(1);
});
it("blocks another request on an uncertain page without claiming delivery or account existence", async () => {
  vi.mocked(staffRecoveryApi.forgot).mockRejectedValue(new HttpError("network", null)); const user = userEvent.setup(); render(<Forgot />); await user.type(screen.getByRole("textbox"), "staff@example.test"); await user.click(screen.getByRole("button", { name: "Request reset link" })); await screen.findByText(/This page will not send another email request/); expect(screen.queryByRole("form")).toBeNull(); expect(staffRecoveryApi.forgot).toHaveBeenCalledTimes(1);
});
it.each(["forgot", "reset"] as const)("routes public %s before staff or operator gates and scrubs credentials before reads", async kind => {
  history.replaceState(null, "", `/${kind === "forgot" ? "forgot" : "reset"}-password?token=${token}&returnTo=https://foreign.test#private`); const entry = captureStaffRecoveryEntry(), me = vi.spyOn(api, "me"), stores = vi.spyOn(api, "stores");
  vi.mocked(staffRecoveryApi.session).mockImplementation(async () => { expect(location.search + location.hash).toBe(""); return null; }); render(<App recoveryEntry={entry} />); await screen.findByRole("heading", { name: kind === "forgot" ? "Recover staff access" : "Choose a new staff password" }); expect(me).not.toHaveBeenCalled(); expect(stores).not.toHaveBeenCalled(); expect(staffRecoveryApi.reset).not.toHaveBeenCalled();
});
it("closes missing proof without calling an API or exposing account facts", async () => {
  render(<Reset entry={null} />); await screen.findByRole("heading", { name: "Reset link unavailable" }); expect(staffRecoveryApi.session).not.toHaveBeenCalled(); expect(staffRecoveryApi.reset).not.toHaveBeenCalled();
});
it("resets explicitly with raw matching passwords, clears/re-hides them and never signs the user in", async () => {
  const login = vi.spyOn(api, "login"), user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); await user.click(screen.getByRole("button", { name: "Show passwords" })); await user.click(screen.getByRole("button", { name: "Reset staff password" }));
  await screen.findByRole("heading", { name: "Staff password reset" }); expect(staffRecoveryApi.reset).toHaveBeenCalledExactlyOnceWith(token, secret, expect.any(AbortSignal)); expect(entry.available()).toBe(false); expect(screen.queryByLabelText("New password")).toBeNull(); expect(login).not.toHaveBeenCalled(); expect(screen.getByText(/you will still need an authentication or recovery code/)).toBeTruthy(); expect(screen.getByRole("link", { name: "Continue to staff sign-in" }).getAttribute("href")).toBe("/products"); expect(document.body.innerHTML).not.toContain(token); expect(document.body.innerHTML).not.toContain(secret);
});
it.each([9, 129])("validates the actual %s-character password bound before claiming the proof", async length => {
  const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await screen.findByLabelText("New password"); for (const label of ["New password", "Repeat new password"]) fireEvent.change(screen.getByLabelText(label), { target: { value: "x".repeat(length) } }); await user.click(screen.getByRole("button", { name: "Reset staff password" }));
  await user.click(within(screen.getByRole("alert")).getByRole("button", { name: "Use a password between 10 and 128 characters." })); expect(document.activeElement).toBe(screen.getByLabelText("New password")); expect(staffRecoveryApi.reset).not.toHaveBeenCalled(); expect(entry.available()).toBe(true);
});
it.each([10, 128])("accepts the exact %s-character password boundary", async length => {
  const user = userEvent.setup(); render(<Reset entry={proof()} />); await fill(user, "x".repeat(length)); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByRole("heading", { name: "Staff password reset" }); expect(vi.mocked(staffRecoveryApi.reset).mock.calls[0][1]).toHaveLength(length);
});
it("requires exact confirmation and focuses its field without spending proof", async () => {
  const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); await user.type(screen.getByLabelText("Repeat new password"), "x"); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await user.click(within(screen.getByRole("alert")).getByRole("button", { name: "Repeat the exact same password." })); expect(document.activeElement).toBe(screen.getByLabelText("Repeat new password")); expect(entry.available()).toBe(true); expect(staffRecoveryApi.reset).not.toHaveBeenCalled();
});
it.each([400, 429])("allows known refusal %s to be retried only on the current page with fresh hidden passwords", async status => {
  vi.mocked(staffRecoveryApi.reset).mockRejectedValueOnce(new HttpError("http", status)); const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); await user.click(screen.getByRole("button", { name: "Show passwords" })); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByRole("alert");
  expect(screen.getByLabelText("New password")).toHaveProperty("value", ""); expect(screen.getByLabelText("New password")).toHaveProperty("type", "password"); expect(screen.getByLabelText("Repeat new password")).toHaveProperty("value", ""); expect(entry.available()).toBe(false); await fill(user, "another password"); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByRole("heading", { name: "Staff password reset" }); expect(staffRecoveryApi.reset).toHaveBeenCalledTimes(2);
});
it("does not restore a refused proof after the page unmounts", async () => {
  vi.mocked(staffRecoveryApi.reset).mockRejectedValueOnce(new HttpError("http", 429)); const user = userEvent.setup(), entry = proof(), view = render(<Reset entry={entry} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByRole("alert"); view.unmount(); render(<Reset entry={entry} />); await screen.findByRole("heading", { name: "Reset link unavailable" }); expect(staffRecoveryApi.reset).toHaveBeenCalledTimes(1);
});
it("closes a server-rejected used/expired link without claiming a successful reset", async () => {
  vi.mocked(staffRecoveryApi.reset).mockRejectedValue(new HttpError("http", 404)); const user = userEvent.setup(); render(<Reset entry={proof()} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByRole("heading", { name: "Reset link unavailable" }); expect(screen.queryByLabelText("New password")).toBeNull(); expect(screen.queryByRole("heading", { name: "Staff password reset" })).toBeNull();
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("http", 409), new HttpError("http", 401), new HttpError("invalid-response", 200)])("never replays an uncertain reset, including a remount with the same entry: %j", async error => {
  vi.mocked(staffRecoveryApi.reset).mockRejectedValue(error); const user = userEvent.setup(), entry = proof(), view = render(<Reset entry={entry} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByText(/This page will not submit this link again/); expect(screen.queryByRole("form")).toBeNull(); expect(staffRecoveryApi.reset).toHaveBeenCalledTimes(1); view.unmount(); render(<Reset entry={entry} />); await screen.findByRole("heading", { name: "Reset link unavailable" }); expect(staffRecoveryApi.reset).toHaveBeenCalledTimes(1);
});
it("requires explicit staff logout and observes guest before enabling password fields", async () => {
  vi.mocked(staffRecoveryApi.session).mockResolvedValue({ email: "other@example.test" }); const user = userEvent.setup(); render(<Reset entry={proof()} />); await screen.findByText(/Currently signed in as other@example.test/); expect(staffRecoveryApi.signOut).not.toHaveBeenCalled(); expect(screen.queryByLabelText("New password")).toBeNull(); await user.click(screen.getByRole("button", { name: "Sign out before resetting" })); await screen.findByText(/still has a staff session/); expect(screen.queryByLabelText("New password")).toBeNull();
  vi.mocked(staffRecoveryApi.session).mockResolvedValue(null); await user.click(screen.getByRole("button", { name: "Sign out before resetting" })); await screen.findByLabelText("New password"); expect(staffRecoveryApi.signOut).toHaveBeenCalledTimes(2); expect(staffRecoveryApi.reset).not.toHaveBeenCalled();
});
it("retries only GET after a failed logout-follow-up read", async () => {
  vi.mocked(staffRecoveryApi.session).mockResolvedValueOnce({ email: "other@example.test" }).mockRejectedValueOnce(new HttpError("network", null)).mockResolvedValue(null); const user = userEvent.setup(); render(<Reset entry={proof()} />); await user.click(await screen.findByRole("button", { name: "Sign out before resetting" })); await user.click(await screen.findByRole("button", { name: "Check staff sign-in" })); await screen.findByLabelText("New password"); expect(staffRecoveryApi.signOut).toHaveBeenCalledTimes(1);
});
it("detects another-tab staff login before resetting without spending proof or keeping passwords", async () => {
  const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); vi.mocked(staffRecoveryApi.session).mockResolvedValue({ email: "other@example.test" }); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByText(/Currently signed in as other@example.test/); expect(staffRecoveryApi.reset).not.toHaveBeenCalled(); expect(entry.available()).toBe(true); expect(screen.queryByLabelText("New password")).toBeNull();
});
it("keeps failed preflight distinct from a submitted reset and permits GET-only recovery", async () => {
  const user = userEvent.setup(), entry = proof(); render(<Reset entry={entry} />); await fill(user); vi.mocked(staffRecoveryApi.session).mockRejectedValueOnce(new HttpError("network", null)); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByText(/No password reset was submitted after this failed check/); expect(staffRecoveryApi.reset).not.toHaveBeenCalled(); expect(entry.available()).toBe(true); await user.click(screen.getByRole("button", { name: "Check staff sign-in" })); expect(await screen.findByLabelText("New password")).toHaveProperty("value", "");
});
it("locks duplicate submissions, keeps a pending form safely blank and discards late reset answers on departure", async () => {
  const held = deferred<void>(); vi.mocked(staffRecoveryApi.reset).mockReturnValue(held.promise); const user = userEvent.setup(), entry = proof(), view = render(<Reset entry={entry} />); await fill(user); fireEvent.submit(screen.getByRole("form")); fireEvent.submit(screen.getByRole("form")); await waitFor(() => expect(staffRecoveryApi.reset).toHaveBeenCalledTimes(1)); expect(screen.getByLabelText("New password")).toHaveProperty("value", ""); expect(screen.getByLabelText("New password")).toHaveProperty("disabled", true); view.unmount(); expect(vi.mocked(staffRecoveryApi.reset).mock.calls[0][2].aborted).toBe(true); await act(async () => held.resolve()); render(<Reset entry={entry} />); await screen.findByRole("heading", { name: "Reset link unavailable" });
});
it("scopes a new proof to a fresh form and discards old in-flight work", async () => {
  const held = deferred<void>(); vi.mocked(staffRecoveryApi.reset).mockReturnValue(held.promise); const user = userEvent.setup(), view = render(<Reset entry={proof()} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await waitFor(() => expect(staffRecoveryApi.reset).toHaveBeenCalledTimes(1)); view.rerender(<Reset entry={proof()} />); expect(vi.mocked(staffRecoveryApi.reset).mock.calls[0][2].aborted).toBe(true); await act(async () => held.resolve()); expect(await screen.findByLabelText("New password")).toHaveProperty("value", ""); expect(screen.queryByRole("heading", { name: "Staff password reset" })).toBeNull();
});
it("does not leak echoed server credentials, references, confirmation or URL destinations", async () => {
  vi.mocked(staffRecoveryApi.reset).mockRejectedValue(new HttpError("http", 503, { detail: token, traceId: token, errors: { [token]: [secret] } }, token)); const user = userEvent.setup(); render(<Reset entry={proof()} />); await fill(user); await user.click(screen.getByRole("button", { name: "Reset staff password" })); await screen.findByText(/This page will not submit this link again/); expect(document.body.innerHTML).not.toContain(token); expect(document.body.innerHTML).not.toContain(secret); expect(Object.values({ ...localStorage, ...sessionStorage }).join(" ")).not.toContain(token); expect(Object.values({ ...localStorage, ...sessionStorage }).join(" ")).not.toContain(secret);
});
it("preserves local valid secrets on a language change without replaying any request", async () => {
  const user = userEvent.setup(); render(<Reset entry={proof()} />); await fill(user); const reads = vi.mocked(staffRecoveryApi.session).mock.calls.length; await act(async () => { await i18n.changeLanguage("cs"); }); expect(screen.getByLabelText(i18n.t("staffRecovery:password"))).toHaveProperty("value", secret); expect(staffRecoveryApi.session).toHaveBeenCalledTimes(reads); expect(staffRecoveryApi.reset).not.toHaveBeenCalled();
});
it("completes StrictMode reads without spending proof or resetting automatically", async () => { render(<StrictMode><Reset entry={proof()} /></StrictMode>); await screen.findByLabelText("New password"); expect(staffRecoveryApi.reset).not.toHaveBeenCalled(); });
it("does not start another session read after departing during logout", async () => {
  const held = deferred<void>(); vi.mocked(staffRecoveryApi.session).mockResolvedValue({ email: "other@example.test" }); vi.mocked(staffRecoveryApi.signOut).mockReturnValue(held.promise); const user = userEvent.setup(), view = render(<Reset entry={proof()} />); await user.click(await screen.findByRole("button", { name: "Sign out before resetting" })); const reads = vi.mocked(staffRecoveryApi.session).mock.calls.length; view.unmount(); expect(vi.mocked(staffRecoveryApi.signOut).mock.calls[0][0].aborted).toBe(true); await act(async () => held.resolve()); expect(staffRecoveryApi.session).toHaveBeenCalledTimes(reads);
});
