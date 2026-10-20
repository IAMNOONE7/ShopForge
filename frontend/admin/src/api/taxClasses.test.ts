import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api";
import { parseTaxPercentage, type TaxClass } from "./taxClasses";

const storeId = "aaaaaaaa-1111-2222-3333-444444444444";
const item: TaxClass = { code: "books", name: "Books", rates: [{ country: "CZ", rate: 10 }, { country: "DE", rate: 7 }] };
function answer(value: unknown, status = 200) {
  const fetch = vi.fn().mockResolvedValue(status === 204 ? new Response(null, { status }) : Response.json(value, { status }));
  vi.stubGlobal("fetch", fetch); return fetch;
}
afterEach(() => vi.unstubAllGlobals());

it("reads the existing collection with cancellation and canonical country ordering", async () => {
  const fetch = answer([{ ...item, rates: [...item.rates].reverse() }]), signal = new AbortController().signal;
  expect(await api.taxClasses(storeId, signal)).toEqual([item]);
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/${storeId}/tax-classes`); expect(fetch.mock.calls[0][1].signal).toBe(signal);
  expect(fetch).toHaveBeenCalledOnce();
});
it.each([
  null, {}, [item, item], [{ ...item, code: "Books" }], [{ ...item, code: "bad--code" }],
  [{ ...item, code: "x".repeat(51) }], [{ ...item, name: " " }], [{ ...item, name: "x".repeat(101) }],
  [{ ...item, rates: null }], [{ ...item, rates: [item.rates[0], item.rates[0]] }],
  [{ ...item, rates: [{ country: "cz", rate: 10 }] }], [{ ...item, rates: [{ country: "CZ", rate: "10" }] }],
  [{ ...item, rates: [{ country: "CZ", rate: -1 }] }], [{ ...item, rates: [{ country: "CZ", rate: 100.01 }] }],
  [{ ...item, rates: [{ country: "CZ", rate: 0.001 }] }],
].map(value => ({ value })))("rejects malformed or ambiguous tax records: %j", async ({ value }) => {
  answer(value); await expect(api.taxClasses(storeId)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("distinguishes an empty collection and empty country rates from a configured zero", async () => {
  const fetch = answer([]); expect(await api.taxClasses(storeId)).toEqual([]);
  fetch.mockResolvedValueOnce(Response.json([{ ...item, rates: [{ country: "IE", rate: 0 }] }, { code: "food", name: "Food", rates: [] }]));
  const rows = await api.taxClasses(storeId); expect(rows[0].rates).toEqual([{ country: "IE", rate: 0 }]); expect(rows[1].rates).toEqual([]);
});
it("discards a late tax-class read after its scope is aborted", async () => {
  const controller = new AbortController(); answer([item]); controller.abort();
  await expect(api.taxClasses(storeId, controller.signal)).rejects.toMatchObject({ name: "AbortError" });
});
it.each([
  { text: "0", value: 0 }, { text: "100.00", value: 100 }, { text: " 12,50 ", value: 12.5 },
  { text: "0.29", value: 0.29 }, { text: "99.99", value: 99.99 },
  ...["", " ", "100.01", "-1", "1.234", "1,2.3", "1e1", "Infinity", ".5", "10%"].map(text => ({ text, value: null })),
])("validates localized percentage input without rounding or inventing a rate ($text)", ({ text, value }) => {
  expect(parseTaxPercentage(text)).toBe(value);
});
it("creates a canonical class and checks its exact code, name and initially empty rates", async () => {
  const fetch = answer({ ...item, rates: [] }, 201);
  expect(await api.createTaxClass("store / one", "books", " Books ")).toEqual({ ...item, rates: [] });
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/stores/store%20%2F%20one/tax-classes");
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ code: "books", name: "Books" });
  fetch.mockResolvedValueOnce(Response.json({ ...item, code: "foreign", rates: [] }));
  await expect(api.createTaxClass(storeId, "books", "Books")).rejects.toMatchObject({ kind: "invalid-response" });
});
it("renames only the label and treats the empty rate array as an acknowledgment", async () => {
  const fetch = answer({ ...item, name: "Printed books", rates: [] });
  expect(await api.renameTaxClass(storeId, "books", " Printed books ")).toEqual({ ...item, name: "Printed books", rates: [] });
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/${storeId}/tax-classes/books`);
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: "Printed books" });
});
it.each([0, 0.29, 12.5, 100])("sets the exact normalized country rate, including zero (%s)", async rate => {
  const fetch = answer({ country: "DE", rate });
  expect(await api.setTaxRate(storeId, "books", " de ", rate)).toEqual({ country: "DE", rate });
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/${storeId}/tax-classes/books/rates/DE`);
  expect(fetch.mock.calls[0][1].method).toBe("PUT"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ rate });
});
it.each([{ country: "CZ", rate: 7 }, { country: "DE", rate: 8 }, { country: "DE", rate: 7.001 }])("rejects a mismatched rate acknowledgment: %j", async row => {
  answer(row); await expect(api.setTaxRate(storeId, "books", "DE", 7)).rejects.toMatchObject({ kind: "invalid-response" });
});
it("prevents invalid input writes, including rate rounding and non-ASCII countries", async () => {
  const fetch = answer({});
  await expect(api.createTaxClass(storeId, "Bad Code", "Books")).rejects.toBeInstanceOf(RangeError);
  await expect(api.renameTaxClass(storeId, "books", "")).rejects.toBeInstanceOf(RangeError);
  await expect(api.setTaxRate(storeId, "books", "ČZ", 1)).rejects.toBeInstanceOf(RangeError);
  await expect(api.setTaxRate(storeId, "books", "CZ", 1.001)).rejects.toBeInstanceOf(RangeError);
  await expect(api.removeTaxRate(storeId, "books", "CZE")).rejects.toBeInstanceOf(RangeError);
  await expect(api.deleteTaxClass(storeId, "bad/code")).rejects.toBeInstanceOf(RangeError); expect(fetch).not.toHaveBeenCalled();
});
it.each(["class", "rate"] as const)("checks bodyless %s deletion without guessing success", async kind => {
  const fetch = answer(undefined, 204);
  if (kind === "class") await api.deleteTaxClass(storeId, "books"); else await api.removeTaxRate(storeId, "books", "de");
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/stores/${storeId}/tax-classes/books${kind === "rate" ? "/rates/DE" : ""}`);
  expect(fetch.mock.calls[0][1].method).toBe("DELETE"); expect(fetch.mock.calls[0][1].body).toBeUndefined();
  fetch.mockResolvedValueOnce(Response.json({ deleted: true }));
  await expect(kind === "class" ? api.deleteTaxClass(storeId, "books") : api.removeTaxRate(storeId, "books", "DE")).rejects.toMatchObject({ kind: "invalid-response" });
});
it.each([403, 404, 409, 429, 503])("does not automatically replay a rejected or uncertain tax change (%i)", async status => {
  const fetch = answer({ title: "Refused" }, status);
  await expect(api.deleteTaxClass(storeId, "books")).rejects.toMatchObject({ status }); expect(fetch).toHaveBeenCalledOnce();
});
