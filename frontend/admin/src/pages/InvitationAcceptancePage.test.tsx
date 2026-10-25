// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { HttpError } from "../api/http";
import { invitationsApi } from "../api/invitations";
import { initializeI18n, i18n } from "../i18n";
import { captureInvitationEntry } from "../invitationEntry";
import { InvitationAcceptancePage } from "./InvitationAcceptancePage";

const credential = ["fixture", "invitation", "credential"].join("-");
const secret = ["chosen", "password", "value"].join("-");
const invitation = { email: "colleague@example.test", role: "CatalogManager" as const };
function page(token: string | null = credential) { return render(<StrictMode><InvitationAcceptancePage entry={{ token }} /></StrictMode>); }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
async function fill(user: ReturnType<typeof userEvent.setup>, value = secret) {
  await user.type(await screen.findByLabelText(i18n.t("invitations:password")), value);
  await user.type(screen.getByLabelText(i18n.t("invitations:confirmation")), value);
}
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  vi.spyOn(invitationsApi, "details").mockResolvedValue(invitation); vi.spyOn(invitationsApi, "session").mockResolvedValue(null);
  vi.spyOn(invitationsApi, "signOut").mockResolvedValue(undefined); vi.spyOn(invitationsApi, "accept").mockResolvedValue({ email: invitation.email });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); });
