import { strict as assert } from "node:assert";
import { test } from "node:test";
import { allowedHostedWrite, hostedStoreOrigin, parseHostedEnvironment } from "./hostedEnvironment.ts";
import type { IsolatedRun } from "./isolatedEnvironment.ts";

const stack = { api: "http://127.0.0.1:5180", rig: "http://127.0.0.1:5190" }, environment = parseHostedEnvironment(stack);
const run: IsolatedRun = {
  id: "r12345678", emailDomain: "r12345678.e2e.test", tenantId: "tenant",
  stores: [{ id: "a", name: "A", host: "r12345678-a.localhost" }, { id: "b", name: "B", host: "r12345678-b.localhost" }],
  products: [{ id: "p", name: "Product", slug: "product", price: 100, listings: { a: "listing-a", b: "listing-b" }, variants: [{ id: "variant", sku: "SKU", size: null, stock: 10 }] }],
};
const cart = { storeProductId: "listing-a", variantId: "variant", quantity: 1 };
const allowed = (origin: string, path: string, body: unknown = cart, method = "POST") => allowedHostedWrite(run, environment, method, new URL(path, origin), body);

test("hosted apps use the API's actual port and distinct configured loopback hostnames", () => {
  assert.deepEqual(environment, { apiOrigin: stack.api, rigOrigin: stack.rig, adminOrigin: "http://admin.localhost:5180", edgeOrigin: "http://edge.localhost:5180" });
  assert.equal(hostedStoreOrigin(environment, run.stores[0]), "http://r12345678-a.localhost:5180");
  assert.throws(() => parseHostedEnvironment(stack, "admin.localhost", "admin.localhost"), /distinct/);
  for (const host of ["localhost", "127.0.0.1", "admin.example.com", "admin.localhost:5180", "admin.localhost/path", "https://admin.localhost", "admin.localhost@remote.example", "-bad.localhost"]) {
    assert.throws(() => parseHostedEnvironment(stack, host), /hostnames/);
    assert.throws(() => parseHostedEnvironment(stack, "admin.localhost", host), /hostnames/);
  }
  for (const api of ["https://remote.example", "http://user:secret@127.0.0.1:5180", "http://127.0.0.1:5180/path"]) assert.throws(() => parseHostedEnvironment({ ...stack, api }));
});

test("hosted smoke allows one exact run-owned cart variant and the run owner's sign-in only", () => {
  const a = hostedStoreOrigin(environment, run.stores[0]), b = hostedStoreOrigin(environment, run.stores[1]);
  assert.equal(allowed(a, "/api/storefront/cart/items"), true);
  assert.equal(allowed(b, "/api/storefront/cart/items", { ...cart, storeProductId: "listing-b" }), true);
  const owner = { email: `owner@${run.emailDomain}`, password: "test-only" };
  assert.equal(allowed(environment.adminOrigin, "/api/admin/auth/login", owner), true);
  assert.equal(allowed("http://user:secret@admin.localhost:5180", "/api/admin/auth/login", owner), false);
  for (const origin of [a, b, environment.edgeOrigin, stack.api, "http://admin.localhost:5176", "http://remote.example"]) assert.equal(allowed(origin, "/api/admin/auth/login", owner), false);
  for (const body of [null, [], { ...owner, email: "other@e2e.test" }, { ...owner, password: "" }, { ...owner, extra: true }]) assert.equal(allowed(environment.adminOrigin, "/api/admin/auth/login", body), false);
  for (const body of [null, [], { ...cart, storeProductId: "foreign" }, { ...cart, variantId: "foreign" }, { ...cart, quantity: 2 }, { ...cart, extra: true }]) assert.equal(allowed(a, "/api/storefront/cart/items", body), false);
  assert.equal(allowed(b, "/api/storefront/cart/items"), false);
  for (const path of ["/api/storefront/cart/items?x=1", "/api/storefront/checkout", "/api/e2e/clock", "/api/admin/stores/a/publish"]) assert.equal(allowed(a, path), false);
  for (const method of ["PUT", "PATCH", "DELETE", "GET"]) assert.equal(allowed(a, "/api/storefront/cart/items", cart, method), false);
});
