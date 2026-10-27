// @vitest-environment jsdom
import { StrictMode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, beforeEach, expect, it, vi } from "vitest";
import type { CurrentUser } from "../api";
import { colleaguesApi, type Colleagues } from "../api/colleagues";
import { HttpError } from "../api/http";
import { SessionContext } from "../session";
import { i18n, initializeI18n } from "../i18n";
import { colleagueTeam, colleagueOwner, colleagueAdmin } from "../test/colleagueFixtures";
import { ColleaguesPage } from "./ColleaguesPage";
let actor: CurrentUser, rows: Colleagues;
function Shop({ user = colleagueOwner }: { user?: CurrentUser }) {
  return <SessionContext value={{ user, logout: async () => undefined, logoutPending: false, logoutError: null }}><MemoryRouter initialEntries={["/users"]}><Link to="/products">Leave team</Link><Routes><Route path="/users" element={<ColleaguesPage />} /><Route path="/products" element={<h1>Products destination</h1>} /></Routes></MemoryRouter></SessionContext>;
}
const editor = () => within(screen.getByRole("region", { name: /Invite a colleague|Change colleague role|Withdraw invitation|Deactivate colleague|Activate colleague/ }));
async function invite(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "Invite a colleague" })); await user.type(editor().getByRole("textbox", { name: "E-mail address" }), " New@example.test "); await user.selectOptions(editor().getByRole("combobox"), "Support");
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(yes => { resolve = yes; }); return { promise, resolve }; }
beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en"); actor = structuredClone(colleagueOwner); rows = structuredClone(colleagueTeam);
  vi.spyOn(colleaguesApi, "session").mockImplementation(async () => structuredClone(actor)); vi.spyOn(colleaguesApi, "list").mockImplementation(async () => structuredClone(rows));
  vi.spyOn(colleaguesApi, "invite").mockImplementation(async (email, role) => { const row = { ...colleagueTeam.invitations[0], id: "new-invitation", email: email.trim().toLowerCase(), role }; rows.invitations = [...rows.invitations.filter(item => item.email !== row.email), row]; return structuredClone(row); });
  vi.spyOn(colleaguesApi, "role").mockImplementation(async (before, role) => { const row = { ...before, role }; rows.users = rows.users.map(item => item.id === row.id ? row : item); return structuredClone(row); });
  vi.spyOn(colleaguesApi, "activity").mockImplementation(async before => { const row = { ...before, isActive: !before.isActive }; rows.users = rows.users.map(item => item.id === row.id ? row : item); return structuredClone(row); });
  vi.spyOn(colleaguesApi, "withdraw").mockImplementation(async id => { rows.invitations = rows.invitations.filter(row => row.id !== id); });
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.stubGlobal("CSS", { escape: (value: string) => value });
});
afterEach(async () => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); await i18n.changeLanguage("en"); });
it("shows company-wide active/inactive people and actual invitation dates without creating access or fetching proof links", async () => {
  render(<Shop />); await screen.findByRole("heading", { name: "Colleagues (5)" }); expect(screen.getByText(/applies across all stores/)).toBeTruthy(); expect(screen.getByRole("heading", { name: "Pending invitations (2)" })).toBeTruthy(); expect(screen.getByText("Inactive", { exact: false })).toBeTruthy(); expect(document.querySelector("time")?.dateTime).toBe(colleagueTeam.invitations[0].expiresAt); expect(document.querySelector('a[href*="token="]')).toBeNull(); expect(colleaguesApi.invite).not.toHaveBeenCalled();
});
it.each(["CatalogManager", "OrderManager", "Warehouse", "Support", "FutureRole"])("does not read or offer company management to %s", async role => {
  render(<Shop user={{ ...colleagueOwner, role }} />); await screen.findByText(i18n.t("errors:permissionTitle")); expect(colleaguesApi.list).not.toHaveBeenCalled(); expect(colleaguesApi.session).not.toHaveBeenCalled();
});
it("validates explicit invitation email/role locally and focuses each issue", async () => {
  const user = userEvent.setup(); render(<Shop />); await user.click(await screen.findByRole("button", { name: "Invite a colleague" })); await user.click(editor().getByRole("button", { name: "Create invitation" }));
  await user.click(screen.getByRole("button", { name: "Enter a valid e-mail address." })); expect(document.activeElement).toBe(editor().getByRole("textbox")); await user.click(screen.getByRole("button", { name: "Choose an available role." })); expect(document.activeElement).toBe(editor().getByRole("combobox")); expect(colleaguesApi.invite).not.toHaveBeenCalled();
});
it("creates an invitation once on explicit submit, without declaring delivery or active membership", async () => {
  const user = userEvent.setup(); render(<Shop />); await invite(user); expect(colleaguesApi.invite).not.toHaveBeenCalled(); await user.click(editor().getByRole("button", { name: "Create invitation" }));
  await screen.findByText("Invitation created for new@example.test. Acceptance and email delivery are separate."); expect(colleaguesApi.invite).toHaveBeenCalledExactlyOnceWith("New@example.test", "Support", expect.any(AbortSignal)); expect(screen.getByRole("heading", { name: "Colleagues (5)" })).toBeTruthy();
});
it("requires explicit replacement acknowledgment even when a new invitation is typed for a pending address", async () => {
  const user = userEvent.setup(); render(<Shop />); await invite(user); await user.clear(editor().getByRole("textbox")); await user.type(editor().getByRole("textbox"), "pending@example.test"); await user.click(editor().getByRole("button", { name: "Replace this invitation" }));
  await user.click(screen.getByRole("button", { name: "Confirm replacement of the existing invitation." })); expect(document.activeElement).toBe(editor().getByRole("checkbox")); expect(colleaguesApi.invite).not.toHaveBeenCalled(); await user.click(editor().getByRole("checkbox")); await user.click(editor().getByRole("button", { name: "Replace this invitation" })); await screen.findByText(/Invitation created for pending@example.test/); expect(rows.invitations.filter(row => row.email === "pending@example.test")).toHaveLength(1);
});
it("lets an admin withdraw an owner invitation while withholding owner assignment and existing-owner controls", async () => {
  actor = structuredClone(colleagueAdmin); const user = userEvent.setup(); render(<Shop user={colleagueAdmin} />); await screen.findByRole("heading", { name: "Colleagues (5)" }); expect(screen.queryByRole("button", { name: "Change role for owner@example.test" })).toBeNull(); expect(screen.queryByRole("button", { name: "Deactivate other-owner@example.test" })).toBeNull();
  await user.click(screen.getByRole("button", { name: "Invite a colleague" })); expect(editor().queryByRole("option", { name: "Owner" })).toBeNull(); await user.click(editor().getByRole("button", { name: "Cancel change" }));
  await user.click(screen.getByRole("button", { name: "Withdraw invitation for pending-owner@example.test" })); await user.click(editor().getByRole("button", { name: "Confirm change" })); await screen.findByText("Invitation withdrawn for pending-owner@example.test."); expect(colleaguesApi.withdraw).toHaveBeenCalledTimes(1);
});
it("prevents self role/activity changes and permits an owner to change another owner explicitly", async () => {
  const user = userEvent.setup(); render(<Shop />); await screen.findByRole("heading", { name: "Colleagues (5)" }); expect(screen.queryByRole("button", { name: "Deactivate owner@example.test" })).toBeNull(); expect(screen.queryByRole("button", { name: "Change role for owner@example.test" })).toBeNull();
  await user.click(screen.getByRole("button", { name: "Change role for other-owner@example.test" })); await user.selectOptions(editor().getByRole("combobox"), "Support"); await user.click(editor().getByRole("button", { name: "Confirm change" })); await screen.findByText("Role changed for other-owner@example.test."); expect(colleaguesApi.role).toHaveBeenCalledWith(colleagueTeam.users[4], "Support", expect.any(AbortSignal));
});
it.each([true, false])("confirms and applies activity change for initially active=%s without deleting a person", async active => {
  const user = userEvent.setup(), target = active ? colleagueTeam.users[2] : colleagueTeam.users[3]; render(<Shop />); await user.click(await screen.findByRole("button", { name: `${active ? "Deactivate" : "Activate"} ${target.email}` })); expect(colleaguesApi.activity).not.toHaveBeenCalled(); await user.click(editor().getByRole("button", { name: "Confirm change" })); await screen.findByText(`Colleague ${active ? "deactivated" : "activated"}: ${target.email}.`); expect(rows.users).toHaveLength(5);
});
it("uses Escape to discard changes and restore the connected opener", async () => {
  const user = userEvent.setup(); render(<Shop />); const trigger = await screen.findByRole("button", { name: "Change role for support@example.test" }); await user.click(trigger); await user.selectOptions(editor().getByRole("combobox"), "Warehouse"); await user.keyboard("{Escape}"); await waitFor(() => expect(document.activeElement).toBe(trigger)); expect(colleaguesApi.role).not.toHaveBeenCalled();
});
it.each([400, 404, 409, 429])("retains invitation drafts after known refusal %s without replay", async status => {
  vi.mocked(colleaguesApi.invite).mockRejectedValue(new HttpError("http", status)); const user = userEvent.setup(); render(<Shop />); await invite(user); await user.click(editor().getByRole("button", { name: "Create invitation" })); await screen.findByRole("alert"); expect(editor().getByRole("textbox")).toHaveProperty("value", "New@example.test"); expect(editor().getByRole("combobox")).toHaveProperty("value", "Support"); expect(colleaguesApi.invite).toHaveBeenCalledTimes(1);
});
it.each([new HttpError("network", null), new HttpError("http", 503), new HttpError("invalid-response", 200)])("hides old records after an uncertain invitation and recovers with GET only: %j", async error => {
  vi.mocked(colleaguesApi.invite).mockRejectedValue(error); const user = userEvent.setup(); render(<Shop />); await invite(user); await user.click(editor().getByRole("button", { name: "Create invitation" })); await screen.findByText(/The change may have completed/); expect(screen.queryByRole("heading", { name: "Colleagues (5)" })).toBeNull(); expect(screen.queryByRole("textbox")).toBeNull(); await user.click(screen.getByRole("button", { name: "Review current company records" })); await screen.findByText(/This read does not confirm what happened/); expect(colleaguesApi.invite).toHaveBeenCalledTimes(1);
});
it("keeps failed uncertain recovery read-only until a fresh snapshot succeeds", async () => {
  vi.mocked(colleaguesApi.withdraw).mockRejectedValue(new HttpError("network", null)); const user = userEvent.setup(); render(<Shop />); await user.click(await screen.findByRole("button", { name: "Withdraw invitation for pending@example.test" })); await user.click(editor().getByRole("button", { name: "Confirm change" })); await screen.findByText(/The change may have completed/);
  vi.mocked(colleaguesApi.list).mockRejectedValueOnce(new HttpError("network", null)); await user.click(screen.getByRole("button", { name: "Review current company records" })); await screen.findByText(/The change may have completed/); expect(screen.queryByRole("textbox")).toBeNull(); await user.click(screen.getByRole("button", { name: "Review current company records" })); await screen.findByText(/This read does not confirm what happened/); expect(colleaguesApi.withdraw).toHaveBeenCalledTimes(1);
});
it.each([401, 403])("hides people and draft after permission denial %s", async status => {
  vi.mocked(colleaguesApi.role).mockRejectedValue(new HttpError("http", status)); const user = userEvent.setup(); render(<Shop />); await user.click(await screen.findByRole("button", { name: "Change role for support@example.test" })); await user.selectOptions(editor().getByRole("combobox"), "Warehouse"); await user.click(editor().getByRole("button", { name: "Confirm change" })); await screen.findByRole("alert"); expect(screen.queryByRole("heading", { name: "Colleagues (5)" })).toBeNull(); expect(screen.queryByRole("combobox")).toBeNull();
});
it.each(["person", "invitation", "email"] as const)("drops a stale %s target on preflight without a write", async kind => {
  const user = userEvent.setup(); render(<Shop />);
  if (kind === "person") { await user.click(await screen.findByRole("button", { name: "Change role for support@example.test" })); await user.selectOptions(editor().getByRole("combobox"), "Warehouse"); rows.users[2].role = "CatalogManager"; }
  else if (kind === "invitation") { await user.click(await screen.findByRole("button", { name: "Withdraw invitation for pending@example.test" })); rows.invitations = []; }
  else { await invite(user); rows.invitations.push({ ...colleagueTeam.invitations[0], id: "other-id", email: "new@example.test" }); }
  await user.click(editor().getByRole("button", { name: kind === "email" ? "Create invitation" : "Confirm change" })); await screen.findByText(/The colleague or invitation changed/); expect(colleaguesApi.role).not.toHaveBeenCalled(); expect(colleaguesApi.withdraw).not.toHaveBeenCalled(); expect(colleaguesApi.invite).not.toHaveBeenCalled();
});
it("keeps a draft after a failed preflight and sends no invitation", async () => {
  const user = userEvent.setup(); render(<Shop />); await invite(user); vi.mocked(colleaguesApi.list).mockRejectedValueOnce(new HttpError("network", null)); await user.click(editor().getByRole("button", { name: "Create invitation" })); await screen.findByText(/No change was submitted/); expect(colleaguesApi.invite).not.toHaveBeenCalled(); expect(editor().getByRole("combobox")).toHaveProperty("value", "Support");
});
it("blocks observed identity, tenant or role movement and requires a full administration reload", async () => {
  const user = userEvent.setup(); render(<Shop />); await invite(user); actor.tenantId = "other-company"; await user.click(editor().getByRole("button", { name: "Create invitation" })); await screen.findByText(/Your sign-in or company permissions changed/); expect(colleaguesApi.invite).not.toHaveBeenCalled(); expect(screen.getByRole("link", { name: "Reload administration" }).getAttribute("href")).toMatch(/\/users$/); expect(screen.queryByRole("textbox")).toBeNull();
});
it("does not claim a successful write when its follow-up session read fails", async () => {
  vi.mocked(colleaguesApi.invite).mockImplementation(async () => { vi.mocked(colleaguesApi.session).mockRejectedValueOnce(new HttpError("network", null)); return { ...colleagueTeam.invitations[0], email: "new@example.test" }; }); const user = userEvent.setup(); render(<Shop />); await invite(user); await user.click(editor().getByRole("button", { name: "Create invitation" })); await screen.findByText(/The change may have completed/); expect(screen.queryByText(/Invitation created for/)).toBeNull();
});
it("ignores duplicate submits and late answers after leaving", async () => {
  const held = deferred<typeof colleagueTeam.invitations[0]>(); vi.mocked(colleaguesApi.invite).mockReturnValue(held.promise); const user = userEvent.setup(); render(<Shop />); await invite(user); fireEvent.submit(screen.getByRole("form")); fireEvent.submit(screen.getByRole("form")); await waitFor(() => expect(colleaguesApi.invite).toHaveBeenCalledTimes(1)); await user.click(screen.getByRole("link", { name: "Leave team" })); expect(vi.mocked(colleaguesApi.invite).mock.calls[0][2].aborted).toBe(true); await act(async () => held.resolve(colleagueTeam.invitations[0])); expect(screen.queryByText(/Invitation created for/)).toBeNull();
});
it("discards work after company context movement", async () => {
  const held = deferred<Colleagues>(); vi.mocked(colleaguesApi.list).mockReturnValueOnce(held.promise); const view = render(<Shop />); await waitFor(() => expect(colleaguesApi.list).toHaveBeenCalledTimes(1)); actor = { ...colleagueOwner, tenantId: "company-b" }; view.rerender(<Shop user={actor} />); expect(vi.mocked(colleaguesApi.list).mock.calls[0][0].aborted).toBe(true); await act(async () => held.resolve(colleagueTeam)); await screen.findByRole("heading", { name: "Colleagues (5)" }); expect(colleaguesApi.invite).not.toHaveBeenCalled();
});
it("keeps new roles read-only and preserves their literal names", async () => {
  rows.users[2].role = "FutureRole <literal>"; rows.invitations[0].role = "FutureInvitationRole"; render(<Shop />); await screen.findByText("FutureRole <literal> · Active"); expect(screen.queryByRole("button", { name: "Change role for support@example.test" })).toBeNull(); expect(screen.queryByRole("button", { name: "Withdraw invitation for pending@example.test" })).toBeNull(); expect(document.querySelector('literal')).toBeNull();
});
it("pages complete arrays locally and clamps the page after withdrawing its final invitation", async () => {
  rows.invitations = Array.from({ length: 21 }, (_, index) => ({ ...colleagueTeam.invitations[0], id: "invite-" + index, email: `pending-${index}@example.test` })); const user = userEvent.setup(); render(<Shop />); await screen.findByRole("heading", { name: "Pending invitations (21)" }); await user.click(within(screen.getByRole("navigation", { name: "Invitation pages" })).getByRole("button", { name: "Next page" })); await user.click(screen.getByRole("button", { name: "Withdraw invitation for pending-20@example.test" })); await user.click(editor().getByRole("button", { name: "Confirm change" })); await screen.findByText("Invitation withdrawn for pending-20@example.test."); expect(screen.queryByText("Page 2 of 1")).toBeNull(); expect(screen.getByRole("heading", { name: "pending-0@example.test" })).toBeTruthy();
});
it("preserves a draft when changing UI language, without another read or write", async () => {
  const user = userEvent.setup(); render(<Shop />); await invite(user); const reads = vi.mocked(colleaguesApi.list).mock.calls.length; await act(async () => { await i18n.changeLanguage("cs"); }); expect(screen.getByRole("textbox", { name: i18n.t("colleagues:email") })).toHaveProperty("value", "New@example.test"); expect(colleaguesApi.list).toHaveBeenCalledTimes(reads); expect(colleaguesApi.invite).not.toHaveBeenCalled();
});
it("finishes initial reads under StrictMode", async () => { render(<StrictMode><Shop /></StrictMode>); await screen.findByRole("heading", { name: "Colleagues (5)" }); expect(colleaguesApi.invite).not.toHaveBeenCalled(); });
