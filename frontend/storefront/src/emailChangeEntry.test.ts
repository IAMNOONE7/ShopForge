// @vitest-environment jsdom
import { afterEach, expect, it } from "vitest";
import { captureEmailChangeEntry } from "./emailChangeEntry";
afterEach(() => { window.history.replaceState(null, "", "/"); window.localStorage.clear(); window.sessionStorage.clear(); });
it("captures and scrubs a confirmation token, keeps it only in a one-use memory capsule and drops destinations", () => {
  window.history.replaceState(null, "", "/account/confirm-email?token=fixture%2Bproof&returnTo=https%3A%2F%2Fforeign.test#private");
  const entry = captureEmailChangeEntry()!;
  expect(location.pathname + location.search + location.hash).toBe("/account/confirm-email"); expect(entry.available()).toBe(true);
  expect(JSON.stringify(entry)).toBe("{}"); expect(entry.take()).toBe("fixture+proof"); expect(entry.available()).toBe(false); expect(entry.take()).toBeNull();
  expect(window.localStorage.length).toBe(0); expect(window.sessionStorage.length).toBe(0);
});
it.each(["", "?token=", "?token=%20%20", "?token=one&token=two", "?token=one&token=one"])("rejects missing/blank/duplicate tokens but still scrubs the link: %s", query => {
  window.history.replaceState(null, "", "/account/confirm-email/" + query + "#private"); const entry = captureEmailChangeEntry()!;
  expect(entry.available()).toBe(false); expect(entry.take()).toBeNull(); expect(location.search + location.hash).toBe("");
});
it.each(["/account/verify", "/account/reset-password", "/account"])("preserves the existing %s entry behavior", path => {
  window.history.replaceState(null, "", path + "?token=fixture-proof"); expect(captureEmailChangeEntry()).toBeNull(); expect(location.search).toBe("?token=fixture-proof");
});
