// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { HttpError } from "../api/http";
import { initializeI18n } from "../i18n";
import { LoginPage } from "./LoginPage";

beforeAll(() => initializeI18n());
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe("LoginPage mutation state", () => {
  it("submits once while pending and preserves fields after failure", async () => {
    let reject!: (reason: unknown) => void;
    const pending = new Promise<never>((_resolve, no) => {
      reject = no;
    });
    const login = vi.spyOn(api, "login").mockReturnValue(pending);
    const user = userEvent.setup();
    render(<LoginPage expired={false} onLogin={vi.fn()} />);

    const email = screen.getByLabelText("E-mail");
    const password = screen.getByLabelText("Password");
    const submit = screen.getByRole("button", { name: "Sign in" });
    await user.type(email, "owner@example.com");
    await user.type(password, "secret-value");
    await user.click(submit);
    await user.click(submit);
    expect(login).toHaveBeenCalledTimes(1);

    reject(new HttpError("http", 401));
    await screen.findByRole("alert");
    await waitFor(() =>
      expect((submit as HTMLButtonElement).disabled).toBe(false),
    );
    expect((email as HTMLInputElement).value).toBe("owner@example.com");
    expect((password as HTMLInputElement).value).toBe("secret-value");
  });

  it("uses the wrapped signed-in user returned by the current API", async () => {
    const user = userEvent.setup();
    const account = {
      id: "owner-a", email: "owner@example.com", role: "Owner", tenantId: "tenant-a",
    };
    vi.spyOn(api, "login").mockResolvedValue({
      twoFactorRequired: false, ticket: null, user: account,
    });
    const onLogin = vi.fn();
    render(<LoginPage expired={false} onLogin={onLogin} />);

    await user.type(screen.getByLabelText("E-mail"), account.email);
    await user.type(screen.getByLabelText("Password"), "secret-value");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    await waitFor(() => expect(onLogin).toHaveBeenCalledWith(account));
  });

  it("completes a two-factor challenge without treating the ticket as a user", async () => {
    const user = userEvent.setup();
    const account = {
      id: "owner-a", email: "owner@example.com", role: "Owner", tenantId: "tenant-a",
    };
    vi.spyOn(api, "login").mockResolvedValue({
      twoFactorRequired: true, ticket: "single-use-ticket", user: null,
    });
    const complete = vi.spyOn(api, "completeTwoFactor").mockResolvedValue({
      twoFactorRequired: false, ticket: null, user: account,
    });
    const onLogin = vi.fn();
    render(<LoginPage expired={false} onLogin={onLogin} />);

    await user.type(screen.getByLabelText("E-mail"), account.email);
    await user.type(screen.getByLabelText("Password"), "secret-value");
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByLabelText("Authentication or recovery code")).toBeTruthy();
    expect(onLogin).not.toHaveBeenCalled();
    await user.type(screen.getByLabelText("Authentication or recovery code"), "123456");
    await user.click(screen.getByRole("button", { name: "Verify and sign in" }));
    await waitFor(() => expect(complete).toHaveBeenCalledWith("single-use-ticket", "123456"));
    expect(onLogin).toHaveBeenCalledWith(account);
  });

});
