// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Outlet, Route, Routes, useLocation, useNavigate } from "react-router";
import type { ProductPage, ProductSummary } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { ProductListPage } from "../pages/ProductListPage";
import { StoreContext } from "../storeContext";
import type { Store } from "../store";
import { RouteEffects } from "./RouteEffects";
import { StoreSearch } from "./StoreSearch";

const api = vi.hoisted(() => ({ getProducts: vi.fn<(...args: [string | undefined, URLSearchParams, AbortSignal]) => Promise<ProductPage>>() }));
vi.mock("../api", async (original) => ({
  ...await original<typeof import("../api")>(),
  getProducts: api.getProducts,
}));

const store: Store = {
  id: "store-a", name: "Test store", currency: "CZK", culture: "cs-CZ", logoUrl: null, providerKeys: [],
  theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 },
};
const product: ProductSummary = {
  id: "listing-1", slug: "oak-table", name: "Dubový stůl", price: 100, available: 1,
  rating: 0, reviewCount: 0, imageUrl: null,
};
function page(overrides: Partial<ProductPage> = {}): ProductPage {
  return { items: [product], totalCount: 1, page: 1, pageSize: 24, filters: [], path: [], children: [], pageText: null, ...overrides };
}
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
function Shell() {
  const location = useLocation();
  const navigate = useNavigate();
  return <StoreContext value={store}>
    <RouteEffects store={store} categories={[]} />
    <header><StoreSearch /></header>
    <button onClick={() => navigate(-1)}>Back</button>
    <output data-testid="location">{location.pathname + location.search}</output>
    <main id="main-content"><Outlet context={[]} /></main>
  </StoreContext>;
}
function renderShop(url = "/") {
  render(<MemoryRouter initialEntries={[url]}><Routes><Route element={<Shell />}>
    <Route index element={<ProductListPage />} />
    <Route path="c/:slug" element={<ProductListPage />} />
    <Route path="account/sign-in" element={<h1>Sign in</h1>} />
  </Route></Routes></MemoryRouter>);
}
function searchInput() { return screen.getByRole("searchbox") as HTMLInputElement; }
function location() { return screen.getByTestId("location").textContent!; }

beforeAll(initializeI18n);
beforeEach(async () => {
  await i18n.changeLanguage("en");
  api.getProducts.mockReset().mockResolvedValue(page());
});
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });

