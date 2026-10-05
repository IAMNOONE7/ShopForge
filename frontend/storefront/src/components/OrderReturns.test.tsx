// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import type { Returns } from "../account";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import type { Store } from "../store";
import { StoreContext } from "../storeContext";
import { OrderReturns } from "./OrderReturns";

const mocks = vi.hoisted(() => ({ getReturns: vi.fn(), requestReturn: vi.fn() }));
vi.mock("../account", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../account")>()),
  getReturns: mocks.getReturns,
  requestReturn: mocks.requestReturn,
}));

const store: Store = {
  id: "store", name: "Store", currency: "CZK", culture: "cs-CZ",
  logoUrl: null, providerKeys: [],
  theme: { primaryColor: "#123456", secondaryColor: "#ffffff", borderRadius: 4 },
};
const returns: Returns = {
  closesAt: "2099-01-01T00:00:00Z",
  returnable: [
    { storeProductId: "hoodie", variantId: "small-red", productName: "Hoodie (S / Red)", quantity: 1 },
    { storeProductId: "hoodie", variantId: "medium-blue", productName: "Hoodie (M / Blue)", quantity: 2 },
  ],
  returns: [],
};

function renderReturns(orderStatus = "Paid") {
  const onReturned = vi.fn();
  render(
    <StoreContext value={store}>
      <OrderReturns number="2026-00023" orderStatus={orderStatus} currency="EUR" onReturned={onReturned} />
    </StoreContext>,
  );
  return onReturned;
}

beforeAll(async () => {
  await initializeI18n();
  await i18n.changeLanguage("en");
});
afterEach(() => {
  cleanup();
  mocks.getReturns.mockReset();
  mocks.requestReturn.mockReset();
});
afterAll(() => i18n.changeLanguage("en"));

