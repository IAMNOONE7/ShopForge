// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { lazy } from "react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { Link, MemoryRouter, useLocation } from "react-router";
import { i18n, initializeI18n } from "../i18n";
import { RouteContent } from "./RouteContent";

beforeAll(initializeI18n);
afterEach(async () => { cleanup(); vi.restoreAllMocks(); await i18n.changeLanguage("en"); });

describe("deferred route content", () => {
  it("keeps the shopping shell usable and announces loading until the page module arrives", async () => {
    let resolve!: (module: { default: () => React.ReactNode }) => void;
    const Page = lazy(() => new Promise<{ default: () => React.ReactNode }>((ready) => { resolve = ready; }));
    render(<MemoryRouter><Link to="/">Shopping shell</Link><RouteContent><Page /></RouteContent></MemoryRouter>);
    expect(screen.getByRole("link", { name: "Shopping shell" })).toBeTruthy();
    expect(screen.getByRole("status").textContent).toContain("Loading…");
    resolve({ default: () => <h1>Loaded account</h1> });
    expect(await screen.findByRole("heading", { name: "Loaded account" })).toBeTruthy();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("localizes a failed module and permits navigation to a different page without retaining the failure", async () => {
    await i18n.changeLanguage("cs");
    vi.spyOn(console, "error").mockImplementation(() => {});
    const Failed = lazy(async () => { throw new Error("module unavailable"); });
    function Content() {
      const location = useLocation();
      return <><Link to="/next">Next page</Link><RouteContent>{location.pathname === "/next" ? <h1>Available page</h1> : <Failed />}</RouteContent></>;
    }
    render(<MemoryRouter><Content /></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "Stránku se nepodařilo načíst" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Obnovit stránku" })).toBeTruthy();
    await userEvent.setup().click(screen.getByRole("link", { name: "Next page" }));
    expect(screen.getByRole("heading", { name: "Available page" })).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
