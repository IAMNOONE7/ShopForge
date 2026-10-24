import { describe, expect, it } from "vitest";
import { measurementFields, physicalInput, validateProduct } from "./productValidation";
import { validateVariant, variantDraft } from "./variantValidation";

const measured = { ...variantDraft(), sku: "MEASURED", weightGrams: "1",
  lengthMm: "200", widthMm: "150", heightMm: "100" };

describe("required package measurements", () => {
  it.each(measurementFields)("requires %s to be a positive whole number in both forms", (field) => {
    for (const value of ["", " ", "0", "-1", "1.5", "1,5", "1e2", "1 mm", "NaN", "Infinity"]) {
      const draft = { ...measured, [field]: value };
      expect(validateProduct(draft, true).map((issue) => issue.field), value).toEqual([field]);
      expect(validateVariant(draft, []).map((issue) => issue.field), value).toEqual([field]);
    }
  });

  it.each(measurementFields)("checks the inclusive upper bound for %s without rounding or clamping", (field) => {
    const maximum = field === "weightGrams" ? 2147483647 : 10000;
    expect(validateProduct({ ...measured, [field]: String(maximum) }, true)).toEqual([]);
    expect(validateProduct({ ...measured, [field]: String(maximum + 1) }, true)
      .map((issue) => issue.field)).toEqual([field]);
    expect(validateProduct({ ...measured, [field]: "1" }, true)).toEqual([]);
  });

  it("requires every measurement, including when all three sizes are missing", () => {
    expect(validateProduct({ ...variantDraft(), sku: "OLD" }, false).map((issue) => issue.field))
      .toEqual([...measurementFields]);
    expect(validateVariant({ ...measured, lengthMm: "", heightMm: "" }, []).map((issue) => issue.field))
      .toEqual(["lengthMm", "heightMm"]);
  });

  it.each([false, true])("sends exact integer measurements and shipsAlone=%s", (shipsAlone) => {
    const input = physicalInput({ ...measured, weightGrams: " 0001 ", lengthMm: " 10000 ", shipsAlone });
    expect(input).toMatchObject({ weightGrams: 1, lengthMm: 10000, widthMm: 150, heightMm: 100, shipsAlone });
  });
});
