// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { api, type ShippingMethod } from "../api";
import { HttpError } from "../api/http";
import { i18n, initializeI18n } from "../i18n";
import { useAction } from "../useAction";
import { RequestError } from "./ui/RequestError";
import { MethodsSection } from "./MethodsSection";

const zbox: ShippingMethod = {
  code: "z-box",
  name: "Z-BOX",
  providerKey: "packeta",
  price: 59,
  vatRate: 21,
  isActive: true,
  requiresPickupPoint: true,
  maxWeightGrams: null,
  countries: [],
  taxClass: null,
};

const money = new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" });
const run = (change: () => Promise<unknown>) => change().then(() => undefined);

function mockReads(method: ShippingMethod) {
  vi.spyOn(api, "paymentProviders").mockResolvedValue(["manual"]);
  vi.spyOn(api, "shippingProviders").mockResolvedValue(["manual", "packeta"]);
  vi.spyOn(api, "paymentMethods").mockResolvedValue([]);
  vi.spyOn(api, "shippingMethods").mockResolvedValue([method]);
  vi.spyOn(api, "pickupPoints").mockResolvedValue([]);
}

function EditableMethods() {
  const [error, run] = useAction(() => undefined);
  return <><MethodsSection storeId="store-a" money={money} run={run} />{error !== null && <RequestError error={error} operation="write" />}</>;
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

describe("MethodsSection", () => {
  it.each(["standard", null])("preserves tax class %s, zero prices and false flags after a failed edit", async (taxClass) => {
    const user = userEvent.setup();
    const method = { ...zbox, taxClass, price: 0, vatRate: 0, isActive: false, requiresPickupPoint: false, maxWeightGrams: 5000, countries: ["CZ"] };
    mockReads(method);
    const saved = vi.spyOn(api, "updateShippingMethod")
      .mockRejectedValueOnce(new HttpError("http", 400, { errors: { name: ["Invalid name"] } }))
      .mockResolvedValue(method);
    render(<EditableMethods />);
    const form = (await screen.findByRole("spinbutton", { name: /Weight limit/ })).closest("form")!;
    const name = form.querySelector<HTMLInputElement>('input[name="name"]')!;
    await user.clear(name); await user.type(name, "Parcel");
    await user.click(screen.getAllByRole("button", { name: "Save" })[0]);
    await screen.findByRole("alert");
    expect(name).toHaveProperty("value", "Parcel");
    await user.click(screen.getAllByRole("button", { name: "Save" })[0]);
    await waitFor(() => expect(saved).toHaveBeenCalledTimes(2));
    const input = { name: "Parcel", price: 0, vatRate: 0, isActive: false, requiresPickupPoint: false, maxWeightGrams: 5000, countries: ["CZ"], taxClass };
    expect(saved.mock.calls.map(([, , payload]) => payload)).toEqual([input, input]);
  });

  // The limits are what keep a method off the shelf for a parcel it cannot take, and until now only the API
  // could set them.
  it("saves a weight limit and the countries a method serves", async () => {
    const user = userEvent.setup();
    mockReads(zbox);
    const saved = vi.spyOn(api, "updateShippingMethod").mockResolvedValue(zbox);

    render(<MethodsSection storeId="store-a" money={money} run={run} />);

    const weight = await screen.findByRole("spinbutton", { name: /Weight limit/ });
    await user.type(weight, "5000");
    await user.type(screen.getByRole("textbox", { name: /Countries/ }), "cz sk");
    await user.click(screen.getAllByRole("button", { name: "Save" })[0]);

    await waitFor(() => expect(saved).toHaveBeenCalledTimes(1));
    expect(saved.mock.calls[0][2]).toMatchObject({
      maxWeightGrams: 5000,
      countries: ["CZ", "SK"],
    });
  });

  // An empty box is "no limit", which is a different thing from a limit of nothing.
  it("treats empty limits as no limit rather than zero", async () => {
    const user = userEvent.setup();
    mockReads({ ...zbox, maxWeightGrams: 5000, countries: ["CZ"] });
    const saved = vi.spyOn(api, "updateShippingMethod").mockResolvedValue(zbox);

    render(<MethodsSection storeId="store-a" money={money} run={run} />);

    await user.clear(await screen.findByRole("spinbutton", { name: /Weight limit/ }));
    await user.clear(screen.getByRole("textbox", { name: /Countries/ }));
    await user.click(screen.getAllByRole("button", { name: "Save" })[0]);

    await waitFor(() => expect(saved).toHaveBeenCalledTimes(1));
    expect(saved.mock.calls[0][2]).toMatchObject({
      maxWeightGrams: null,
      countries: [],
  taxClass: null,
    });
  });

  it("shows what a method is limited to when it opens", async () => {
    mockReads({ ...zbox, maxWeightGrams: 5000, countries: ["CZ", "SK"] });

    render(<MethodsSection storeId="store-a" money={money} run={run} />);

    const weight = await screen.findByRole("spinbutton", { name: /Weight limit/ });
    expect(weight).toHaveProperty("value", "5000");
    expect(screen.getByRole("textbox", { name: /Countries/ })).toHaveProperty("value", "CZ SK");
  });
});
