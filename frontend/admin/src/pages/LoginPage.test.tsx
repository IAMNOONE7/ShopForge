// @vitest-environment jsdom
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { HttpError } from "../api/http";
import { initializeI18n } from "../i18n";
import { LoginPage } from "./LoginPage";

beforeAll(() => initializeI18n());

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
});
