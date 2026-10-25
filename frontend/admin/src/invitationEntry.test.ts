// @vitest-environment jsdom
import { afterEach, describe, expect, it } from "vitest";
import { captureInvitationEntry } from "./invitationEntry";

afterEach(() => window.history.replaceState(null, "", "/"));
describe("invitation entry capture", () => {
  it.each(["/", "/admin/"])("captures an opaque token once and scrubs every query/hash before startup for base %s", base => {
    const path = base + "invitations/accept";
    window.history.replaceState({ token: "old-state" }, "", path + "?token=opaque%2Bvalue%2F%3D&returnTo=https%3A%2F%2Fforeign.test%2F#credential");
    expect(captureInvitationEntry(base)).toEqual({ token: "opaque+value/=" }); expect(window.location.pathname).toBe(path);
    expect(window.location.search + window.location.hash).toBe(""); expect(window.history.state).toBeNull();
    expect(captureInvitationEntry(base)).toEqual({ token: null });
  });
  it.each(["", "?token=", "?token=%20", "?token=one&token=two", "?Token=value"])("rejects missing/ambiguous tokens without retaining the URL: %s", query => {
    window.history.replaceState(null, "", "/admin/invitations/accept" + query);
    expect(captureInvitationEntry("/admin/")).toEqual({ token: null }); expect(window.location.search).toBe("");
  });
  it.each(["/admin/platform/invitations/accept", "/invitations/accept", "/admin/other", "/administrator/invitations/accept"])("does not capture other paths with admin base: %s", path => {
    window.history.replaceState(null, "", path + "?token=unchanged"); expect(captureInvitationEntry("/admin/")).toBeNull(); expect(window.location.search).toBe("?token=unchanged");
  });
});
