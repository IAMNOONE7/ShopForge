// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import App from "../App";
import { api } from "../api";
import { HttpError, onUnauthorized } from "../api/http";
import { platformApi, type PlatformUser } from "../api/platform";
import { i18n, initializeI18n } from "../i18n";
import PlatformApp from "./PlatformApp";

const operator: PlatformUser = { id: "00000000-0000-0000-0000-000000000001", email: "operator@example.test", isTwoFactorEnabled: true };
function app(path = "/platform/tenants") { return <MemoryRouter initialEntries={[path]}><Routes><Route path="platform/*" element={<PlatformApp />} /></Routes></MemoryRouter>; }
function deferred<T>() { let resolve!: (value: T) => void, reject!: (reason: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
async function credentials(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByRole("textbox", { name: /^E-mail/ }), operator.email);
  await user.type(screen.getByLabelText(/^Password/), "private-password");
  return screen.getByLabelText(/^Password/).closest("form")!;
}
beforeAll(async () => { await initializeI18n(); });
beforeEach(async () => {
  await i18n.changeLanguage("en"); window.history.replaceState(null, "", "/platform/tenants");
  vi.spyOn(platformApi, "me").mockResolvedValue(operator); vi.spyOn(platformApi, "tenants").mockResolvedValue([]);
  vi.spyOn(platformApi, "logout").mockResolvedValue(); vi.spyOn(platformApi, "login").mockResolvedValue({ twoFactorRequired: false, ticket: null, user: operator });
  vi.spyOn(platformApi, "completeTwoFactor").mockResolvedValue({ twoFactorRequired: false, ticket: null, user: operator });
  vi.spyOn(api, "me"); vi.spyOn(api, "stores"); vi.spyOn(api, "logout");
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); window.history.replaceState(null, "", "/"); });

