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
  return <Outlet context={[]} />;
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
