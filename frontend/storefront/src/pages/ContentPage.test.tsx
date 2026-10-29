// @vitest-environment jsdom
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
    seo: { title, description: null, socialImageUrl: null, noIndex: false, canonical: null },
  });
}

function renderPage(slug = "terms") {
  return render(
    <MemoryRouter initialEntries={[`/pages/${slug}`]}>
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

it.each(["kontakt", "o-obchodu"])("serves actual merchant information for %s as inert text with the store's language", async slug => {
  mocks.getContentPage.mockResolvedValue({ slug, title: "Merchant heading", body: "Recorded line\n<script>literal</script>\n\nSecond paragraph",
    seo: { title: "Merchant heading", description: null, socialImageUrl: null, noIndex: false, canonical: null } });
  renderPage(slug);
  const heading = await screen.findByRole("heading", { name: "Merchant heading" });
  expect(heading.getAttribute("lang")).toBe(store.culture);
  await waitFor(() => expect(document.title).toBe("Merchant heading"));
  expect(document.querySelector(".content-body")?.getAttribute("lang")).toBe(store.culture);
  expect(document.querySelector(".content-body")?.textContent).toContain("<script>literal</script>");
  expect(document.querySelector(".content-body script")).toBeNull();
  expect(mocks.getContentPage).toHaveBeenCalledExactlyOnceWith(slug, expect.any(AbortSignal));
  const navigation = screen.getByRole("navigation", { name: "Store information" });
  expect(within(navigation).getByRole("link", { name: "Contact" }).getAttribute("href")).toBe("/pages/kontakt");
  expect(within(navigation).getByRole("link", { name: "About the store" }).getAttribute("href")).toBe("/pages/o-obchodu");
});

it.each(["kontakt", "o-obchodu"])("uses a truthful unavailable state for an unpublished %s page", async slug => {
  mocks.getContentPage.mockRejectedValue(new HttpError("http", 404));
  renderPage(slug);
  expect(await screen.findByRole("heading", { name: slug === "kontakt" ? "Contact" : "About the store" })).toBeTruthy();
  expect(await screen.findByText(slug === "kontakt" ? "This store's contact details are not available yet." : "Information about this store is not available yet.")).toBeTruthy();
  expect(screen.queryByRole("form")).toBeNull();
  expect(screen.queryByRole("textbox")).toBeNull();
  expect(document.querySelector('a[href^="mailto:"],a[href^="tel:"]')).toBeNull();
  expect(mocks.getContentPage).toHaveBeenCalledTimes(1);
});

it("does not mistake a published empty contact page for supplied contact details", async () => {
  mocks.getContentPage.mockResolvedValue({ slug: "kontakt", title: "Contact", body: "   ", seo: null });
  renderPage("kontakt");
  expect(await screen.findByText("This store's contact details are not available yet.")).toBeTruthy();
  expect(document.querySelector(".content-body")).toBeNull();
});

it("retries a failed information read explicitly and returns focus to the current heading", async () => {
  const user = userEvent.setup();
  mocks.getContentPage.mockRejectedValueOnce(new HttpError("http", 503)).mockResolvedValue({ slug: "kontakt", title: "Saved contact", body: "Actual merchant text", seo: null });
  renderPage("kontakt");
  await user.click(await screen.findByRole("button", { name: "Try again" }));
  const heading = await screen.findByRole("heading", { name: "Saved contact" });
  await waitFor(() => expect(document.activeElement).toBe(heading));
  expect(mocks.getContentPage).toHaveBeenCalledTimes(2);
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
