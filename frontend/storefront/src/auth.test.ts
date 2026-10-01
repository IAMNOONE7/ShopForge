// @vitest-environment jsdom
import { describe, expect, it } from "vitest";
import {
  authPath,
  emailIssues,
  passwordIssues,
  registrationIssues,
  rememberAuthReturn,
  resolveAuthReturn,
  clearAuthReturn,
  safeAuthReturn,
  signInIssues,
} from "./auth";

describe("auth helpers", () => {
  it("only accepts checkout and private account return paths", () => {
    expect(safeAuthReturn("/checkout?step=payment")).toBe("/checkout?step=payment");
    expect(safeAuthReturn("/account/orders/2026-1#documents")).toBe(
      "/account/orders/2026-1#documents",
    );
    expect(safeAuthReturn("https://attacker.example/checkout")).toBe("/account");
    expect(safeAuthReturn("//attacker.example/checkout")).toBe("/account");
    expect(safeAuthReturn("/cart")).toBe("/account");
    expect(authPath("/account/sign-in", "/checkout")).toBe(
      "/account/sign-in?returnTo=%2Fcheckout",
    );
  });

  it("carries a validated return path across an e-mail link", () => {
    window.sessionStorage.clear();
    rememberAuthReturn("/checkout");
    expect(resolveAuthReturn(null)).toBe("/checkout");
    expect(resolveAuthReturn("//attacker.example")).toBe("/account");
    clearAuthReturn();
    expect(resolveAuthReturn(null)).toBe("/account");
  });

  it("matches the customer contract before submitting", () => {
    expect(signInIssues({ email: "", password: "" }).map((issue) => issue.field)).toEqual([
      "email",
      "password",
    ]);
    expect(emailIssues("not-an-email")).toHaveLength(1);
    expect(passwordIssues("short")).toHaveLength(1);
    expect(passwordIssues("1234567890")).toEqual([]);
    expect(passwordIssues("x".repeat(129))).toHaveLength(1);
    expect(
      registrationIssues({
        firstName: " ",
        lastName: "x".repeat(101),
        email: "person@example.test",
        password: "valid-pass",
      }).map((issue) => issue.field),
    ).toEqual(["firstName", "lastName"]);
  });
});
