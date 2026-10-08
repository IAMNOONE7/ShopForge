// @vitest-environment jsdom
import { lazy, type ReactNode } from "react";
import { act, cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { i18n, initializeI18n } from "../i18n";
import { DeferredPage } from "./DeferredPage";

beforeAll(async () => { await initializeI18n(); await i18n.changeLanguage("en"); });
afterEach(async () => { cleanup(); vi.restoreAllMocks(); await i18n.changeLanguage("en"); });

it("keeps navigation visible while a deferred category page loads", async () => {
  let resolve!: (module: { default: () => ReactNode }) => void;
  const Page = lazy(() => new Promise<{ default: () => ReactNode }>((done) => { resolve = done; }));
  render(<MemoryRouter><Link to="/stores">Stores</Link><DeferredPage><Page /></DeferredPage></MemoryRouter>);
  expect(screen.getByRole("link", { name: "Stores" })).toBeTruthy();
  expect(screen.getByRole("status").textContent).toContain("Loading");
  await act(async () => resolve({ default: () => <h1>Categories</h1> }));
  expect(await screen.findByRole("heading", { name: "Categories" })).toBeTruthy();
  expect(screen.queryByRole("status")).toBeNull();
});

it("shows a localized module failure with a retry and recovers when navigating elsewhere", async () => {
  await i18n.changeLanguage("cs");
  vi.spyOn(console, "error").mockImplementation(() => undefined);
  const Broken = lazy(() => Promise.reject(new Error("Chunk load failed")));
  const user = userEvent.setup();
  render(<MemoryRouter initialEntries={["/categories"]}>
    <Link to="/stores">Obchody</Link>
    <Routes>
      <Route path="/categories" element={<DeferredPage><Broken /></DeferredPage>} />
      <Route path="/stores" element={<DeferredPage><h1>Obchody</h1></DeferredPage>} />
    </Routes>
  </MemoryRouter>);
  expect(await screen.findByRole("alert")).toBeTruthy();
  expect(screen.getByRole("button", { name: "Zkusit znovu" })).toBeTruthy();
  expect(screen.queryByText("Chunk load failed")).toBeNull();
  await user.click(screen.getByRole("link", { name: "Obchody" }));
  expect(await screen.findByRole("heading", { name: "Obchody" })).toBeTruthy();
  expect(screen.queryByRole("alert")).toBeNull();
});
