import { describe, expect, it } from "vitest";
import type { Facet } from "../../api";
import {
  activeFilterCount,
  facetsForQuery,
  optionValueWithout,
  withParam,
  withoutCatalogQuery,
} from "./filterParams";

const facets: Facet[] = [
  {
    code: "material",
    name: "Material",
    type: "multiSelect",
    unit: null,
    options: [
      { code: "oak", name: "Oak", count: 2, selected: false },
      { code: "walnut", name: "Walnut", count: 0, selected: false },
    ],
    min: null,
    max: null,
    selectedMin: null,
    selectedMax: null,
    trueCount: null,
    falseCount: null,
    selected: null,
  },
  {
    code: "foldable",
    name: "Foldable",
    type: "boolean",
    unit: null,
    options: null,
    min: null,
    max: null,
    selectedMin: null,
    selectedMax: null,
    trueCount: 1,
    falseCount: 3,
    selected: null,
  },
  {
    code: "width",
    name: "Width",
    type: "decimal",
    unit: "cm",
    options: null,
    min: 20,
    max: 120,
    selectedMin: null,
    selectedMax: null,
    trueCount: null,
    falseCount: null,
    selected: null,
  },
];

describe("catalog URL parameters", () => {
  it("resets the page for filter and sort changes but not page navigation", () => {
    const initial = new URLSearchParams("page=4&sort=price");
    expect(withParam(initial, "f.material", "oak").toString()).toBe(
      "sort=price&f.material=oak",
    );
    expect(withParam(initial, "page", "3").toString()).toBe(
      "page=3&sort=price",
    );
  });

  it("removes invalid catalog state while preserving unrelated parameters", () => {
    const cleaned = withoutCatalogQuery(
      new URLSearchParams(
        "f.unknown=x&sort=popularity&page=8&pageSize=6&campaign=fall",
      ),
    );
    expect(cleaned.toString()).toBe("pageSize=6&campaign=fall");
  });

  it("mirrors pending URL selections and preserves selected zero-count options", () => {
    const selected = facetsForQuery(
      facets,
      new URLSearchParams(
        "f.material=oak,walnut&f.foldable=false&f.width=40.5..80",
      ),
    );
    expect(selected[0].options?.map((option) => option.selected)).toEqual([
      true,
      true,
    ]);
    expect(selected[1].selected).toBe(false);
    expect([selected[2].selectedMin, selected[2].selectedMax]).toEqual([
      "40.5",
      "80",
    ]);
    expect(activeFilterCount(selected)).toBe(4);
    expect(optionValueWithout(selected[0], "oak")).toBe("walnut");

    const exact = facetsForQuery(
      [facets[2]],
      new URLSearchParams("f.width=42.5"),
    )[0];
    expect([exact.selectedMin, exact.selectedMax]).toEqual(["42.5", "42.5"]);
  });
});
