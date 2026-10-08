// @vitest-environment jsdom
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { api, type Product, type Stock, type StockMovement, type AdminList } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { SessionContext } from "../session";
import { ProductStockPage } from "./ProductStockPage";
import { StockPage } from "./StockPage";

const product: Product = {
  id: "product-a", sku: "SHARED-1", ean: null, weightGrams: null,
  brand: null,
  optionNames: [],
  variants: [{ id: "variant-a", sku: "SHARED-1", ean: null, weightGrams: null, partNumber: null, condition: null, optionValues: [], position: 0 }],
  images: [],
};
const reserved: Stock = {
  variantId: "variant-a", onHand: 8, reserved: 3, available: 5,
};

function movementPage(items: StockMovement[] = []): AdminList<StockMovement> {
  return { items, totalCount: items.length, page: 1, pageSize: 50, hasMore: false };
}

function renderAt(path: string, role = "Owner") {
  return render(
    <SessionContext.Provider value={{
      user: { id: "user-a", email: "owner@example.test", role, tenantId: "tenant-a" },
      logout: async () => undefined,
      logoutPending: false,
      logoutError: null,
    }}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/stock" element={<StockPage />} />
          <Route path="/stock/:variantId" element={<ProductStockPage />} />
        </Routes>
      </MemoryRouter>
    </SessionContext.Provider>,
  );
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});
afterAll(() => i18n.changeLanguage("en"));

