import { afterEach, describe, expect, it, vi } from "vitest";
import { invitationsApi } from "./invitations";
import { onUnauthorized } from "./http";

const invitation = { email: "colleague@example.test", role: "Support" as const };
const credential = ["fixture", "invite", "value"].join("-");
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());
describe("public staff invitation contracts", () => {
  it("uses only the exact existing token-body endpoints and checked replies", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json({ ...invitation, extra: "discard" })).mockResolvedValueOnce(json({ id: "id", tenantId: "tenant", email: "current@example.test", role: "Owner" }))
      .mockResolvedValueOnce(new Response(null, { status: 200 })).mockResolvedValueOnce(json({ email: invitation.email.toUpperCase() }));
    vi.stubGlobal("fetch", fetch); const signal = new AbortController().signal;
    expect(await invitationsApi.details(credential, signal)).toEqual(invitation); expect(await invitationsApi.session(signal)).toEqual({ email: "current@example.test" });
    await invitationsApi.signOut(signal); expect(await invitationsApi.accept(credential, " raw-password ", invitation, signal)).toEqual({ email: invitation.email.toUpperCase() });
    expect(fetch.mock.calls.map(([path, init]) => [path, init.method, init.body, init.signal])).toEqual([
      ["/api/admin/invitations/details", "POST", JSON.stringify({ token: credential }), signal],
      ["/api/admin/auth/me", "GET", undefined, signal], ["/api/admin/auth/logout", "POST", undefined, signal],
      ["/api/admin/invitations/accept", "POST", JSON.stringify({ token: credential, password: " raw-password " }), signal],
    ]);
  });
  it.each([null, {}, { email: "", role: "Support" }, { ...invitation, role: "Root" }, { ...invitation, role: null }])("rejects malformed or unknown invitation authority: %j", value => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); return expect(invitationsApi.details(credential)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it.each([null, {}, { email: "other@example.test" }])("does not confirm an uncertain or mismatched acceptance: %j", value => {
    const fetch = vi.fn().mockResolvedValue(json(value)); vi.stubGlobal("fetch", fetch);
    return expect(invitationsApi.accept(credential, "some-password", invitation)).rejects.toMatchObject({ kind: "invalid-response" });
  });
  it("treats only 401 as signed out and never broadcasts public authorization failures", async () => {
    const admin = vi.fn(), platform = vi.fn(), stopAdmin = onUnauthorized(admin), stopPlatform = onUnauthorized(platform, "platform");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json({}, 401)).mockResolvedValueOnce(json({}, 403)).mockResolvedValueOnce(json({}, 401)));
    expect(await invitationsApi.session()).toBeNull(); await expect(invitationsApi.session()).rejects.toMatchObject({ status: 403 });
    await expect(invitationsApi.details(credential)).rejects.toMatchObject({ status: 401 }); expect(admin).not.toHaveBeenCalled(); expect(platform).not.toHaveBeenCalled(); stopAdmin(); stopPlatform();
  });
  it("ignores a late aborted reply and never replays a refusal", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(invitation)).mockResolvedValueOnce(json({}, 409)); vi.stubGlobal("fetch", fetch);
    const controller = new AbortController(); controller.abort();
    await expect(invitationsApi.details(credential, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
    await expect(invitationsApi.accept(credential, "some-password", invitation)).rejects.toMatchObject({ status: 409 }); expect(fetch).toHaveBeenCalledTimes(2);
  });
});
