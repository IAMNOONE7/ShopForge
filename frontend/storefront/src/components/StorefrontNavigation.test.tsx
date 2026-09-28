// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  afterAll,
  afterEach,
  beforeAll,
  describe,
  expect,
  it,
  vi,
} from "vitest";
import { MemoryRouter } from "react-router";
import { i18n, initializeI18n } from "../i18n";
import { CategoryNavigation, MobileNavigation } from "./StorefrontNavigation";

const categories = [
  { name: "Living room", slug: "living-room" },
  { name: "Kitchen", slug: "kitchen" },
];

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});

afterEach(cleanup);
afterAll(() => i18n.changeLanguage("en"));

describe("storefront category navigation", () => {
  it("opens every category from the mobile disclosure and restores focus on Escape", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <MobileNavigation
          state={{ status: "ready", categories }}
          contentLanguage="en-IE"
        />
      </MemoryRouter>,
    );

    const trigger = screen.getByRole("button", { name: "Categories" });
    await user.click(trigger);

    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    expect(screen.getByRole("link", { name: "Living room" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "Kitchen" })).toBeTruthy();
    await waitFor(() =>
      expect(document.activeElement).toBe(
        screen.getByRole("link", { name: "All products" }),
      ),
    );

    await user.keyboard("{Escape}");
    expect(trigger.getAttribute("aria-expanded")).toBe("false");
    expect(document.activeElement).toBe(trigger);
  });

  it("keeps category failure local and offers a retry", async () => {
    const user = userEvent.setup();
    const retry = vi.fn();
    render(
      <MemoryRouter>
        <CategoryNavigation
          state={{ status: "error", retry }}
          contentLanguage="en-IE"
        />
      </MemoryRouter>,
    );

    expect(screen.getByRole("link", { name: "All products" })).toBeTruthy();
    expect(screen.getByRole("alert").textContent).toContain(
      "Categories are unavailable.",
    );
    await user.click(
      screen.getByRole("button", { name: "Try categories again" }),
    );
    expect(retry).toHaveBeenCalledOnce();
  });
});
