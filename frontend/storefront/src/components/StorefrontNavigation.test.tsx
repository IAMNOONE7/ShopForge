// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
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
  { name: "Living room", slug: "living-room", parentSlug: null },
  { name: "Kitchen", slug: "kitchen", parentSlug: null },
];

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});

afterEach(cleanup);
afterAll(() => i18n.changeLanguage("en"));

describe("storefront category navigation", () => {
  it("keeps a deep hierarchy reachable on mobile and closes after category navigation", async () => {
    const user = userEvent.setup();
    const tree = [categories[0],
      { name: "Tables", slug: "tables", parentSlug: "living-room" },
      { name: "Oak", slug: "oak", parentSlug: "tables" },
      { name: "Dining", slug: "dining", parentSlug: "oak" },
      { name: "Small", slug: "small", parentSlug: "dining" },
    ];
    render(<MemoryRouter><MobileNavigation state={{ status: "ready", categories: tree }} contentLanguage="en-IE" /></MemoryRouter>);
    await user.click(screen.getByRole("button", { name: "Categories" }));
    const small = screen.getByRole("link", { name: "Small" });
    expect(small.getAttribute("href")).toBe("/c/small");
    expect(small.closest("ul")?.parentElement?.querySelector("a")?.textContent).toBe("Dining");
    await user.click(small);
    expect(screen.getByRole("button", { name: "Categories" }).getAttribute("aria-expanded")).toBe("false");
  });

  it("exposes parent and child destinations on desktop and restores summary focus on Escape", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><CategoryNavigation variant="desktop" state={{ status: "ready", categories: [categories[0],
      { name: "Tables", slug: "tables", parentSlug: "living-room" },
    ] }} contentLanguage="en-IE" /></MemoryRouter>);
    const summary = screen.getByText("Living room");
    await user.click(summary);
    expect(screen.getByRole("link", { name: "All Living room" }).getAttribute("href")).toBe("/c/living-room");
    const child = screen.getByRole("link", { name: "Tables" });
    child.focus();
    await user.keyboard("{Escape}");
    expect(summary.closest("details")?.open).toBe(false);
    expect(document.activeElement).toBe(summary);
    await user.click(summary);
    fireEvent.pointerDown(document.body);
    expect(summary.closest("details")?.open).toBe(false);
  });

  it("bounds the desktop row and keeps extra roots reachable in the overflow menu", async () => {
    const user = userEvent.setup();
    const many = Array.from({ length: 8 }, (_, index) => ({ name: `Category ${index}`, slug: `category-${index}`, parentSlug: null }));
    render(<MemoryRouter><CategoryNavigation variant="desktop" state={{ status: "ready", categories: many }} contentLanguage="en-IE" /></MemoryRouter>);
    const summary = screen.getByText("More categories");
    expect(summary.closest("details")?.open).toBe(false);
    expect(document.querySelectorAll(".desktop-category-list > li")).toHaveLength(6);
    await user.click(summary);
    expect(summary.closest("details")?.open).toBe(true);
    expect(screen.getByRole("link", { name: "Category 7" }).getAttribute("href")).toBe("/c/category-7");
  });

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
