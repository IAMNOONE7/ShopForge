import { strict as assert } from "node:assert";
import { test } from "node:test";
import { allowedMerchantWrite, merchantScope, recordMerchantResult } from "./merchantBoundary.ts";
import { merchantOrigin } from "./merchantEnvironment.ts";
import type { IsolatedRun } from "./isolatedEnvironment.ts";

const admin = "http://127.0.0.1:5176", rig = "http://127.0.0.1:5190", host = "http://r12345678-a.localhost:5175";
const id = (n: number) => `00000000-0000-0000-0000-${String(n).padStart(12, "0")}`;
const run: IsolatedRun = { id: "r12345678", emailDomain: "r12345678.e2e.test", tenantId: id(1), stores: [{ id: id(2), name: "A", host: new URL(host).hostname }], products: [] };
function scope() { return merchantScope(run, admin, rig, new Map([[host, id(2)]]), "owner@r12345678.e2e.test"); }
const url = (path: string) => new URL(admin + path);
const store = `/api/admin/stores/${id(2)}`;
test("admin preview must use a distinct local unprivileged port", () => {
  const environment = { apiOrigin: "http://127.0.0.1:5180", rigOrigin: rig, storefrontPort: 5175, frontendOrigin: "http://127.0.0.1:5175" };
  assert.equal(merchantOrigin(environment), admin);
  for (const port of ["80", "5175", "5180", "5190", "65536", "foo", "-1", "5176/other"]) assert.throws(() => merchantOrigin(environment, port));
});
test("only this run's owner and Test provider account setup are writable", () => {
  const own = scope();
  assert.equal(allowedMerchantWrite(own, "POST", url("/api/admin/auth/login"), { email: own.ownerEmail }), true);
  assert.equal(allowedMerchantWrite(own, "POST", url("/api/admin/auth/login"), { email: "other@e2e.test" }), false);
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store}/provider-connections/packeta`), { environment: "Test" }), true);
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store}/provider-connections/packeta`), { environment: "Live" }), false);
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store}/provider-connections/packeta/secret`), { secret: `${run.id}-local-only-test` }), true);
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store}/provider-connections/comgate/secret`), { secret: "replacement" }), false);
});
test("new product and variant identities must be learned from a matching real creation before stock or listing changes", () => {
  const own = scope(), request = { sku: "R12345678-NEW" };
  const write = () => allowedMerchantWrite(own, "PUT", url(`/api/admin/stock/${id(4)}`), { quantity: 3 });
  assert.equal(write(), false);
  recordMerchantResult(own, "POST", url("/api/admin/products"), request, { id: id(3), sku: "wrong", variants: [{ id: id(4), sku: request.sku }] }); assert.equal(write(), false);
  recordMerchantResult(own, "POST", url("/api/admin/products"), request, { id: id(3), sku: request.sku, variants: [{ id: id(4), sku: request.sku }] }); assert.equal(write(), true); assert.equal(own.skus.has(request.sku), true);
  assert.equal(allowedMerchantWrite(own, "POST", url(`${store}/products`), { productId: id(3) }), true);
  assert.equal(allowedMerchantWrite(own, "POST", url(`${store}/products`), { productId: id(99) }), false);
  assert.equal(allowedMerchantWrite(own, "PUT", url(`/api/admin/stock/${id(4)}`), { quantity: -1 }), false);
  assert.equal(allowedMerchantWrite(own, "POST", url("/api/admin/products"), { sku: "OTHER-NEW" }), false);
});
test("listing and method edits belong to the exact store and registered record", () => {
  const own = scope(); own.products.add(id(3)); const request = { productId: id(3) };
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store}/products/${id(5)}`), {}), false);
  recordMerchantResult(own, "POST", url(`${store}/products`), request, { id: id(5), productId: id(3) });
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store}/products/${id(5)}`), {}), true);
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store.replace(id(2), id(8))}/products/${id(5)}`), {}), false);
  recordMerchantResult(own, "GET", url(`${store}/shipping-methods`), null, [{ code: "personal-pickup" }]);
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store}/shipping-methods/personal-pickup`), {}), true);
  assert.equal(allowedMerchantWrite(own, "PUT", url(`${store}/shipping-methods/foreign`), {}), false);
  assert.equal(allowedMerchantWrite(own, "POST", url(`${store}/shipping-methods`), { providerKey: "manual" }), false);
});
test("a financial write is limited to an order actually placed on this run's store", () => {
  const own = scope(), request = { email: `buyer@${run.emailDomain}` }, checkout = new URL(host + "/api/storefront/checkout");
  assert.equal(allowedMerchantWrite(own, "POST", url(`${store}/orders/2026-00001/refund`), null), false);
  recordMerchantResult(own, "POST", checkout, request, { number: "2026-00001" });
  assert.equal(allowedMerchantWrite(own, "POST", url(`${store}/orders/2026-00001/refund`), null), true);
  assert.equal(allowedMerchantWrite(own, "POST", url(`${store}/orders/2026-00002/refund`), null), false);
  assert.equal(allowedMerchantWrite(own, "POST", checkout, { email: "buyer@other.e2e.test" }), false);
});
test("operator, other-run, direct API, deletion and encoded/query paths remain forbidden", () => {
  const own = scope();
  for (const path of ["/api/platform/tenants", "/api/admin/stores", `${store}/publish?force=true`, `${store}/products/%2e%2e`, `${store}/provider-connections/stripe`, `${store}/provider-connections/packeta/secret?secret=x`]) {
    assert.equal(allowedMerchantWrite(own, "POST", url(path), {}), false, path);
  }
  for (const origin of ["http://127.0.0.1:5180", "http://other-a.localhost:5175", "https://provider.example"]) assert.equal(allowedMerchantWrite(own, "POST", new URL(origin + "/api/admin/auth/login"), { email: own.ownerEmail }), false);
  assert.equal(allowedMerchantWrite(own, "DELETE", url(`${store}/provider-connections/packeta`), null), false);
  assert.equal(allowedMerchantWrite(own, "POST", url(`${store}/publish`), null), true);
});
