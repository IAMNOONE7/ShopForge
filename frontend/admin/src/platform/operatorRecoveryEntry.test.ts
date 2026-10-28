// @vitest-environment jsdom
import { afterEach, expect, it } from "vitest";
import { captureOperatorRecoveryEntry } from "./operatorRecoveryEntry";
afterEach(() => { window.history.replaceState(null, "", "/"); localStorage.clear(); sessionStorage.clear(); });
it.each(["/", "/admin/"])("captures opaque proof in a one-use scoped capsule and scrubs all URL/state credentials before startup for %s", base => {
  window.history.replaceState({ token: "old-proof" }, "", base + "platform/reset-password/?token=fixture%2Bproof%2F%3D&returnTo=https://foreign.test#private");
  const entry = captureOperatorRecoveryEntry(base); expect(entry?.kind).toBe("reset"); if (entry?.kind !== "reset") throw new Error("Expected reset entry");
  expect(location.pathname).toBe(base + "platform/reset-password/"); expect(location.search + location.hash).toBe(""); expect(history.state).toBeNull();
  expect(JSON.stringify(entry)).not.toContain("fixture"); expect(entry.proof.available()).toBe(true); expect(entry.proof.take()).toBe("fixture+proof/="); expect(entry.proof.available()).toBe(false); expect(entry.proof.take()).toBeNull();
  const again = captureOperatorRecoveryEntry(base); expect(again?.kind === "reset" && again.proof.id !== entry.proof.id).toBe(true); expect(localStorage.length + sessionStorage.length).toBe(0);
});
it.each(["", "?token=", "?token=%20", "?token=one&token=two", "?token=one&token=one", "?Token=value"])("closes missing/ambiguous proof while scrubbing its URL: %s", query => {
  window.history.replaceState(null, "", "/admin/platform/reset-password" + query + "#private"); const entry = captureOperatorRecoveryEntry("/admin/");
  expect(entry?.kind === "reset" && entry.proof.available()).toBe(false); expect(location.search + location.hash).toBe("");
});
it.each(["/", "/admin/"])("captures the public request page without keeping accidental credentials: %s", base => {
  window.history.replaceState(null, "", base + "platform/forgot-password?token=private&email=private@example.test#private"); expect(captureOperatorRecoveryEntry(base)).toEqual({ kind: "forgot" }); expect(location.search + location.hash).toBe("");
});
it.each(["/admin/reset-password", "/account/reset-password", "/platform/reset-password", "/admin/platform/invitations/accept", "/admin/invitations/accept", "/administrator/platform/reset-password"])("leaves another realm or purpose untouched: %s", path => {
  window.history.replaceState(null, "", path + "?token=unchanged"); expect(captureOperatorRecoveryEntry("/admin/")).toBeNull(); expect(location.search).toBe("?token=unchanged");
});
