// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { i18n, initializeI18n } from "../i18n";
import { PermissionScope } from "./PermissionScope";

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(cleanup);
afterAll(() => i18n.changeLanguage("en"));

describe("PermissionScope", () => {
  it("disables mutation controls while leaving read retries available", async () => {
    render(
      <PermissionScope allowed={false}>
        <form>
          <input aria-label="Name" />
          <button type="submit">Save</button>
        </form>
        <div className="request-error">
          <button type="button">Retry read</button>
        </div>
      </PermissionScope>,
    );
    await waitFor(() =>
      expect(
        (screen.getByRole("button", { name: "Save" }) as HTMLButtonElement)
          .disabled,
      ).toBe(true),
    );
    expect(
      (screen.getByRole("textbox", { name: "Name" }) as HTMLInputElement)
        .disabled,
    ).toBe(true);
    expect(
      (screen.getByRole("button", { name: "Retry read" }) as HTMLButtonElement)
        .disabled,
    ).toBe(false);
    expect(
      screen.getByText("Your role has read-only access to this section."),
    ).toBeTruthy();
  });
});
