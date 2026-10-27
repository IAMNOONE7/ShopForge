import { afterEach, expect, it, vi } from "vitest";
import { colleaguesApi, canChangeColleague } from "./colleagues";
import { onUnauthorized } from "./http";
import { colleagueTeam, colleagueOwner, colleagueAdmin } from "../test/colleagueFixtures";
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
const signal = () => new AbortController().signal;
afterEach(() => vi.unstubAllGlobals());
it("reads checked company-wide records and actor without a store identifier, caching or undeclared facts", async () => {
  const fetch = vi.fn().mockResolvedValueOnce(json({ ...colleagueOwner, credential: "not-public" })).mockResolvedValueOnce(json(colleagueTeam)); vi.stubGlobal("fetch", fetch);
  expect(await colleaguesApi.session(signal())).toEqual(colleagueOwner); expect(await colleaguesApi.list(signal())).toEqual(colleagueTeam);
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.cache, options.body])).toEqual([["/api/admin/auth/me", "GET", "no-store", undefined], ["/api/admin/users", "GET", "no-store", undefined]]);
});
it.each([null, {}, { ...colleagueOwner, tenantId: null }, { ...colleagueOwner, id: "" }])("rejects malformed actor records: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(colleaguesApi.session(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([null, [], {}, { users: [], invitations: null }, { ...colleagueTeam, users: [null] }, { ...colleagueTeam, users: [{ ...colleagueTeam.users[0], isActive: "false" }] }, { ...colleagueTeam, users: [colleagueTeam.users[0], colleagueTeam.users[0]] }, { ...colleagueTeam, invitations: [colleagueTeam.invitations[0], { ...colleagueTeam.invitations[0], id: "another" }] }, { ...colleagueTeam, invitations: [{ ...colleagueTeam.invitations[0], expiresAt: "bad" }] }])("rejects malformed or ambiguous team snapshots: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(colleaguesApi.list(signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it("retains unknown role text for safe read-only presentation", async () => {
  const team = { users: [{ ...colleagueTeam.users[0], role: "FutureRole" }], invitations: [] }; vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(team)));
  expect(await colleaguesApi.list(signal())).toEqual(team); expect(canChangeColleague(colleagueOwner, team.users[0])).toBe(false);
});
it("sends exact explicit create, role, activity and withdrawal commands with encoded identities", async () => {
  const person = { ...colleagueTeam.users[2], id: "person /?" }, invitation = { ...colleagueTeam.invitations[0], email: "new@example.test" };
  const fetch = vi.fn().mockResolvedValueOnce(json(invitation)).mockResolvedValueOnce(json({ ...person, role: "Warehouse" })).mockResolvedValueOnce(json({ ...person, isActive: false })).mockResolvedValueOnce(json({ ...person, isActive: true })).mockResolvedValueOnce(new Response(null, { status: 204 })); vi.stubGlobal("fetch", fetch);
  const request = signal(); await colleaguesApi.invite(" New@example.test ", "Support", request); await colleaguesApi.role(person, "Warehouse", request); await colleaguesApi.activity(person, request); await colleaguesApi.activity({ ...person, isActive: false }, request); await colleaguesApi.withdraw("invite /?", request);
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body, options.signal])).toEqual([
    ["/api/admin/users/invitations", "POST", '{"email":"New@example.test","role":"Support"}', request],
    ["/api/admin/users/person%20%2F%3F/role", "PUT", '{"role":"Warehouse"}', request],
    ["/api/admin/users/person%20%2F%3F/deactivate", "POST", undefined, request], ["/api/admin/users/person%20%2F%3F/activate", "POST", undefined, request],
    ["/api/admin/users/invitations/invite%20%2F%3F", "DELETE", undefined, request],
  ]);
});
it.each([{ ...colleagueTeam.invitations[0], email: "other@example.test" }, { ...colleagueTeam.invitations[0], role: "Owner" }, { ...colleagueTeam.invitations[0], expiresAt: null }, null])("does not confirm an invitation from the wrong or malformed answer: %j", async value => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(value))); await expect(colleaguesApi.invite("pending@example.test", "Support", signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([200, 202, 205])("does not infer a withdrawn invitation from undocumented status %s", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status }))); await expect(colleaguesApi.withdraw("id", signal())).rejects.toMatchObject({ kind: "invalid-response", status });
});
it("rejects wrong identity, role, activity and unchanged activity acknowledgments", async () => {
  const before = colleagueTeam.users[2];
  for (const row of [{ ...before, id: "wrong" }, { ...before, role: "Warehouse" }, before]) {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json(row))); await expect(colleaguesApi.activity(before, signal())).rejects.toMatchObject({ kind: "invalid-response" });
  }
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({ ...before, role: "Warehouse", isActive: false }))); await expect(colleaguesApi.role(before, "Warehouse", signal())).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([400, 401, 403, 404, 409, 429, 503])("retains actionable refusal %s without leaking private diagnostics or replaying", async status => {
  const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: "private-email", traceId: "private-reference", errors: { Email: ["private-address"], Role: ["private-role"], token: ["private-proof"] } }), { status, headers: { "Content-Type": "application/problem+json", "Retry-After": "10" } })); vi.stubGlobal("fetch", fetch);
  const listener = vi.fn(), unsubscribe = onUnauthorized(listener);
  try {
    const error = await colleaguesApi.invite("pending@example.test", "Support", signal()).catch((value: unknown) => value);
    expect(error).toMatchObject({ status, traceId: null, retryAfter: "10", problem: { errors: { email: [], role: [] } } }); expect(JSON.stringify(error)).not.toContain("private-"); expect(fetch).toHaveBeenCalledTimes(1); expect(listener).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
  } finally { unsubscribe(); }
});
it("ignores an aborted response and does not invalidate a new actor from an aborted 401", async () => {
  const request = new AbortController(); request.abort(); const listener = vi.fn(), unsubscribe = onUnauthorized(listener);
  try { vi.stubGlobal("fetch", vi.fn().mockResolvedValue(json({}, 401))); await expect(colleaguesApi.list(request.signal)).rejects.toMatchObject({ name: "AbortError" }); expect(listener).not.toHaveBeenCalled(); }
  finally { unsubscribe(); }
});
it("mirrors self and owner restrictions without inventing an owner-count limit or restricting pending withdrawals", () => {
  expect(canChangeColleague(colleagueOwner, colleagueTeam.users[0])).toBe(false); expect(canChangeColleague(colleagueAdmin, colleagueTeam.users[0])).toBe(false);
  expect(canChangeColleague(colleagueOwner, colleagueTeam.users[4])).toBe(true); expect(canChangeColleague(colleagueAdmin, colleagueTeam.users[2])).toBe(true);
});
