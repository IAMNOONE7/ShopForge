// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { RouteEffects } from "./RouteEffects";

const store: Store = {
  id: "store-a",
  name: "Test Store",
  currency: "EUR",
  culture: "en-IE",
  logoUrl: null,
  providerKeys: [],
  theme: {
    primaryColor: "#123456",
    secondaryColor: "#eeeeee",
    borderRadius: 6,
  },
};

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});

afterEach(cleanup);
afterAll(() => i18n.changeLanguage("en"));

function Shell() {
  return (
    <>
      <RouteEffects store={store} categories={[]} />
      <nav>
        <Link to="/">Products</Link>
        <Link to="/cart">Open cart</Link>
      </nav>
      <main id="main-content">
        <Routes>
          <Route path="/" element={<h1>All products</h1>} />
          <Route path="/cart" element={<h1>Your cart</h1>} />
        </Routes>
      </main>
    </>
  );
}

describe("route effects", () => {
  it("updates the store title and focuses the new route heading after navigation", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/"]}>
        <Shell />
      </MemoryRouter>,
    );

    const initialHeading = screen.getByRole("heading", {
      level: 1,
      name: "All products",
    });
    await waitFor(() =>
      expect(document.title).toBe("All products · Test Store"),
    );
    expect(document.activeElement).not.toBe(initialHeading);

    await user.click(screen.getByRole("link", { name: "Open cart" }));
    const cartHeading = screen.getByRole("heading", {
      level: 1,
      name: "Your cart",
    });
    await waitFor(() => expect(document.activeElement).toBe(cartHeading));
    expect(cartHeading.getAttribute("tabindex")).toBe("-1");
    expect(document.title).toBe("Your cart · Test Store");
  });
});
