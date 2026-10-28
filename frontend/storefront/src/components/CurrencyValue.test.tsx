// @vitest-environment jsdom
import { cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { formatCurrency } from "../utils/format";
import { CurrencyValue } from "./CurrencyValue";

afterEach(cleanup);

describe("currency values", () => {
  it.each([
    ["cs-CZ", "CZK"], ["en-IE", "EUR"], ["en-US", "USD"], ["cs-CZ", "USD"],
    ["ja-JP", "JPY"], ["en-KW", "KWD"], ["ar-KW", "KWD"], ["en-GB", "GBP"],
  ])("preserves %s/%s formatting and keeps currency and fractional digits intact", (culture, currency) => {
    for (const value of [-1234567.891, -0, 0, 1.2, 1234567890.123]) {
      const view = render(<CurrencyValue value={value} culture={culture} currency={currency} />);
      expect(view.container.textContent).toBe(formatCurrency(value, culture, currency));
      expect(view.container.querySelector("bdi")?.lang).toBe(culture);
      const groups = [...view.container.querySelectorAll(".currency-value-group")].map(node => node.textContent ?? "");
      const parts = new Intl.NumberFormat(culture, { style: "currency", currency }).formatToParts(value);
      for (const part of parts.filter(part => part.type === "currency" || part.type === "fraction")) {
        expect(groups.some(group => group.includes(part.value))).toBe(true);
      }
      const lastInteger = parts.map(part => part.type).lastIndexOf("integer");
      const numberEnd = parts.slice(lastInteger).filter(part => ["integer", "decimal", "fraction"].includes(part.type)).map(part => part.value).join("");
      expect(groups.some(group => group.includes(numberEnd))).toBe(true);
      view.unmount();
    }
  });

  it("keeps the displayed discount sign with the first group without changing the value", () => {
    const { container } = render(<CurrencyValue value={1234.56} culture="cs-CZ" currency="CZK" prefix="−" />);
    expect(container.textContent).toBe("−" + formatCurrency(1234.56, "cs-CZ", "CZK"));
    expect(container.querySelector(".currency-value-group")?.textContent?.startsWith("−")).toBe(true);
  });

  it("updates culture, currency and value without retaining a prior store's format", () => {
    const view = render(<CurrencyValue value={12} culture="en-IE" currency="EUR" />);
    view.rerender(<CurrencyValue value={12.345} culture="cs-CZ" currency="KWD" />);
    expect(view.container.textContent).toBe(formatCurrency(12.345, "cs-CZ", "KWD"));
    expect(view.container.querySelector("bdi")?.lang).toBe("cs-CZ");
  });
});
