// @vitest-environment jsdom
import { StrictMode } from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router";
import { HttpError } from "../api/http";
import { CustomerContext, type CustomerState } from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import { ForgotPasswordPage } from "./ForgotPasswordPage";
import { RegisterPage } from "./RegisterPage";
import { ResetPasswordPage } from "./ResetPasswordPage";
import { SignInPage } from "./SignInPage";
import { VerifyEmailPage } from "./VerifyEmailPage";

const mocks = vi.hoisted(() => ({
  register: vi.fn(),
  verifyEmail: vi.fn(),
  signIn: vi.fn(),
  requestPasswordReset: vi.fn(),
  resetPassword: vi.fn(),
}));
vi.mock("../account", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../account")>()),
  ...mocks,
}));

const customer = {
  email: "ada@example.test",
  firstName: "Ada",
  lastName: "Lovelace",
  phone: null,
};
const apply = vi.fn();
const customerState: CustomerState = {
  status: "guest",
  customer: null,
  error: null,
  apply,
  retry: vi.fn(),
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((yes) => {
    resolve = yes;
  });
  return { promise, resolve };
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  apply.mockReset();
  for (const mock of Object.values(mocks)) mock.mockReset();
  window.sessionStorage.clear();
});
afterAll(() => i18n.changeLanguage("en"));

function pageFor(path: string) {
  if (path.includes("forgot-password")) return <ForgotPasswordPage />;
  if (path.includes("reset-password")) return <ResetPasswordPage />;
  if (path.includes("register")) return <RegisterPage />;
  if (path.includes("verify")) return <VerifyEmailPage />;
  return <SignInPage />;
}

function renderPage(path: string, strict = false) {
  const content = (
    <MemoryRouter initialEntries={[path]}>
      <CustomerContext value={customerState}>
        <Routes>
          <Route path="/account/sign-in" element={pageFor(path)} />
          <Route path="/account/register" element={pageFor(path)} />
          <Route path="/account/forgot-password" element={pageFor(path)} />
          <Route path="/account/reset-password" element={pageFor(path)} />
          <Route path="/account/verify" element={pageFor(path)} />
          <Route path="/account" element={<h1>Account destination</h1>} />
          <Route path="/checkout" element={<h1>Checkout destination</h1>} />
        </Routes>
      </CustomerContext>
    </MemoryRouter>
  );
  return render(strict ? <StrictMode>{content}</StrictMode> : content);
}

async function fillCredentials(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole("textbox", { name: "E-mail" }), "ada@example.test");
  await user.type(screen.getByLabelText("Password"), "correct-pass");
}

