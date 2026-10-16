import { strict as assert } from "node:assert";
import { test } from "node:test";
import { allowedBrowserWrite } from "./writeBoundary.ts";

const stores = new Set(["http://r12345678-a.localhost:5175", "http://r12345678-b.localhost:5175"]);
const rig = "http://127.0.0.1:5190";
test("allows only the guest and registered shopper writes exercised on this run's two hosts", () => {
  for (const origin of stores) {
    for (const path of ["/cart/items", "/checkout", "/account/register", "/account/verify", "/account/login", "/account/logout", "/account/orders/2026-00001/returns"]) {
      assert.equal(allowedBrowserWrite("POST", new URL(`${origin}/api/storefront${path}`), stores, rig), true);
    }
    assert.equal(allowedBrowserWrite("DELETE", new URL(`${origin}/api/storefront/cart/items/listing?variantId=variant`), stores, rig), true);
  }
  assert.equal(allowedBrowserWrite("POST", new URL(`${rig}/comgate/pay/transaction`), stores, rig), true);
});
test("refuses other runs, ports, providers and merchant or operator browser writes", () => {
  for (const address of ["http://other-a.localhost:5175/api/storefront/checkout", "http://r12345678-a.localhost:5173/api/storefront/account/register", "http://127.0.0.1:5180/api/admin/auth/login", "http://r12345678-a.localhost:5175/api/platform/tenants", "https://provider.example/comgate/pay/transaction", `${rig}/runs/r12345678/clock`]) {
    assert.equal(allowedBrowserWrite("POST", new URL(address), stores, rig), false);
  }
});
test("refuses unexercised mutations, changed verbs and return progression from the browser", () => {
  const origin = [...stores][0];
  for (const [method, path] of [["PUT", "/api/storefront/checkout"], ["DELETE", "/api/storefront/account/orders/2026-00001/returns"], ["POST", "/api/storefront/account/orders/2026-00001/returns/receive"], ["POST", "/api/storefront/account/password/reset"], ["POST", "/api/admin/stores/store/returns/return/receive"]]) {
    assert.equal(allowedBrowserWrite(method, new URL(origin + path), stores, rig), false);
  }
  assert.equal(allowedBrowserWrite("DELETE", new URL(`${rig}/comgate/pay/transaction`), stores, rig), false);
});
