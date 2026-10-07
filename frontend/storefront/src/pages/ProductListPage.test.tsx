// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
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
import {
  MemoryRouter,
  Outlet,
  Route,
  Routes,
  useLocation,
} from "react-router";
import type { Facet, ProductPage } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import type { RequestState } from "../useRequest";
import { ProductListPage } from "./ProductListPage";
import { StoreContext } from "../storeContext";
import type { Store } from "../store";

const store: Store = {
  id: "store-a", name: "Test Store", currency: "CZK", culture: "cs-CZ",
  logoUrl: null, providerKeys: [],
  theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 },
};

const request = vi.hoisted(() => ({
  state: null as RequestState<ProductPage> | null,
}));

vi.mock("../useRequest", () => ({
  useRequest: () => request.state,
}));

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });
afterAll(() => i18n.changeLanguage("en"));

function CatalogLayout() {
  return <StoreContext value={store}><Outlet context={[]} /></StoreContext>;
}

function Location() {
  const location = useLocation();
  return <output>{location.pathname + location.search}</output>;
}

function renderPage(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route element={<CatalogLayout />}>
          <Route
            index
            element={
              <>
                <ProductListPage />
                <Location />
              </>
            }
          />
          <Route path="c/:slug" element={<><ProductListPage /><Location /></>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe("ProductListPage recovery", () => {
  it("uses server ancestry, direct children and plain category text even when the shell list is unavailable", async () => {
    const user = userEvent.setup();
    request.state = { status: "ready", data: {
      items: [], totalCount: 0, page: 1, pageSize: 24, filters: [],
      path: [
        { name: "Furniture", slug: "furniture", parentSlug: null },
        { name: "Tables", slug: "tables", parentSlug: null },
      ],
      children: [{ name: "Dining", slug: "dining", parentSlug: "tables" }],
      pageText: "Shop text.\n\n<script>store content</script>",
    }, refreshing: false, refreshError: null, reload: vi.fn() };
    renderPage("/c/old-tables?sort=price&f.material=oak&page=3");
    expect(screen.getByRole("heading", { level: 1, name: "Tables" })).toBeTruthy();
    const crumbs = screen.getByRole("navigation", { name: "Category breadcrumb" });
    expect(within(crumbs).getByRole("link", { name: "Furniture" }).getAttribute("href")).toBe("/c/furniture");
    expect(within(crumbs).getByText("Tables").getAttribute("aria-current")).toBe("page");
    expect(document.querySelector(".catalog-description script")).toBeNull();
    expect(screen.getByText("<script>store content</script>")).toBeTruthy();
    expect(document.querySelector(".catalog-filter-rail")).toBeNull();
    await user.click(within(screen.getByRole("navigation", { name: "Browse subcategories" })).getByRole("link", { name: "Dining" }));
    expect(screen.getByText("/c/dining")).toBeTruthy();
  });

  it("removes one selected option and clears filters while preserving sort and unrelated URL state", async () => {
    const user = userEvent.setup();
    const facet: Facet = {
      code: "material", name: "Material", type: "multiSelect", unit: null,
      options: [{ code: "oak", name: "Oak", count: 0, selected: true }, { code: "walnut", name: "Walnut", count: 0, selected: true }],
      min: null, max: null, selectedMin: null, selectedMax: null, selected: null, trueCount: null, falseCount: null,
    };
    request.state = { status: "ready", data: { items: [], totalCount: 0, page: 2, pageSize: 24, filters: [facet],
      path: [], children: [], pageText: null }, refreshing: false, refreshError: null, reload: vi.fn() };
    renderPage("/?f.material=oak,walnut&sort=price&page=2&campaign=fall");
    await user.click(screen.getByRole("button", { name: "Remove filter Material: Oak" }));
    expect(screen.getByText("/?f.material=walnut&sort=price&campaign=fall")).toBeTruthy();
    expect((screen.getByRole("checkbox", { name: "Walnut (0)" }) as HTMLInputElement).disabled).toBe(false);
    await user.click(within(screen.getByRole("region", { name: "Active filters" })).getByRole("button", { name: "Clear filters" }));
    expect(screen.getByText("/?sort=price&campaign=fall")).toBeTruthy();
  });

  it("formats numeric filter thresholds using store culture without changing the URL value", async () => {
    await i18n.changeLanguage("cs");
    const facet: Facet = {
      code: "width", name: "Width", type: "decimal", unit: "cm", options: null,
      min: 10, max: 80, selectedMin: 42.1234, selectedMax: null, selected: null, trueCount: null, falseCount: null,
    };
    request.state = { status: "ready", data: { items: [], totalCount: 0, page: 1, pageSize: 24, filters: [facet],
      path: [], children: [], pageText: null }, refreshing: false, refreshError: null, reload: vi.fn() };
    renderPage("/?f.width=42.1234..");
    expect(screen.getByRole("button", { name: "Odebrat filtr Width: od 42,1234 cm" })).toBeTruthy();
    expect(screen.getByText("/?f.width=42.1234..")).toBeTruthy();
  });

  it("recovers from no matches by clearing filters without erasing sort or campaign state", async () => {
    const user = userEvent.setup();
    request.state = { status: "ready", data: {
      items: [], totalCount: 0, page: 1, pageSize: 24, filters: [], path: [], children: [], pageText: null,
    }, refreshing: false, refreshError: null, reload: vi.fn() };
    renderPage("/?f.width=1000..&sort=price&campaign=fall");
    const empty = screen.getByRole("region", { name: "No matching products" });
    await user.click(within(empty).getByRole("button", { name: "Clear filters" }));
    expect(screen.getByText("/?sort=price&campaign=fall")).toBeTruthy();
  });

  it("adds discovery only to a fresh home visit and preserves the catalog empty state", () => {
    request.state = { status: "ready", data: { items: [], totalCount: 0, page: 1, pageSize: 24, filters: [], path: [], children: [], pageText: null },
      refreshing: false, refreshError: null, reload: vi.fn() };
    renderPage("/?campaign=fall");
    expect(screen.getByRole("heading", { level: 1, name: store.name })).toBeTruthy();
    expect(screen.getByRole("heading", { level: 2, name: "All products" })).toBeTruthy();
    expect(screen.getByText("/?campaign=fall")).toBeTruthy();
    expect(document.querySelectorAll("h1")).toHaveLength(1);
  });

  it("opens a sorted root URL directly on its results", () => {
    request.state = { status: "loading", reload: vi.fn() };
    renderPage("/?sort=price");
    expect(screen.getByRole("heading", { level: 1, name: "All products" })).toBeTruthy();
    expect(screen.queryByRole("link", { name: "Explore products" })).toBeNull();
  });

  it("offers page 1 when the requested page is outside the real result range", async () => {
    const user = userEvent.setup();
    request.state = {
      status: "ready",
      data: {
        items: [],
        totalCount: 25,
        page: 9,
        pageSize: 24,
        filters: [],
        path: [], children: [], pageText: null,
      },
      refreshing: false,
      refreshError: null,
      reload: vi.fn(),
    };

    renderPage("/?page=9&sort=price");
    expect(screen.getByRole("heading", { name: "This page is empty" })).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Return to page 1" }));
    expect(screen.getByText("/?sort=price")).toBeTruthy();
  });

  it("removes invalid filters and sort while preserving unrelated URL state", async () => {
    const user = userEvent.setup();
    request.state = {
      status: "error",
      error: new HttpError("http", 400),
      reload: vi.fn(),
    };

    renderPage("/?f.unknown=x&sort=popularity&page=4&campaign=fall");
    await user.click(
      screen.getByRole("button", { name: "Remove invalid options" }),
    );
    expect(screen.getByText("/?campaign=fall")).toBeTruthy();
  });
});