describe("customer authentication pages", () => {
  it("guides an empty sign-in form and exposes password text through a separate control", async () => {
    const user = userEvent.setup();
    renderPage("/account/sign-in");

    await user.click(screen.getByRole("button", { name: "Sign in" }));
    expect(screen.getByText("Complete the required fields.")).toBeTruthy();
    expect(document.activeElement?.getAttribute("role")).toBe("alert");
    expect(mocks.signIn).not.toHaveBeenCalled();

    const password = screen.getByLabelText("Password") as HTMLInputElement;
    expect(password.type).toBe("password");
    await user.click(screen.getByRole("button", { name: "Show password" }));
    expect(password.type).toBe("text");
    expect(screen.getByRole("button", { name: "Hide password" })).toBeTruthy();
  });

  it("keeps the e-mail, clears the secret, and gives the generic credential error", async () => {
    const user = userEvent.setup();
    mocks.signIn.mockRejectedValue(new HttpError("http", 401));
    renderPage("/account/sign-in");
    await fillCredentials(user);
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Invalid e-mail or password.")).toBeTruthy();
    expect(screen.getByRole("textbox", { name: "E-mail" })).toHaveProperty(
      "value",
      "ada@example.test",
    );
    expect(screen.getByLabelText("Password")).toHaveProperty("value", "");
  });

  it("returns a signed-in customer safely", async () => {
    const user = userEvent.setup();
    mocks.signIn.mockResolvedValue(customer);
    renderPage("/account/sign-in?returnTo=%2Fcheckout");
    await fillCredentials(user);
    await user.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByRole("heading", { name: "Checkout destination" })).toBeTruthy();
    expect(apply).toHaveBeenCalledWith(customer);

    cleanup();
    apply.mockReset();
    mocks.signIn.mockResolvedValue(customer);
    const next = userEvent.setup();
    renderPage("/account/sign-in?returnTo=https%3A%2F%2Fattacker.example");
    await fillCredentials(next);
    await next.click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByRole("heading", { name: "Account destination" })).toBeTruthy();
  });

  it("does not claim a reset e-mail was sent when the request fails", async () => {
    const user = userEvent.setup();
    mocks.requestPasswordReset.mockRejectedValue(new HttpError("network", null));
    renderPage("/account/forgot-password");
    const email = screen.getByRole("textbox", { name: "E-mail" });
    await user.type(email, "ada@example.test");
    await user.click(screen.getByRole("button", { name: "Send a reset link" }));

    expect(
      await screen.findByText("The server could not be reached. Check your connection and try again."),
    ).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Check your e-mail" })).toBeNull();
    expect(email).toHaveProperty("value", "ada@example.test");
  });

  it("shows enumeration-safe copy only after an accepted reset request", async () => {
    const user = userEvent.setup();
    mocks.requestPasswordReset.mockResolvedValue(undefined);
    renderPage("/account/forgot-password");
    await user.type(screen.getByRole("textbox", { name: "E-mail" }), "unknown@example.test");
    await user.click(screen.getByRole("button", { name: "Send a reset link" }));

    expect(await screen.findByRole("heading", { name: "Check your e-mail" })).toBeTruthy();
    expect(screen.getByText(/If an account exists for that address/)).toBeTruthy();
  });

  it("verifies only after an explicit action and locks rapid duplicate attempts", async () => {
    const confirmation = deferred<typeof customer>();
    mocks.verifyEmail.mockReturnValue(confirmation.promise);
    renderPage("/account/verify?token=one-time-token", true);
    expect(mocks.verifyEmail).not.toHaveBeenCalled();

    const button = screen.getByRole("button", { name: "Confirm e-mail" });
    fireEvent.click(button);
    fireEvent.click(button);
    expect(mocks.verifyEmail).toHaveBeenCalledTimes(1);
    confirmation.resolve(customer);

    expect(await screen.findByRole("heading", { name: "Account destination" })).toBeTruthy();
    expect(apply).toHaveBeenCalledWith(customer);
  });

  it("gives next steps for missing and used tokens", async () => {
    renderPage("/account/reset-password");
    expect(screen.getByRole("heading", { name: "This link no longer works" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "Request a new reset link" })).toBeTruthy();
    expect(mocks.resetPassword).not.toHaveBeenCalled();

    cleanup();
    mocks.verifyEmail.mockRejectedValue(new HttpError("http", 400));
    const user = userEvent.setup();
    renderPage("/account/verify?token=used-token");
    await user.click(screen.getByRole("button", { name: "Confirm e-mail" }));
    expect(await screen.findByRole("heading", { name: "This link no longer works" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "Register again" })).toBeTruthy();
  });

  it("changes a password once and rejects a reused reset link", async () => {
    const user = userEvent.setup();
    mocks.resetPassword.mockResolvedValue(undefined);
    renderPage("/account/reset-password?token=reset-token&returnTo=%2Fcheckout");
    await user.type(screen.getByLabelText("New password"), "new-password-1");
    await user.click(screen.getByRole("button", { name: "Save password" }));
    expect(await screen.findByRole("heading", { name: "Password changed" })).toBeTruthy();
    expect(mocks.resetPassword).toHaveBeenCalledWith("reset-token", "new-password-1");
    expect(screen.getByRole("link", { name: "Sign in" }).getAttribute("href")).toContain(
      "returnTo=%2Fcheckout",
    );

    cleanup();
    mocks.resetPassword.mockRejectedValue(new HttpError("http", 400));
    const next = userEvent.setup();
    renderPage("/account/reset-password?token=used-token");
    await next.type(screen.getByLabelText("New password"), "new-password-2");
    await next.click(screen.getByRole("button", { name: "Save password" }));
    expect(await screen.findByRole("heading", { name: "This link no longer works" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "Request a new reset link" })).toBeTruthy();
  });

  it("locks duplicate registration submissions and keeps the safe return", async () => {
    const accepted = deferred<void>();
    mocks.register.mockReturnValue(accepted.promise);
    const user = userEvent.setup();
    renderPage("/account/register?returnTo=%2Fcheckout");
    await user.type(screen.getByRole("textbox", { name: "First name" }), "Ada");
    await user.type(screen.getByRole("textbox", { name: "Last name" }), "Lovelace");
    await user.type(screen.getByRole("textbox", { name: "E-mail" }), "ada@example.test");
    await user.type(screen.getByLabelText("Password"), "correct-pass");
    const submit = screen.getByRole("button", { name: "Create account" });
    fireEvent.click(submit);
    fireEvent.click(submit);
    expect(mocks.register).toHaveBeenCalledTimes(1);
    accepted.resolve();

    expect(await screen.findByRole("heading", { name: "Check your e-mail" })).toBeTruthy();
    expect(screen.getByText(/If this address can be registered/)).toBeTruthy();
    expect(screen.getByRole("link", { name: "Back to sign in" }).getAttribute("href")).toContain(
      "returnTo=%2Fcheckout",
    );
  });
});
