import { describe, expect, it } from "vitest";
import { normalizeSearch, searchTerm } from "./search";

describe("existing product search terms", () => {
  it("composes accents without folding, stemming or interpreting the shopper's words", () => {
    expect(normalizeSearch('  S\u030croubova\u0301k PH2 -"sada" & +  ')).toBe('Šroubovák PH2 -"sada" & +');
    expect(searchTerm("sroubovak")).toBe("sroubovak");
    expect(searchTerm("šroubováky")).toBe("šroubováky");
  });

  it.each([
    [null, ""], ["", ""], [" x ", ""], ["e\u0301", ""], [" \t ab \n", "ab"],
    ["e\u0301".repeat(200), "é".repeat(200)],
    ["x".repeat(199) + "e\u0301", "x".repeat(199) + "é"],
    ["x".repeat(201), "x".repeat(200)],
    ["x".repeat(199) + "😀", "x".repeat(199)],
    ["x".repeat(198) + "😀z", "x".repeat(198) + "😀"],
    ["😀".repeat(100), "😀".repeat(100)], ["😀".repeat(101), "😀".repeat(100)],
  ])("matches normalized server bounds for %j", (raw, expected) => {
    const term = searchTerm(raw);
    expect(term).toBe(expected);
    expect(term.length).toBeLessThanOrEqual(200);
    expect(term).not.toMatch(/[\ud800-\udfff]/u);
  });
});