it("boots the actual operator entry without mounting merchant session or store reads", async () => {
  render(<App />); await screen.findByText("No companies were returned.");
  expect(platformApi.me).toHaveBeenCalledOnce(); expect(platformApi.tenants).toHaveBeenCalledOnce();
  expect(api.me).not.toHaveBeenCalled(); expect(api.stores).not.toHaveBeenCalled(); expect(api.logout).not.toHaveBeenCalled();
  expect(screen.getByRole("link", { name: "Company administration" }).getAttribute("href")).toBe("/products");
});
it("redirects the operator root and keeps unknown operator routes inside its own shell", async () => {
  const view = render(app("/platform")); await screen.findByRole("heading", { name: "Companies" }); view.unmount();
  render(app("/platform/missing")); await screen.findByRole("heading", { name: "Operator page not found" });
  expect(api.me).not.toHaveBeenCalled();
});
it("keeps an anonymous deep link gated until its checked platform sign-in succeeds", async () => {
  vi.mocked(platformApi.me).mockRejectedValue(new HttpError("http", 401)); const user = userEvent.setup(); render(app());
  const form = await credentials(user); expect(platformApi.tenants).not.toHaveBeenCalled(); fireEvent.submit(form);
  await screen.findByRole("heading", { name: "Companies" }); expect(platformApi.login).toHaveBeenCalledExactlyOnceWith(operator.email, "private-password");
});
it("keeps the challenge unauthenticated, clears submitted secrets, locks duplicate submits and accepts a recovery code", async () => {
  vi.mocked(platformApi.me).mockRejectedValue(new HttpError("http", 401));
  const held = deferred<Awaited<ReturnType<typeof platformApi.login>>>(); vi.mocked(platformApi.login).mockReturnValue(held.promise);
  const user = userEvent.setup(); render(app()); const form = await credentials(user); fireEvent.submit(form); fireEvent.submit(form);
  expect(platformApi.login).toHaveBeenCalledOnce(); expect(screen.getByLabelText(/^Password/)).toHaveProperty("value", "");
  await act(async () => held.resolve({ twoFactorRequired: true, ticket: "opaque-private-ticket", user: null }));
  const code = await screen.findByRole("textbox", { name: "Authentication or recovery code" }); expect(code).toBe(document.activeElement);
  expect(platformApi.tenants).not.toHaveBeenCalled(); await user.type(code, "recovery-with-letters"); fireEvent.submit(code.closest("form")!);
  await screen.findByRole("heading", { name: "Companies" }); expect(platformApi.completeTwoFactor).toHaveBeenCalledExactlyOnceWith("opaque-private-ticket", "recovery-with-letters");
  expect(document.body.textContent).not.toMatch(/opaque-private-ticket|private-password|recovery-with-letters/);
  expect(JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage }, history: window.location.href })).not.toMatch(/opaque-private-ticket|private-password|recovery-with-letters/);
});
it("clears a refused code, allows a new challenge and never replays credentials automatically", async () => {
  vi.mocked(platformApi.me).mockRejectedValue(new HttpError("http", 401)); vi.mocked(platformApi.login).mockResolvedValue({ twoFactorRequired: true, ticket: "ticket", user: null });
  vi.mocked(platformApi.completeTwoFactor).mockRejectedValue(new HttpError("http", 401)); const user = userEvent.setup(); render(app());
  fireEvent.submit(await credentials(user)); const code = await screen.findByRole("textbox", { name: "Authentication or recovery code" });
  await user.type(code, "wrong-code"); fireEvent.submit(code.closest("form")!); await screen.findByText(/The code was not accepted/);
  expect(code).toHaveProperty("value", ""); expect(platformApi.tenants).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Start sign-in again" }));
  expect(screen.getByRole("textbox", { name: /^E-mail/ })).toBe(document.activeElement); expect(screen.getByLabelText(/^Password/)).toHaveProperty("value", "");
  expect(platformApi.login).toHaveBeenCalledOnce(); expect(platformApi.completeTwoFactor).toHaveBeenCalledOnce();
});
it.each([403, 503])("keeps an unavailable/restricted session %s gated and retries only its read", async status => {
  vi.mocked(platformApi.me).mockRejectedValueOnce(new HttpError("http", status)); const user = userEvent.setup(); render(app());
  await screen.findByRole("heading", { name: "Operator session unavailable" }); expect(platformApi.tenants).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Try again" })); await screen.findByRole("heading", { name: "Companies" });
  expect(platformApi.login).not.toHaveBeenCalled(); expect(platformApi.me).toHaveBeenCalledTimes(2);
});
it("offers a read-only session check after a lost sign-in response", async () => {
  vi.mocked(platformApi.me).mockRejectedValueOnce(new HttpError("http", 401)); vi.mocked(platformApi.login).mockRejectedValue(new HttpError("network", null));
  const user = userEvent.setup(); render(app()); fireEvent.submit(await credentials(user)); await screen.findByRole("button", { name: "Check operator session" });
  await user.click(screen.getByRole("button", { name: "Check operator session" })); await screen.findByRole("heading", { name: "Companies" }); expect(platformApi.login).toHaveBeenCalledOnce();
});
it("expires only the operator session on a protected platform 401", async () => {
  vi.mocked(platformApi.tenants).mockRestore(); const merchantExpired = vi.fn(), off = onUnauthorized(merchantExpired);
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("", { status: 401 })));
  try { render(app()); await screen.findByText("Your operator session ended. Sign in again to continue."); expect(merchantExpired).not.toHaveBeenCalled(); expect(api.me).not.toHaveBeenCalled(); }
  finally { off(); vi.unstubAllGlobals(); }
});
it("does not notify the platform shell for a merchant request's unauthorized event", async () => {
  render(app()); await screen.findByRole("heading", { name: "Companies" }); vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("", { status: 401 })));
  const { requestJson } = await import("../api/http");
  try { await act(async () => { await requestJson("/api/admin/test").catch(() => undefined); }); expect(screen.getByRole("heading", { name: "Companies" })).toBeTruthy(); }
  finally { vi.unstubAllGlobals(); }
});
it("locks duplicate sign-out, pauses changes on a lost reply and recovers with a session read", async () => {
  vi.mocked(platformApi.tenants).mockResolvedValue([{ id: operator.id, name: "Company A", status: "Active", planCode: "basic", usage: [] }]);
  const held = deferred<void>(); vi.mocked(platformApi.logout).mockReturnValueOnce(held.promise); const user = userEvent.setup(); render(app());
  const suspend = await screen.findByRole("button", { name: "Suspend access for Company A" }); const signOut = screen.getByRole("button", { name: "Sign out" });
  fireEvent.click(signOut); fireEvent.click(signOut); expect(platformApi.logout).toHaveBeenCalledOnce(); expect(suspend).toHaveProperty("disabled", true);
  await act(async () => held.reject(new HttpError("network", null))); await screen.findByText(/Sign-out was not confirmed/); expect(suspend).toHaveProperty("disabled", true);
  await user.click(screen.getByRole("button", { name: "Check operator session" })); await waitFor(() => expect(screen.getByRole("button", { name: "Suspend access for Company A" })).toHaveProperty("disabled", false));
  expect(api.logout).not.toHaveBeenCalled(); expect(platformApi.logout).toHaveBeenCalledOnce();
});
it("signs out only the operator and gates the previous protected page", async () => {
  const user = userEvent.setup(); render(app()); await user.click(await screen.findByRole("button", { name: "Sign out" }));
  await screen.findByRole("heading", { name: "Operator sign-in" }); expect(screen.queryByRole("heading", { name: "Companies" })).toBeNull(); expect(api.logout).not.toHaveBeenCalled();
});
it("ignores a departed operator's delayed session read", async () => {
  const held = deferred<PlatformUser>(); vi.mocked(platformApi.me).mockReturnValueOnce(held.promise); const view = render(app()); view.unmount();
  render(app()); await screen.findByRole("heading", { name: "Companies" }); await act(async () => held.resolve({ ...operator, email: "departed@example.test" }));
  expect(screen.queryByText("departed@example.test")).toBeNull();
});
