import { afterEach, expect, it, vi } from "vitest";
import { api } from "../api";
import { checkedProductImages } from "./productMedia";

const product = "aaaaaaaa-1111-2222-3333-444444444444", a = "11111111-1111-1111-1111-111111111111", b = "22222222-2222-2222-2222-222222222222";
const image = (id = a, position = 0) => ({ id, position, url: `/api/admin/products/${product}/images/${id}`, altText: "Front" });
function answer(value: unknown, status = 200, headers = {}) { const fetch = vi.fn().mockResolvedValue(Response.json(value, { status, headers })); vi.stubGlobal("fetch", fetch); return fetch; }
afterEach(() => vi.unstubAllGlobals());

it("reads the existing product inventory, sorting the saved positions with cancellation", async () => {
  const fetch = answer([{ id: product, images: [image(b, 1), image()] }]); const signal = new AbortController().signal;
  expect(await api.productImages(product.toUpperCase(), signal)).toEqual([image(), image(b, 1)]);
  expect(fetch.mock.calls[0][0]).toBe("/api/admin/products"); expect(fetch.mock.calls[0][1].signal).toBe(signal);
});
it("does not invent an image-list endpoint or infer success for a missing physical product", async () => {
  const fetch = answer([]); await expect(api.productImages(product)).rejects.toMatchObject({ status: 404 }); expect(fetch).toHaveBeenCalledOnce();
});
it.each([
  null, {}, [image(), image()], [image(a, 1)], [image(a, -1)], [{ ...image(), position: 0.5 }],
  [{ ...image(), id: "foreign" }], [{ ...image(), altText: undefined }],
  [{ ...image(), url: "/api/admin/products/another/images/" + a }], [{ ...image(), url: "https://outside.test/photo" }],
].map(value => ({ value })))("rejects malformed or foreign inventories (%j)", ({ value }) => {
  expect(() => checkedProductImages(product, value)).toThrow();
});
it("writes the complete image order and checks the returned IDs/order", async () => {
  const fetch = answer([image(b), image(a, 1)]);
  expect(await api.reorderProductImages(product, [b, a])).toEqual([image(b), image(a, 1)]);
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/products/${product}/images`);
  expect(fetch.mock.calls[0][1].method).toBe("PUT"); expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ imageIds: [b, a] });
});
it.each([[image()], [image(), image(b, 1)], [image(b), image(a, 1), image("33333333-3333-3333-3333-333333333333", 2)]].map(value => ({ value })))("rejects stale or unexpected order answers (%j)", async ({ value }) => {
  answer(value); await expect(api.reorderProductImages(product, [b, a])).rejects.toMatchObject({ kind: "invalid-response" });
});
it("does not send empty or duplicate image orders", async () => {
  const fetch = answer([]);
  await expect(api.reorderProductImages(product, [])).rejects.toBeInstanceOf(RangeError);
  await expect(api.reorderProductImages(product, [a, a])).rejects.toBeInstanceOf(RangeError); expect(fetch).not.toHaveBeenCalled();
});
it.each(["  New description  ", ""])("edits and clears descriptions with the 200-character contract (%s)", async text => {
  const fetch = answer({ ...image(), altText: text.trim() || null });
  await api.describeProductImage(product, a, text);
  expect(fetch.mock.calls[0][0]).toBe(`/api/admin/products/${product}/images/${a}`);
  expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ altText: text.trim() || null });
});
it("rejects a foreign description answer and prevents overlength writes", async () => {
  const fetch = answer(image(b)); await expect(api.describeProductImage(product, a, "Front")).rejects.toMatchObject({ kind: "invalid-response" });
  await expect(api.describeProductImage(product, a, "x".repeat(201))).rejects.toBeInstanceOf(RangeError); expect(fetch).toHaveBeenCalledOnce();
});
it("uploads exactly one file as multipart and validates its product-owned result", async () => {
  const fetch = answer(image()); const file = new File(["png"], "front.png", { type: "image/png" });
  await api.uploadProductImage(product, file, " Front ");
  const options = fetch.mock.calls[0][1]; expect(options.method).toBe("POST");
  expect(options.body.getAll("file")).toHaveLength(1); expect(options.body.get("altText")).toBe("Front");
  expect(new Headers(options.headers).has("Content-Type")).toBe(false);
});
it("retains 429 and Retry-After without replaying the upload", async () => {
  const fetch = answer({ title: "Wait" }, 429, { "Retry-After": "25" });
  await expect(api.uploadProductImage(product, new File(["png"], "front.png", { type: "image/png" }), "")).rejects.toMatchObject({ status: 429, retryAfter: "25" });
  expect(fetch).toHaveBeenCalledOnce();
});
it("checks deletion acknowledgments rather than accepting a response body as success", async () => {
  answer(image()); await expect(api.deleteProductImage(product, a)).rejects.toMatchObject({ kind: "invalid-response" });
});