describe("account order returns", () => {
  it("retries a lost return response with the same variant payload and key", async () => {
    mocks.getReturns.mockResolvedValue(returns);
    mocks.requestReturn
      .mockRejectedValueOnce(new HttpError("network", null))
      .mockResolvedValueOnce({ ...returns, returnable: [] });
    const user = userEvent.setup();
    renderReturns();
    const blue = await screen.findByRole("combobox", { name: "Quantity to return: Hoodie (M / Blue)" });
    await user.selectOptions(blue, "1");
    await user.click(screen.getByRole("button", { name: "Ask to send back" }));
    expect(await screen.findByText(/Submitting these unchanged details again uses the same request key/)).toBeTruthy();
    expect(mocks.requestReturn).toHaveBeenCalledTimes(1);
    const [number, firstBody, firstKey] = mocks.requestReturn.mock.calls[0];

    await user.click(screen.getByRole("button", { name: "Ask to send back" }));
    expect(await screen.findByText("Your return request was sent.")).toBeTruthy();
    expect(mocks.requestReturn.mock.calls[1]).toEqual([number, firstBody, firstKey]);
  });

  it("requires review before changed return details use a new key", async () => {
    mocks.getReturns.mockResolvedValue(returns);
    mocks.requestReturn
      .mockRejectedValueOnce(new HttpError("network", null))
      .mockRejectedValueOnce(new HttpError("http", 400));
    const user = userEvent.setup();
    renderReturns();
    const red = await screen.findByRole("combobox", { name: "Quantity to return: Hoodie (S / Red)" });
    await user.selectOptions(red, "1");
    await user.click(screen.getByRole("button", { name: "Ask to send back" }));
    await screen.findByText(/same request key/);
    const firstKey = mocks.requestReturn.mock.calls[0][2];

    await user.type(screen.getByRole("textbox", { name: "Why are you sending it back?" }), "Wrong colour");
    expect(screen.getByText("The previous return may have been requested")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Ask to send back" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Start a new return attempt" }));
    await user.click(screen.getByRole("button", { name: "Ask to send back" }));
    await waitFor(() => expect(mocks.requestReturn).toHaveBeenCalledTimes(2));
    expect(mocks.requestReturn.mock.calls[1][2]).not.toBe(firstKey);
    expect(mocks.requestReturn.mock.calls[1][1].reason).toBe("Wrong colour");
  });

  it("keeps an in-progress key and requires review before replacing a mismatched key", async () => {
    mocks.getReturns.mockResolvedValue(returns);
    mocks.requestReturn
      .mockRejectedValueOnce(new HttpError("http", 409, { title: "A request with this key is still in progress" }))
      .mockRejectedValueOnce(new HttpError("http", 422))
      .mockRejectedValueOnce(new HttpError("http", 400));
    const user = userEvent.setup();
    renderReturns();
    const blue = await screen.findByRole("combobox", { name: "Quantity to return: Hoodie (M / Blue)" });
    await user.selectOptions(blue, "1");
    await user.click(screen.getByRole("button", { name: "Ask to send back" }));
    expect(await screen.findByText("Your return request is still processing")).toBeTruthy();
    expect(mocks.getReturns).toHaveBeenCalledTimes(1);
    const firstKey = mocks.requestReturn.mock.calls[0][2];

    await user.click(screen.getByRole("button", { name: "I checked; allow the same retry" }));
    await user.click(screen.getByRole("button", { name: "Ask to send back" }));
    expect(await screen.findByText("Review this return before retrying")).toBeTruthy();
    expect(mocks.requestReturn.mock.calls[1][2]).toBe(firstKey);
    expect(screen.getByRole("button", { name: "Ask to send back" })).toHaveProperty("disabled", true);
    await user.click(screen.getByRole("button", { name: "Start a new return attempt" }));
    await user.click(screen.getByRole("button", { name: "Ask to send back" }));
    await waitFor(() => expect(mocks.requestReturn).toHaveBeenCalledTimes(3));
    expect(mocks.requestReturn.mock.calls[2][2]).not.toBe(firstKey);
  });

  it("requires a selected quantity and sends only the chosen variant once", async () => {
    mocks.getReturns.mockResolvedValue(returns);
    let resolve!: (value: Returns) => void;
    mocks.requestReturn.mockImplementation(() => new Promise<Returns>((accept) => { resolve = accept; }));
    const user = userEvent.setup();
    const onReturned = renderReturns();
    const blue = await screen.findByRole("combobox", { name: "Quantity to return: Hoodie (M / Blue)" });
    const send = screen.getByRole("button", { name: "Ask to send back" });
    expect(send).toHaveProperty("disabled", true);
    await user.selectOptions(blue, "2");
    await user.type(screen.getByRole("textbox", { name: "Why are you sending it back?" }), "  Wrong size  ");
    fireEvent.click(send);
    fireEvent.click(send);
    expect(mocks.requestReturn).toHaveBeenCalledTimes(1);
    expect(mocks.requestReturn).toHaveBeenCalledWith("2026-00023", {
      lines: [{ storeProductId: "hoodie", variantId: "medium-blue", quantity: 2 }],
      reason: "Wrong size",
    }, expect.stringMatching(/^sf-[a-f0-9]{32}$/));

    resolve({ ...returns, returnable: [returns.returnable[0]] });
    expect(await screen.findByText("Your return request was sent.")).toBeTruthy();
    expect(onReturned).toHaveBeenCalledOnce();
    await waitFor(() => expect(mocks.getReturns).toHaveBeenCalledTimes(2));
  });

  it("blocks requests before payment, after the window, and when no lines remain", async () => {
    mocks.getReturns.mockResolvedValue(returns);
    renderReturns("AwaitingPayment");
    expect(await screen.findByText("Returns can be requested after this order is paid or shipped.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Ask to send back" })).toBeNull();
    cleanup();

    mocks.getReturns.mockResolvedValue({ ...returns, closesAt: "2000-01-01T00:00:00Z" });
    renderReturns();
    expect(await screen.findByText("The return window for this order has closed.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Ask to send back" })).toBeNull();
    cleanup();

    mocks.getReturns.mockResolvedValue({ ...returns, returnable: [] });
    renderReturns();
    expect(await screen.findByText("Nothing on this order can be sent back.")).toBeTruthy();
    expect(mocks.requestReturn).not.toHaveBeenCalled();
  });

  it("refreshes eligibility after a conflict and retains the reason", async () => {
    mocks.getReturns
      .mockResolvedValueOnce(returns)
      .mockResolvedValueOnce({ ...returns, returnable: [] });
    mocks.requestReturn.mockRejectedValue(new HttpError("http", 409));
    const user = userEvent.setup();
    renderReturns();
    const red = await screen.findByRole("combobox", { name: "Quantity to return: Hoodie (S / Red)" });
    await user.selectOptions(red, "1");
    await user.type(screen.getByRole("textbox", { name: "Why are you sending it back?" }), "Different colour");
    await user.click(screen.getByRole("button", { name: "Ask to send back" }));
    expect(await screen.findByText("Nothing on this order can be sent back.")).toBeTruthy();
    expect(mocks.requestReturn).toHaveBeenCalledTimes(1);
  });

  it("shows existing returns in the order currency with Czech controls", async () => {
    await i18n.changeLanguage("cs");
    mocks.getReturns.mockResolvedValue({
      ...returns,
      returnable: [],
      returns: [{
        number: "RET-1", status: "Received", requestedAt: "2026-09-01T00:00:00Z",
        refundedAmount: 14.5, lines: [{ productName: "Hoodie (M / Blue)", quantity: 1 }],
      }],
    });
    renderReturns("Refunded");
    expect(await screen.findByText("Vrácení zboží")).toBeTruthy();
    expect(screen.getByText(/14,50/)).toBeTruthy();
    expect(screen.getByText(/€/)).toBeTruthy();
    await i18n.changeLanguage("en");
  });
});
