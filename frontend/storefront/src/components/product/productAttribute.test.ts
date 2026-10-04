import { describe, expect, it } from "vitest";
import type { ProductAttribute } from "../../api";
import type { Store } from "../../store";
import { formatProductAttribute } from "./productAttribute";

const store: Store = {
  id: "store",
  name: "Store",
  currency: "EUR",
  culture: "cs-CZ",
  logoUrl: null,
  providerKeys: [],
  theme: { primaryColor: "#000000", secondaryColor: "#ffffff", borderRadius: 4 },
};

function attribute(
  type: ProductAttribute["type"],
  value: ProductAttribute["value"],
  unit: string | null = null,
): ProductAttribute {
  return { code: "value", name: "Value", type, value, unit };
}

describe("formatProductAttribute", () => {
  it("uses store culture for numbers and dates while preserving units and option labels", () => {
    expect(formatProductAttribute(attribute("decimal", 12.5, "kg"), store, "Ano", "Ne"))
      .toBe("12,5 kg");
    expect(formatProductAttribute(attribute("date", "2026-09-26"), store, "Ano", "Ne"))
      .toBe("26. 9. 2026");
    expect(
      formatProductAttribute(
        attribute("multiSelect", ["Solid oak", "Natural oil"]),
        store,
        "Ano",
        "Ne",
      ),
    ).toBe("Solid oak, Natural oil");
  });

  it("uses localized interface text for booleans", () => {
    expect(formatProductAttribute(attribute("boolean", true), store, "Ano", "Ne"))
      .toBe("Ano");
    expect(formatProductAttribute(attribute("boolean", false), store, "Ano", "Ne"))
      .toBe("Ne");
  });
});
