// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { CustomerContext, type CustomerState } from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import { WishlistButton } from "./WishlistButton";
import type { WishlistItem } from "../account";

const mocks = vi.hoisted(() => ({ getWishlist: vi.fn(), addToWishlist: vi.fn(), removeFromWishlist: vi.fn() }));
vi.mock("../account", async (original) => ({ ...await original<typeof import("../account")>(), ...mocks }));
const guest: CustomerState = { status: "guest", customer: null, error: null, apply: vi.fn(), retry: vi.fn() };
const buyer: CustomerState = { ...guest, status: "authenticated", customer: { email: "alice@example.test", firstName: "Alice", lastName: "Novak", phone: null } };
const item: WishlistItem = { storeProductId: "chair", name: "Oak chair", slug: "chair", price: 649, imageUrl: null };
beforeAll(initializeI18n);
beforeEach(async () => { vi.resetAllMocks(); await i18n.changeLanguage("en"); });
afterEach(cleanup);

function button(product: string, customer = buyer) {
  return <CustomerContext value={customer}><WishlistButton storeProductId={product} /></CustomerContext>;
}

describe("product wishlist signal", () => {
  it("requires an account, waits for server membership and serializes writes without claiming failed success", async () => {
    const user = userEvent.setup();
    const view = render(button("chair", guest));
    expect(screen.queryByRole("button")).toBeNull();
    expect(mocks.getWishlist).not.toHaveBeenCalled();
    mocks.getWishlist.mockResolvedValue([item]);
    view.rerender(button("chair"));
    const wanted = await screen.findByRole("button", { name: "♥ On your wishlist" });
    expect(wanted.getAttribute("aria-pressed")).toBe("true");
    let resolve!: () => void;
    mocks.removeFromWishlist.mockImplementation(() => new Promise<void>((complete) => { resolve = complete; }));
    await user.click(wanted);
    await user.click(wanted);
    expect(mocks.removeFromWishlist).toHaveBeenCalledTimes(1);
    expect(mocks.removeFromWishlist).toHaveBeenCalledWith("chair");
    expect(wanted).toHaveProperty("disabled", true);
    resolve();
    const save = await screen.findByRole("button", { name: "♡ Save for later" });
    mocks.addToWishlist.mockRejectedValue(new Error("unavailable"));
    await user.click(save);
    await screen.findByRole("alert");
    expect(save.getAttribute("aria-pressed")).toBe("false");
    expect(mocks.addToWishlist).toHaveBeenCalledWith("chair");
  });

  it("discards a previous product's late membership read when the page changes", async () => {
    let resolve!: (value: WishlistItem[]) => void;
    mocks.getWishlist.mockImplementationOnce(() => new Promise((complete) => { resolve = complete; }));
    mocks.getWishlist.mockResolvedValue([]);
    const view = render(button("chair"));
    view.rerender(button("table"));
    await screen.findByRole("button", { name: "♡ Save for later" });
    resolve([item]);
    await waitFor(() => expect(screen.getByRole("button").getAttribute("aria-pressed")).toBe("false"));
    expect(mocks.getWishlist.mock.calls[0][0].aborted).toBe(true);
  });
});