describe("public staff invitation entry", () => {
  it.each(["en", "cs"])("accepts explicitly without signing in, clears passwords and supplies a fixed destination in %s", async language => {
    await i18n.changeLanguage(language); const user = userEvent.setup(); page(); await fill(user);
    expect(document.activeElement).toBe(screen.getByLabelText(i18n.t("invitations:confirmation")));
    await user.click(screen.getByRole("button", { name: i18n.t("invitations:accept") })); await screen.findByRole("heading", { name: i18n.t("invitations:acceptedTitle") });
    expect(invitationsApi.accept).toHaveBeenCalledExactlyOnceWith(credential, secret, invitation, expect.any(AbortSignal));
    expect(document.querySelector('input[name="password"]')).toBeNull(); expect(screen.getByRole("link", { name: i18n.t("auth:signIn") }).getAttribute("href")).toBe("/products");
    expect(invitationsApi.session).toHaveBeenCalledTimes(3); expect(invitationsApi.signOut).not.toHaveBeenCalled(); expect(document.body.textContent).not.toContain(credential); expect(document.body.textContent).not.toContain(secret);
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { level: 1 })));
  });
  it("runs before the merchant session gate and scrubs token/return destination before any read", async () => {
    window.history.replaceState(null, "", "/invitations/accept?token=" + credential + "&returnTo=https://foreign.test/#other");
    const entry = captureInvitationEntry(), me = vi.spyOn(api, "me"), login = vi.spyOn(api, "login");
    vi.mocked(invitationsApi.details).mockImplementation(async () => { expect(window.location.search + window.location.hash).toBe(""); return invitation; });
    render(<App invitationEntry={entry} />); await screen.findByText(invitation.email); expect(me).not.toHaveBeenCalled(); expect(login).not.toHaveBeenCalled();
  });
  it("does not call an endpoint for missing/duplicate captured tokens", async () => {
    page(null); await screen.findByText(i18n.t("invitations:closed")); expect(invitationsApi.details).not.toHaveBeenCalled(); expect(invitationsApi.session).not.toHaveBeenCalled(); expect(invitationsApi.accept).not.toHaveBeenCalled();
  });
  it.each([9, 129])("validates the actual password bounds (%i) and focuses the summary/field", async length => {
    const user = userEvent.setup(); page(); await screen.findByLabelText("Choose a password");
    fireEvent.change(screen.getByLabelText("Choose a password"), { target: { value: "x".repeat(length) } }); fireEvent.change(screen.getByLabelText("Repeat the password"), { target: { value: "x".repeat(length) } });
    await user.click(screen.getByRole("button", { name: "Accept invitation" })); await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("alert")));
    await user.click(within(screen.getByRole("alert")).getByRole("button")); expect(document.activeElement).toBe(screen.getByLabelText("Choose a password")); expect(invitationsApi.accept).not.toHaveBeenCalled();
  });
  it("checks exact confirmation and preserves literal whitespace within valid password bounds", async () => {
    const user = userEvent.setup(); page(); await fill(user, "  password  "); await user.type(screen.getByLabelText("Repeat the password"), "x");
    await user.click(screen.getByRole("button", { name: "Accept invitation" })); expect(invitationsApi.accept).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("Repeat the password"), { target: { value: "  password  " } }); await user.click(screen.getByRole("button", { name: "Accept invitation" }));
    await screen.findByRole("heading", { name: "Invitation accepted" }); expect(vi.mocked(invitationsApi.accept).mock.calls[0][1]).toBe("  password  ");
  });
  it.each([10, 128])("accepts the exact %i-character password boundary", async length => {
    const user = userEvent.setup(); page(); await screen.findByLabelText("Choose a password");
    for (const label of ["Choose a password", "Repeat the password"]) fireEvent.change(screen.getByLabelText(label), { target: { value: "x".repeat(length) } });
    await user.click(screen.getByRole("button", { name: "Accept invitation" })); await screen.findByRole("heading", { name: "Invitation accepted" });
    expect(vi.mocked(invitationsApi.accept).mock.calls[0][1]).toHaveLength(length);
  });
  it("requires explicit staff sign-out and verifies the cookie before enabling passwords", async () => {
    vi.mocked(invitationsApi.session).mockResolvedValue({ email: "other@example.test" }); const user = userEvent.setup(); page();
    await screen.findByText(/other@example.test/); expect(screen.queryByLabelText("Choose a password")).toBeNull(); expect(invitationsApi.signOut).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Sign out before accepting" })); await screen.findByText(i18n.t("invitations:signOutFailed")); expect(screen.queryByLabelText("Choose a password")).toBeNull();
    vi.mocked(invitationsApi.session).mockResolvedValue(null); await user.click(screen.getByRole("button", { name: "Sign out before accepting" }));
    await screen.findByLabelText("Choose a password"); expect(invitationsApi.signOut).toHaveBeenCalledTimes(2); expect(invitationsApi.accept).not.toHaveBeenCalled();
  });
  it("rechecks session before accepting and drops passwords if another tab signed in", async () => {
    const user = userEvent.setup(); page(); await fill(user);
    vi.mocked(invitationsApi.session).mockResolvedValue({ email: "new-session@example.test" });
    await user.click(screen.getByRole("button", { name: "Accept invitation" })); await screen.findByText(/new-session@example.test/);
    expect(screen.queryByLabelText("Choose a password")).toBeNull(); expect(invitationsApi.accept).not.toHaveBeenCalled(); expect(invitationsApi.signOut).not.toHaveBeenCalled();
  });
  it("blocks acceptance after a failed pre-submit session check", async () => {
    const user = userEvent.setup(); page(); await fill(user); vi.mocked(invitationsApi.session).mockRejectedValueOnce(new HttpError("http", 503));
    await user.click(screen.getByRole("button", { name: "Accept invitation" })); await screen.findByText(i18n.t("invitations:readFailed"));
    expect(invitationsApi.accept).not.toHaveBeenCalled(); expect(document.querySelector('input[type="password"]')).toBeNull();
  });
  it.each([400, 429])("keeps a refused %i acceptance explicit and requires freshly typed passwords", async code => {
    const user = userEvent.setup(); page(); await fill(user); vi.mocked(invitationsApi.accept).mockRejectedValueOnce(new HttpError("http", code));
    await user.click(screen.getByRole("button", { name: "Accept invitation" })); await screen.findByRole("alert");
    expect(screen.getByLabelText("Choose a password")).toHaveProperty("value", ""); expect(screen.getByLabelText("Repeat the password")).toHaveProperty("value", "");
    expect(invitationsApi.accept).toHaveBeenCalledOnce(); await fill(user); await user.click(screen.getByRole("button", { name: "Accept invitation" })); await screen.findByRole("heading", { name: "Invitation accepted" }); expect(invitationsApi.accept).toHaveBeenCalledTimes(2);
  });
  it.each([404, 409])("does not claim acceptance for a terminal %i response", async code => {
    const user = userEvent.setup(); page(); await fill(user); vi.mocked(invitationsApi.accept).mockRejectedValueOnce(new HttpError("http", code));
    await user.click(screen.getByRole("button", { name: "Accept invitation" })); await screen.findByText(i18n.t(code === 404 ? "invitations:closed" : "invitations:conflict"));
    expect(screen.queryByRole("heading", { name: "Invitation accepted" })).toBeNull(); expect(screen.queryByLabelText("Choose a password")).toBeNull(); expect(invitationsApi.accept).toHaveBeenCalledOnce();
  });
  it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 200)])("locks unconfirmed acceptance even when a later lookup still shows open: %j", async error => {
    const user = userEvent.setup(); page(); await fill(user); vi.mocked(invitationsApi.accept).mockRejectedValueOnce(error);
    await user.click(screen.getByRole("button", { name: "Accept invitation" })); await screen.findByText(i18n.t("invitations:uncertain"));
    await user.click(screen.getByRole("button", { name: "Check invitation again" })); await screen.findByText(i18n.t("invitations:uncertainOpen"));
    expect(screen.queryByRole("button", { name: "Accept invitation" })).toBeNull(); expect(invitationsApi.accept).toHaveBeenCalledOnce();
    vi.mocked(invitationsApi.details).mockRejectedValueOnce(new HttpError("http", 404)); await user.click(screen.getByRole("button", { name: "Check invitation again" })); await screen.findByText(i18n.t("invitations:closedUnconfirmed"));
    expect(screen.queryByRole("heading", { name: "Invitation accepted" })).toBeNull();
  });
  it("serializes immediate submits, aborts departure and ignores late acceptance", async () => {
    const user = userEvent.setup(), held = deferred<{ email: string }>(); const view = page(); await fill(user); vi.mocked(invitationsApi.accept).mockReturnValueOnce(held.promise);
    const form = screen.getByRole("form"); act(() => { fireEvent.submit(form); fireEvent.submit(form); });
    await waitFor(() => expect(invitationsApi.accept).toHaveBeenCalledOnce()); expect(screen.getByLabelText("Choose a password")).toHaveProperty("value", "");
    const signal = vi.mocked(invitationsApi.accept).mock.calls[0][3]; view.unmount(); expect(signal?.aborted).toBe(true); await act(async () => held.resolve({ email: invitation.email }));
    expect(screen.queryByText("Invitation accepted")).toBeNull();
  });
  it("retries failed reads only and never prints server echoes or trace credentials", async () => {
    vi.mocked(invitationsApi.details).mockRejectedValue(new HttpError("http", 503, { detail: credential, traceId: credential, errors: { [credential]: [secret] } }));
    const user = userEvent.setup(); page(); await screen.findByText(i18n.t("invitations:readFailed")); expect(document.body.textContent).not.toContain(credential); expect(document.body.textContent).not.toContain(secret);
    vi.mocked(invitationsApi.details).mockResolvedValue(invitation); await user.click(screen.getByRole("button", { name: "Check invitation again" })); await screen.findByLabelText("Choose a password"); expect(invitationsApi.accept).not.toHaveBeenCalled();
  });
});
