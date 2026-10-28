// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { Link, MemoryRouter, Route, Routes } from "react-router";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { RouteEffects } from "./RouteEffects";
import { ShoppingPageLoading } from "./ShoppingPageLoading";

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
  it("uses the store name once for the discovery home title", async () => {
    render(<MemoryRouter><RouteEffects store={store} categories={[]} />
      <main id="main-content"><h1>{store.name}</h1></main>
    </MemoryRouter>);
    await waitFor(() => expect(document.title).toBe(store.name));
  });

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

  it.each(["cart", "checkout"] as const)("waits for the usable %s heading instead of focusing its loading replacement", async (kind) => {
    function Page() {
      const [ready, setReady] = useState(false);
      return ready ? <h1>Ready to shop</h1> : <>
        <ShoppingPageLoading kind={kind} label="Loading shopping page" />
        <button onClick={() => setReady(true)}>Finish loading</button>
      </>;
    }
    const user = userEvent.setup();
    render(<MemoryRouter><RouteEffects store={store} categories={[]} />
      <Link to="/next">Continue shopping</Link><main id="main-content"><Routes>
        <Route path="/" element={<h1>Products</h1>} />
        <Route path="/next" element={<Page />} />
      </Routes></main></MemoryRouter>);
    await user.click(screen.getByRole("link", { name: "Continue shopping" }));
    const loadingHeading = screen.getByRole("heading", { level: 1 });
    expect(document.activeElement).not.toBe(loadingHeading);
    expect(screen.getByRole("status").textContent).toContain("Loading shopping page");
    await user.click(screen.getByRole("button", { name: "Finish loading" }));
    const heading = screen.getByRole("heading", { name: "Ready to shop" });
    await waitFor(() => expect(document.activeElement).toBe(heading));
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("focuses a retained heading when only its busy ancestor becomes ready", async () => {
    function Page() {
      const [busy, setBusy] = useState(true);
      return <section aria-busy={busy}><h1>Shopping page</h1>
        <button onClick={() => setBusy(false)}>Finish loading</button>
      </section>;
    }
    const user = userEvent.setup();
    render(<MemoryRouter><RouteEffects store={store} categories={[]} />
      <Link to="/next">Continue shopping</Link><main id="main-content"><Routes>
        <Route path="/" element={<h1>Products</h1>} />
        <Route path="/next" element={<Page />} />
      </Routes></main></MemoryRouter>);
    await user.click(screen.getByRole("link", { name: "Continue shopping" }));
    const heading = screen.getByRole("heading", { name: "Shopping page" });
    expect(document.activeElement).not.toBe(heading);
    await user.click(screen.getByRole("button", { name: "Finish loading" }));
    await waitFor(() => expect(document.activeElement).toBe(heading));
  });
});
