// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { CustomerContext, type CustomerState } from "../customerContext";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { ProductReviews } from "./ProductReviews";

const mocks = vi.hoisted(() => ({ getReviews: vi.fn(), writeReview: vi.fn() }));
vi.mock("../api", async (original) => ({ ...await original<typeof import("../api")>(), getReviews: mocks.getReviews }));
vi.mock("../account", async (original) => ({ ...await original<typeof import("../account")>(), writeReview: mocks.writeReview }));
const store: Store = { id: "store", name: "Shop", culture: "cs-CZ", currency: "CZK", logoUrl: null, providerKeys: [],
  theme: { primaryColor: "#8b5a2b", secondaryColor: "#f5f0e8", borderRadius: 8 } };
const buyer: CustomerState = { status: "authenticated", customer: { email: "alice@example.test", firstName: "Alice", lastName: "Novak", phone: null },
  error: null, apply: vi.fn(), retry: vi.fn() };
beforeAll(initializeI18n);
beforeEach(async () => { vi.resetAllMocks(); await i18n.changeLanguage("en"); });
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });

function reviews(count = 0) {
  return render(<StoreContext value={store}><CustomerContext value={buyer}>
    <ProductReviews slug="chair" reviewCount={count} />
  </CustomerContext></StoreContext>);
}

describe("product reviews", () => {
  it("shows only returned reviews and honors server writing eligibility for a signed-in customer", async () => {
    mocks.getReviews.mockResolvedValue({ canWrite: false, reviews: [
      { author: "Marta", rating: 4, text: "<b>A published review</b>", writtenAt: "2026-10-07T09:30:00Z" },
    ] });
    reviews(1);
    expect(await screen.findByText("<b>A published review</b>")).toBeTruthy();
    expect(document.querySelector(".review-text b")).toBeNull();
    expect(document.querySelector("time")?.getAttribute("datetime")).toBe("2026-10-07T09:30:00Z");
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(mocks.writeReview).not.toHaveBeenCalled();
  });

  it("locks a pending review, retains a failed draft and waits for moderation after a successful submission", async () => {
    const user = userEvent.setup();
    mocks.getReviews.mockResolvedValueOnce({ canWrite: true, reviews: [] }).mockResolvedValue({ canWrite: false, reviews: [] });
    let reject!: (error: Error) => void;
    mocks.writeReview.mockImplementationOnce(() => new Promise<void>((_, fail) => { reject = fail; }));
    reviews();
    const draft = await screen.findByRole("textbox", { name: "Your review" });
    await user.type(draft, "A solid chair.");
    const submit = screen.getByRole("button", { name: "Send review" });
    await user.click(submit);
    await user.click(submit);
    expect(submit).toHaveProperty("disabled", true);
    expect(mocks.writeReview).toHaveBeenCalledTimes(1);
    expect(mocks.writeReview).toHaveBeenCalledWith("chair", { rating: 5, text: "A solid chair.", author: "Alice N." });
    reject(new Error("unavailable"));
    await screen.findByRole("alert");
    expect(draft).toHaveProperty("value", "A solid chair.");
    mocks.writeReview.mockResolvedValue(undefined);
    await user.click(submit);
    expect(await screen.findByText(/your review will appear once/)).toBeTruthy();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(document.querySelector(".review")).toBeNull();
    expect(mocks.writeReview).toHaveBeenCalledTimes(2);
  });

  it("keeps the review destination while loading and reports a failed read before an explicit retry", async () => {
    const user = userEvent.setup();
    await i18n.changeLanguage("cs");
    let reject!: (error: Error) => void;
    mocks.getReviews.mockImplementationOnce(() => new Promise((_, fail) => { reject = fail; }));
    mocks.getReviews.mockResolvedValue({ canWrite: false, reviews: [] });
    reviews(1);
    expect(screen.getByText("Načítání recenzí…")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Co říkají zákazníci" }).id).toBe("product-reviews-heading");
    reject(new Error("unavailable"));
    await screen.findByRole("alert");
    expect(screen.queryByText("Zatím bez recenzí")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Zkusit znovu" }));
    expect(await screen.findByText("Zatím bez recenzí")).toBeTruthy();
    expect(mocks.getReviews).toHaveBeenCalledTimes(2);
  });
});
