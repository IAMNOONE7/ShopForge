// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { ContentPage } from "./ContentPage";

const mocks = vi.hoisted(() => ({ getContentPage: vi.fn() }));
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api")>()),
  getContentPage: mocks.getContentPage,
}));

const store: Store = {
  id: "store",
  name: "Store",
  currency: "CZK",
  culture: "en-IE",
  logoUrl: null,
  providerKeys: [],
  theme: { primaryColor: "#000000", secondaryColor: "#ffffff", borderRadius: 4 },
};

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  mocks.getContentPage.mockReset();
});
afterAll(() => i18n.changeLanguage("en"));

function served(body: string, title = "Terms and conditions") {
  mocks.getContentPage.mockResolvedValue({
    slug: "terms",
    title,
    body,
    seo: { title, description: null, noIndex: false, canonical: null },
  });
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/pages/terms"]}>
      <StoreContext value={store}>
        <Routes>
          <Route path="/pages/:slug" element={<ContentPage />} />
        </Routes>
      </StoreContext>
    </MemoryRouter>,
  );
}

it("puts a body separated by blank lines on the page as paragraphs", async () => {
  served("You may return anything within 14 days.\n\nWe deliver on Tuesdays.");

  renderPage();

  expect(
    await screen.findByRole("heading", { name: "Terms and conditions" }),
  ).toBeTruthy();
  expect(
    screen.getByText("You may return anything within 14 days."),
  ).toBeTruthy();
  expect(screen.getByText("We deliver on Tuesdays.")).toBeTruthy();
});

// The body is text and never markup, so a merchant who types a tag gets the tag on the page rather than a
// bold word — and a script tag is words too (D-175).
it("shows a tag the merchant typed as the words they typed", async () => {
  served("Read <b>carefully</b>.");

  const { container } = renderPage();

  expect(await screen.findByText("Read <b>carefully</b>.")).toBeTruthy();
  expect(container.querySelector("b")).toBeNull();
});

it("says so when a page is not there", async () => {
  mocks.getContentPage.mockRejectedValue(new HttpError("http", 404));

  renderPage();

  expect(
    await screen.findByRole("heading", { name: "Page not found" }),
  ).toBeTruthy();
});
