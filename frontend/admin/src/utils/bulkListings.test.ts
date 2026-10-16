import { expect, it } from "vitest";
import { bulkChange, bulkPercent, bulkPrice, bulkPriceDecimals } from "./bulkListings";

it.each([['CZK',2],['EUR',2],['HUF',2],['JPY',0],['ISK',0],['KWD',3],['IQD',3]] as const)("uses contract currency units for %s", (code, digits) => expect(bulkPriceDecimals(code)).toBe(digits));
it.each([
  ['0','CZK',0],['12,50','CZK',12.5],[' 12.50 ','CZK',12.5],['1.000','CZK',1],['1.005','KWD',1.005],['2','JPY',2],
  ['','CZK',null],['-1','CZK',null],['1e3','CZK',null],['1,234.56','CZK',null],['1.001','CZK',null],['1.5','JPY',null],['1.0001','KWD',null],['999999999999999.99','CZK',null],['NaN','CZK',null],['90071992547409.91','CZK',null],['90071992547409.9','CZK',90071992547409.9],
] as const)("parses %s in %s without silent currency rounding", (text, code, expected) => expect(bulkPrice(text, code)).toBe(expected));
it.each([['0',0],['5',5],['-10,5',-10.5],['-99.999999',-99.999999],['1000',1000],['-100',null],['1000.000001',null],['',null],['1e2',null],['1.0000001',null],['Infinity',null]] as const)("checks percentage intent %s", (text, expected) => expect(bulkPercent(text)).toBe(expected));
it("keeps set/percentage and category add/remove semantics separate", () => {
  expect(bulkChange("setPrice", 0, [], [])).toEqual({ action: "price", set: 0 });
  expect(bulkChange("changePrice", -5, [], [])).toEqual({ action: "price", byPercent: -5 });
  expect(bulkChange("categories", 0, ['sale'], ['old'])).toEqual({ action: "categories", addCategoryIds: ['sale'], removeCategoryIds: ['old'] });
  expect(bulkChange("restore", 0, [], [])).toEqual({ action: "archive", archived: false });
});
