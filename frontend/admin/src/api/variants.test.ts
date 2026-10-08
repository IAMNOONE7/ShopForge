import { afterEach, expect, it, vi } from "vitest";
import { api, type VariantInput } from "../api";

afterEach(() => vi.unstubAllGlobals());

it("uses the product and variant contracts without sending a listing price or changing stock", async () => {
  const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(null, { status: 204 })));
  vi.stubGlobal("fetch", fetch);
  const input: VariantInput = { sku: "EXACT-S", ean: null, weightGrams: 0,
    partNumber: "P-1", condition: "Used", optionValues: ["S"] };
  await api.setProductOptions("product-a", { names: ["Size"], values: { "variant-a": ["S"] } });
  await api.addVariant("product-a", input);
  await api.updateVariant("product-a", "variant-a", input);
  await api.deleteVariant("product-a", "variant-a");
  await api.stockMovements("variant-a", 2);
  expect(fetch.mock.calls.map(([url, options]) => [url, options.method, options.body])).toEqual([
    ["/api/admin/products/product-a/options", "PUT", JSON.stringify({ names: ["Size"], values: { "variant-a": ["S"] } })],
    ["/api/admin/products/product-a/variants", "POST", JSON.stringify(input)],
    ["/api/admin/products/product-a/variants/variant-a", "PUT", JSON.stringify(input)],
    ["/api/admin/products/product-a/variants/variant-a", "DELETE", undefined],
    ["/api/admin/stock/variant-a/movements?page=2&pageSize=50", "GET", undefined],
  ]);
});