describe("shopper search", () => {
  it("submits a trimmed, encoded global query with Enter and discards the old category, facets, sort and page", async () => {
    const user = userEvent.setup();
    renderShop("/c/tables?category=tables&f.material=oak&sort=price&page=4");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    expect(screen.getByRole("search", { name: "Search products" })).toBeTruthy();
    const term = 'oak & brass + "table"?';
    await user.type(searchInput(), `  ${term}  {Enter}`);
    expect(location()).toBe(`/?${new URLSearchParams({ q: term })}`);
    await screen.findByRole("link", { name: /Dubový stůl/ });
    expect(api.getProducts.mock.lastCall?.[0]).toBeUndefined();
    expect(api.getProducts.mock.lastCall?.[1].get("q")).toBe(term);
    expect(searchInput().value).toBe(term);
    const heading = screen.getByRole("heading", { level: 1, name: `Search results for “${term}”` });
    expect(document.activeElement).toBe(heading);
    expect(document.title).toBe(`Search results for “${term}” · Test store`);
    expect((screen.getByRole("combobox", { name: "Sort by" }) as HTMLSelectElement).value).toBe("relevance");
  });

  it("validates trimmed short and overlong input without navigation, and permits the exact bounds", async () => {
    const user = userEvent.setup();
    renderShop("/?sort=price");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    expect(searchInput().minLength).toBe(2);
    expect(searchInput().maxLength).toBe(200);
    for (const term of [" a ", "a".repeat(201)]) {
      fireEvent.change(searchInput(), { target: { value: term } });
      fireEvent.submit(screen.getByRole("search"));
      expect(location()).toBe("/?sort=price");
      expect(screen.getByRole("alert").textContent).toContain("2–200");
      expect(searchInput().getAttribute("aria-invalid")).toBe("true");
      expect(document.activeElement).toBe(searchInput());
    }
    for (const term of ["ab", "a".repeat(200)]) {
      fireEvent.change(searchInput(), { target: { value: term } });
      await user.click(searchInput());
      await user.keyboard("{Enter}");
      expect(new URLSearchParams(location().split("?")[1]).get("q")).toBe(term);
      await screen.findByRole("link", { name: /Dubový stůl/ });
      expect(screen.queryByRole("alert")).toBeNull();
    }
  });

  it.each(["clear button", "empty submission"])("clears the applied search and category context through %s", async (method) => {
    const user = userEvent.setup();
    renderShop("/c/tables?q=oak&f.material=oak&sort=relevance&page=2");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    if (method === "clear button") {
      await user.click(within(screen.getByRole("search")).getByRole("button", { name: "Clear search" }));
    } else {
      await user.clear(searchInput());
      await user.type(searchInput(), "   {Enter}");
    }
    expect(location()).toBe("/");
    expect(searchInput().value).toBe("");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    expect(screen.getByRole("option", { name: "Default order" })).toBeTruthy();
    expect(screen.queryByRole("option", { name: "Relevance" })).toBeNull();
  });

  it("restores URL queries and results with Back, and never searches while typing", async () => {
    const user = userEvent.setup();
    renderShop("/?q=oak");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    await user.clear(searchInput());
    await user.type(searchInput(), "brass");
    expect(location()).toBe("/?q=oak");
    expect(api.getProducts).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("heading", { name: "Search results for “oak”" })).toBeTruthy();
    await user.keyboard("{Enter}");
    await screen.findByRole("heading", { name: "Search results for “brass”" });
    await user.click(screen.getByRole("button", { name: "Back" }));
    expect(searchInput().value).toBe("oak");
    expect(location()).toBe("/?q=oak");
    await screen.findByRole("heading", { name: "Search results for “oak”" });
  });

  it("clears an unapplied draft without losing keyboard focus or adding another home visit", async () => {
    const user = userEvent.setup();
    renderShop();
    await screen.findByRole("link", { name: /Dubový stůl/ });
    await user.type(searchInput(), "unsent");
    await user.click(within(screen.getByRole("search")).getByRole("button", { name: "Clear search" }));
    expect(searchInput().value).toBe("");
    expect(document.activeElement).toBe(searchInput());
    await user.keyboard("{Enter}");
    expect(document.activeElement).toBe(searchInput());
    expect(api.getProducts).toHaveBeenCalledTimes(1);
  });

  it("retains q and compatible facets on filter, sort and paging changes, with relevance selectable", async () => {
    const user = userEvent.setup();
    api.getProducts.mockImplementation(async (_, params) => page({
      totalCount: 48, page: Number(params.get("page") ?? 1),
      filters: [{ code: "material", name: "Materiál", type: "select", unit: null,
        options: [{ code: "oak", name: "Dub", count: 48, selected: false }],
        min: null, max: null, selectedMin: null, selectedMax: null, selected: null, trueCount: null, falseCount: null }],
    }));
    renderShop("/?q=oak&page=2&pageSize=24&campaign=fall");
    await screen.findByRole("checkbox", { name: "Dub (48)" });
    await user.click(screen.getAllByRole("checkbox", { name: "Dub (48)" })[0]);
    let params = new URLSearchParams(location().split("?")[1]);
    expect(params.get("q")).toBe("oak");
    expect(params.get("page")).toBeNull();
    expect(params.get("f.material")).toBe("oak");
    await user.selectOptions(screen.getByRole("combobox", { name: "Sort by" }), "price");
    await user.click(screen.getByRole("link", { name: "Go to page 2" }));
    params = new URLSearchParams(location().split("?")[1]);
    expect(Object.fromEntries(params)).toEqual({ q: "oak", pageSize: "24", campaign: "fall", "f.material": "oak", sort: "price", page: "2" });
    await user.selectOptions(screen.getByRole("combobox", { name: "Sort by" }), "relevance");
    expect(new URLSearchParams(location().split("?")[1]).get("page")).toBeNull();
    expect(api.getProducts.mock.lastCall?.[1].get("q")).toBe("oak");
    expect(document.activeElement).toBe(screen.getByRole("combobox", { name: "Sort by" }));
  });

  it("gives search-specific zero-result recovery, focusing the search or clearing only filters", async () => {
    const user = userEvent.setup();
    api.getProducts.mockResolvedValue(page({ items: [], totalCount: 0 }));
    renderShop("/?q=zz-nothing");
    const empty = await screen.findByRole("region", { name: "No results for this search" });
    expect(empty.textContent).toContain("Try fewer words");
    await user.click(within(empty).getByRole("button", { name: "Change search" }));
    expect(document.activeElement).toBe(searchInput());
    expect(searchInput().selectionEnd).toBe(searchInput().value.length);
    expect(within(empty).getByRole("link", { name: "Browse all products" }).getAttribute("href")).toBe("/");
    cleanup();
    renderShop("/?q=oak&f.material=brass&sort=price&page=2");
    const filtered = await screen.findByRole("region", { name: "No results for this search" });
    await user.click(within(filtered).getByRole("button", { name: "Clear filters" }));
    expect(location()).toBe("/?q=oak&sort=price");
  });

  it("discards previous-query cards immediately and ignores late responses during successive searches", async () => {
    const user = userEvent.setup();
    const slow = deferred<ProductPage>();
    const fast = deferred<ProductPage>();
    api.getProducts.mockResolvedValueOnce(page()).mockReturnValueOnce(slow.promise).mockReturnValueOnce(fast.promise);
    renderShop("/?q=oak");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    await user.clear(searchInput());
    await user.type(searchInput(), "slow{Enter}");
    expect(screen.queryByRole("link", { name: /Dubový stůl/ })).toBeNull();
    expect(screen.getByRole("heading", { name: "Search results for “slow”" })).toBeTruthy();
    await user.clear(searchInput());
    await user.type(searchInput(), "fast{Enter}");
    expect(api.getProducts.mock.calls[1][2].aborted).toBe(true);
    await act(async () => fast.resolve(page({ items: [{ ...product, id: "new", name: "Fast answer" }] })));
    await screen.findByRole("link", { name: /Fast answer/ });
    await act(async () => slow.resolve(page({ items: [{ ...product, id: "old", name: "Stale answer" }] })));
    expect(screen.queryByRole("link", { name: /Stale answer/ })).toBeNull();
    expect(screen.queryByRole("link", { name: /Dubový stůl/ })).toBeNull();
  });

  it("keeps same-query cards visibly busy during filtering without moving focus to the heading", async () => {
    const user = userEvent.setup();
    const pending = deferred<ProductPage>();
    api.getProducts.mockResolvedValueOnce(page()).mockReturnValueOnce(pending.promise);
    renderShop("/?q=oak");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    const sort = screen.getByRole("combobox", { name: "Sort by" });
    await user.selectOptions(sort, "price");
    expect(screen.getByRole("link", { name: /Dubový stůl/ })).toBeTruthy();
    expect(document.querySelector(".catalog-results")?.getAttribute("aria-busy")).toBe("true");
    expect(document.activeElement).toBe(sort);
    await act(async () => pending.resolve(page()));
  });

  it("retains the applied query on failure and invalid-option recovery, and retries reads", async () => {
    const user = userEvent.setup();
    api.getProducts.mockRejectedValueOnce(new HttpError("http", 400)).mockResolvedValue(page());
    renderShop("/?q=oak&f.missing=x&sort=unknown&page=2");
    await user.click(await screen.findByRole("button", { name: "Remove invalid options" }));
    expect(location()).toBe("/?q=oak");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    cleanup();
    api.getProducts.mockRejectedValueOnce(new HttpError("network", null));
    renderShop("/?q=brass");
    await user.click(await screen.findByRole("button", { name: "Try again" }));
    await screen.findByRole("link", { name: /Dubový stůl/ });
    expect(searchInput().value).toBe("brass");
  });

  it("shows direct URL terms as the server treats them: short ignored, long truncated, categories retained", async () => {
    renderShop("/?q=a");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    expect(screen.getByRole("heading", { name: "All products" })).toBeTruthy();
    expect(screen.queryByRole("option", { name: "Relevance" })).toBeNull();
    expect(screen.getByText(/Use 2–200 characters/, { selector: "p" })).toBeTruthy();
    cleanup();
    const long = "x".repeat(201);
    renderShop(`/?q=${long}`);
    await screen.findByRole("link", { name: /Dubový stůl/ });
    expect(screen.getByRole("heading", { name: `Search results for “${long.slice(0, 200)}”` })).toBeTruthy();
    expect(screen.getByText("Only the first 200 characters are searched.")).toBeTruthy();
    cleanup();
    api.getProducts.mockResolvedValue(page({ path: [{ name: "Stoly", slug: "tables", parentSlug: null }] }));
    renderShop("/c/tables?q=oak");
    await screen.findByRole("heading", { name: "Stoly" });
    expect(screen.getByText("Search results for “oak”")).toBeTruthy();
    expect(searchInput().value).toBe("oak");
  });

  it("localizes search and validation in Czech while leaving merchant product names unchanged", async () => {
    const user = userEvent.setup();
    await i18n.changeLanguage("cs");
    renderShop("/?q=dub");
    await screen.findByRole("link", { name: /Dubový stůl/ });
    expect(screen.getByRole("searchbox", { name: "Hledat produkty" })).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Výsledky hledání pro „dub“" })).toBeTruthy();
    await user.clear(searchInput());
    await user.type(searchInput(), "x{Enter}");
    expect(screen.getByRole("alert").textContent).toContain("Zadejte 2–200 znaků");
    await user.clear(searchInput());
    api.getProducts.mockResolvedValue(page({ items: [], totalCount: 0 }));
    await user.type(searchInput(), "nicnic{Enter}");
    await screen.findByRole("region", { name: "Pro toto hledání nejsou výsledky" });
  });

  it("does not mistake a non-catalog q parameter for an applied product query", () => {
    renderShop("/account/sign-in?q=private");
    expect(searchInput().value).toBe("");
    expect(api.getProducts).not.toHaveBeenCalled();
  });
});
