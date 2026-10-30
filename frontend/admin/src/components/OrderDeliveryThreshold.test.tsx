// @vitest-environment jsdom
import { cleanup, render } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it } from "vitest";
import { i18n, initializeI18n } from "../i18n";
import { orderDetail } from "../test/orderFixtures";
import { OrderItems } from "./OrderDetailRead";

beforeAll(initializeI18n);
afterEach(async () => { cleanup(); await i18n.changeLanguage("en"); });

describe("recorded order delivery", () => {
  it.each(["cs", "en"])("explains the saved threshold in %s using each order's currency", async language => {
    await i18n.changeLanguage(language);
    for (const [currency, freeShippingThreshold] of [["CZK", 200], ["JPY", 1234], ["KWD", 123.456]] as const) {
      const saved = { ...orderDetail, currency, shippingPrice: 0, freeShippingThreshold };
      const view = render(<OrderItems order={saved} />);
      const amount = new Intl.NumberFormat(language === "cs" ? "cs-CZ" : "en-GB", { style: "currency", currency }).format(freeShippingThreshold);
      expect(view.container.querySelector(".order-free-delivery")?.textContent).toBe(i18n.t("orderDetails:freeShippingRecorded").replace("<amount/>", amount));
      const groups = [...view.container.querySelectorAll(".order-free-delivery .currency-value-group")].map(node => node.textContent ?? "");
      const parts = new Intl.NumberFormat(language === "cs" ? "cs-CZ" : "en-GB", { style: "currency", currency }).formatToParts(freeShippingThreshold);
      const end = parts.slice(parts.map(part => part.type).lastIndexOf("integer")).filter(part => ["integer", "decimal", "fraction"].includes(part.type)).map(part => part.value).join("");
      expect(groups.some(group => group.includes(end))).toBe(true);
      view.rerender(<OrderItems order={{ ...saved, freeShippingThreshold: null }} />);
      expect(view.container.querySelector(".order-free-delivery")).toBeNull();
      view.unmount();
    }
  });

  it.each([undefined, null, 0, -1, NaN])("does not infer a threshold from a free method or code (%s)", freeShippingThreshold => {
    const view = render(<OrderItems order={{ ...orderDetail, shippingPrice: 0, freeShippingThreshold }} />);
    expect(view.container.querySelector(".order-free-delivery")).toBeNull();
  });
});