describe("shared stock", () => {
  it("pages through older movements, exposes totals, and permits returning after a failed page read", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "products").mockResolvedValue([product]);
    vi.spyOn(api, "stock").mockResolvedValue([]);
    const movements = vi.spyOn(api, "stockMovements")
      .mockResolvedValueOnce({ ...movementPage(), totalCount: 51, hasMore: true })
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce({ ...movementPage([{ occurredAt: "2026-01-01T00:00:00Z", quantity: 1, reason: "Adjustment", reference: "OLDER" }]),
        totalCount: 51, page: 2 })
      .mockResolvedValue({ ...movementPage(), totalCount: 51, hasMore: true });
    renderAt("/stock/variant-a", "Support");
    expect(await screen.findByText("Page 1 · 51 movements")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Older movements" }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.queryByText("Page 1 · 51 movements")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Page 2 · 51 movements")).toBeTruthy();
    expect((screen.getByRole("button", { name: "Older movements" }) as HTMLButtonElement).disabled).toBe(true);
    expect(movements).toHaveBeenLastCalledWith("variant-a", 2, expect.anything());
    await user.click(screen.getByRole("button", { name: "Newer movements" }));
    expect(await screen.findByText("Page 1 · 51 movements")).toBeTruthy();
    expect(movements).toHaveBeenLastCalledWith("variant-a", 1, expect.anything());
  });
  it("never turns a failed stock read into zero, and distinguishes a successful missing record", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "products").mockResolvedValue([product]);
    const stock = vi.spyOn(api, "stock")
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValue([]);
    renderAt("/stock");

    expect(await screen.findByRole("link", { name: "SHARED-1" })).toBeTruthy();
    expect(screen.getAllByText("Unavailable").length).toBeGreaterThan(0);
    expect(screen.queryByText("No stock record")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(stock).toHaveBeenCalledTimes(2));
    expect((await screen.findAllByText("No stock record")).length).toBe(2);
    expect(within(screen.getByRole("table")).getAllByText("0").length).toBe(3);
  });

  it("preserves a below-reserved draft after 409 and reloads latest reservations", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "products").mockResolvedValue([product]);
    const stock = vi.spyOn(api, "stock")
      .mockResolvedValueOnce([reserved])
      .mockResolvedValue([{ ...reserved, reserved: 4, available: 4 }]);
    vi.spyOn(api, "stockMovements").mockResolvedValue(movementPage());
    const setStock = vi.spyOn(api, "setStock")
      .mockRejectedValueOnce(new HttpError("http", 409))
      .mockResolvedValue({ ...reserved, onHand: 6, reserved: 4, available: 2 });
    renderAt("/stock/variant-a");

    const quantity = await screen.findByRole("textbox", { name: "New on-hand quantity" });
    await user.clear(quantity);
    await user.type(quantity, "2");
    expect(screen.getByText(/below the latest 3 reserved/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Save on-hand quantity" }));
    expect(await screen.findByText(/Orders currently reserve more/)).toBeTruthy();
    expect((quantity as HTMLInputElement).value).toBe("2");
    await waitFor(() => expect(stock).toHaveBeenCalledTimes(2));
    expect(screen.getByText(/below the latest 4 reserved/)).toBeTruthy();
    await user.clear(quantity);
    await user.type(quantity, "6");
    await user.click(screen.getByRole("button", { name: "Save on-hand quantity" }));
    await waitFor(() => expect(setStock).toHaveBeenCalledTimes(2));
    expect(setStock.mock.calls).toEqual([["variant-a", 2], ["variant-a", 6]]);
    expect(await screen.findByText("Shared stock was updated.")).toBeTruthy();
  });

  it("keeps movements independent of stock failure and labels signed known and unknown reasons", async () => {
    vi.spyOn(api, "products").mockResolvedValue([product]);
    vi.spyOn(api, "stock").mockRejectedValue(new Error("offline"));
    vi.spyOn(api, "stockMovements").mockResolvedValue(movementPage([
      { occurredAt: "2026-09-29T08:00:00Z", quantity: -2, reason: "Sale", reference: "ORDER-2" },
      { occurredAt: "2026-09-28T08:00:00Z", quantity: 5, reason: "Adjustment", reference: "manual" },
      { occurredAt: "2026-09-27T08:00:00Z", quantity: 1, reason: "NewReason", reference: "EXT-1" },
    ]));
    renderAt("/stock/variant-a", "Support");

    expect(await screen.findByRole("heading", { name: "Stock for SHARED-1" })).toBeTruthy();
    expect(screen.queryByRole("textbox", { name: "New on-hand quantity" })).toBeNull();
    const table = await screen.findByRole("table");
    expect(within(table).getByText("Out −2")).toBeTruthy();
    expect(within(table).getByText("In +5")).toBeTruthy();
    expect(within(table).getByText("Sale")).toBeTruthy();
    expect(within(table).getByText("Other (NewReason)")).toBeTruthy();
    expect(within(table).getByText("ORDER-2")).toBeTruthy();
    expect(screen.getByText(/This information could not be loaded/)).toBeTruthy();
    expect(screen.queryByText("No stock record exists yet.")).toBeNull();
  });

  it("shows an empty movement history separately from successful zero stock", async () => {
    vi.spyOn(api, "products").mockResolvedValue([product]);
    vi.spyOn(api, "stock").mockResolvedValue([]);
    vi.spyOn(api, "stockMovements").mockResolvedValue(movementPage());
    renderAt("/stock/variant-a", "Support");

    expect(await screen.findByText(/No stock record exists yet/)).toBeTruthy();
    expect(await screen.findByText("No stock movements have been recorded for this variant.")).toBeTruthy();
    expect(screen.getByText("Your role can view stock but cannot adjust it.")).toBeTruthy();
  });

  it("keeps two variants of one product separate in the list and detail actions", async () => {
    const user = userEvent.setup();
    const multi: Product = {
      ...product,
      optionNames: ["Size"],
      variants: [
        { ...product.variants[0], sku: "SHARED-S", optionValues: ["Small"] },
        { id: "variant-b", sku: "SHARED-L", ean: null, weightGrams: null, partNumber: null, condition: null, optionValues: ["Large"], position: 1 },
      ],
    };
    vi.spyOn(api, "products").mockResolvedValue([multi]);
    vi.spyOn(api, "stock").mockResolvedValue([
      { variantId: "variant-a", onHand: 3, reserved: 1, available: 2 },
      { variantId: "variant-b", onHand: 9, reserved: 4, available: 5 },
    ]);
    const movements = vi.spyOn(api, "stockMovements").mockResolvedValue(movementPage());
    const setStock = vi.spyOn(api, "setStock").mockResolvedValue({
      variantId: "variant-b", onHand: 10, reserved: 4, available: 6,
    });
    renderAt("/stock");

    const table = await screen.findByRole("table");
    const small = within(table).getByRole("row", { name: /SHARED-S Size: Small/ });
    const large = within(table).getByRole("row", { name: /SHARED-L Size: Large/ });
    expect(within(small).getByText("3")).toBeTruthy();
    expect(within(large).getByText("9")).toBeTruthy();
    expect(within(table).getByRole("link", { name: "SHARED-L" }).getAttribute("href")).toBe("/stock/variant-b");

    await user.click(within(table).getByRole("link", { name: "SHARED-L" }));
    expect(await screen.findByRole("heading", { name: "Stock for SHARED-L" })).toBeTruthy();
    expect(screen.getByText("Size: Large")).toBeTruthy();
    await waitFor(() => expect(movements).toHaveBeenCalledWith("variant-b", 1, expect.anything()));
    const quantity = screen.getByRole("textbox", { name: "New on-hand quantity" });
    await user.clear(quantity);
    await user.type(quantity, "10");
    await user.click(screen.getByRole("button", { name: "Save on-hand quantity" }));
    await waitFor(() => expect(setStock).toHaveBeenCalledWith("variant-b", 10));
  });

  it("rejects fractional adjustments and serializes pending saves", async () => {
    const user = userEvent.setup();
    vi.spyOn(api, "products").mockResolvedValue([product]);
    vi.spyOn(api, "stock").mockResolvedValue([reserved]);
    vi.spyOn(api, "stockMovements").mockResolvedValue(movementPage());
    let resolve!: (value: Stock) => void;
    const pending = new Promise<Stock>((done) => { resolve = done; });
    const setStock = vi.spyOn(api, "setStock").mockReturnValue(pending);
    renderAt("/stock/variant-a");

    const quantity = await screen.findByRole("textbox", { name: "New on-hand quantity" });
    await user.clear(quantity);
    await user.type(quantity, "2.5");
    await user.click(screen.getByRole("button", { name: "Save on-hand quantity" }));
    expect(setStock).not.toHaveBeenCalled();
    expect(screen.getByText("Enter a whole on-hand quantity from zero.")).toBeTruthy();
    await user.clear(quantity);
    await user.type(quantity, "9");
    const save = screen.getByRole("button", { name: "Save on-hand quantity" });
    await user.click(save);
    await user.click(save);
    expect(setStock).toHaveBeenCalledTimes(1);
    resolve({ ...reserved, onHand: 9, available: 6 });
    expect(await screen.findByText("Shared stock was updated.")).toBeTruthy();
  });
});
