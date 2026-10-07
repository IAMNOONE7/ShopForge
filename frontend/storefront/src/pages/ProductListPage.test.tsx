// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
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
import type { ProductPage } from "../api";
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
afterEach(cleanup);
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
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe("ProductListPage recovery", () => {
  it("adds discovery only to a fresh home visit and preserves the catalog empty state", () => {
    request.state = { status: "ready", data: { items: [], totalCount: 0, page: 1, pageSize: 24, filters: [] },
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
