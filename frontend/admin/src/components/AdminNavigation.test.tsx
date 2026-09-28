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
import { MemoryRouter, useLocation } from "react-router";
import type { AdminStore } from "../api";
import { i18n, initializeI18n } from "../i18n";
import { AdminNavigation, MobileAdminNavigation } from "./AdminNavigation";

const stores: AdminStore[] = [
  {
    id: "store-a",
    name: "Wooden Home",
    currency: "CZK",
    culture: "cs-CZ",
    status: "published",
    theme: {
      primaryColor: "#000000",
      secondaryColor: "#ffffff",
      borderRadius: 6,
    },
    logoUrl: null,
    primaryHostName: "shop-a.localhost",
    returnWindowDays: 14,
    company: null,
  },
];

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(cleanup);
afterAll(() => i18n.changeLanguage("en"));

function Location() {
  return <output>{useLocation().pathname}</output>;
}

describe("admin navigation", () => {
  it("shows policy-correct company links and selects a store by URL", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/products"]}>
        <AdminNavigation
          stores={{
            status: "ready",
            data: stores,
            refreshing: false,
            refreshError: null,
          }}
          reloadStores={vi.fn()}
          selectedStore={null}
          role="Support"
        />
        <Location />
      </MemoryRouter>,
    );

    expect(
      screen
        .getByRole("link", { name: "Products" })
        .getAttribute("aria-current"),
    ).toBe("page");
    expect(screen.queryByRole("link", { name: "New store" })).toBeNull();
    await user.selectOptions(
      screen.getByRole("combobox", { name: "Selected store" }),
      "store-a",
    );
    expect(screen.getByText("/stores/store-a")).toBeTruthy();
  });

  it("opens the mobile menu from the keyboard and restores trigger focus", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/stores/store-a"]}>
        <MobileAdminNavigation
          stores={{
            status: "ready",
            data: stores,
            refreshing: false,
            refreshError: null,
          }}
          reloadStores={vi.fn()}
          selectedStore={stores[0]}
          role="Owner"
        />
      </MemoryRouter>,
    );
    const trigger = screen.getByRole("button", { name: "Menu" });
    trigger.focus();
    await user.keyboard("{Enter}");
    await waitFor(() =>
      expect(document.activeElement).toBe(
        screen.getByRole("link", { name: "Products" }),
      ),
    );
    expect(screen.getByRole("link", { name: "Orders" })).toBeTruthy();
    await user.keyboard("{Escape}");
    expect(document.activeElement).toBe(trigger);
    expect(trigger.getAttribute("aria-expanded")).toBe("false");
  });
});
