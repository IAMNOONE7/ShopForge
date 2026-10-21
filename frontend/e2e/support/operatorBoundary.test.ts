import { strict as assert } from "node:assert";
import { test } from "node:test";
import { allowedOperatorWrite, type OperatorScope } from "./operatorBoundary.ts";
import { parseOperatorCredentials } from "./operatorEnvironment.ts";
import { parseStack } from "./isolatedEnvironment.ts";

const stack = { api: "http://127.0.0.1:5180", rig: "http://127.0.0.1:5190", operator: { email: "operator@e2e.test", password: `E2e-${"a".repeat(36)}` } };
const scope: OperatorScope = { adminOrigin: "http://127.0.0.1:5176", tenantId: "00000000-0000-0000-0000-000000000001", ownerEmail: "owner@r12345678.e2e.test", customerOrigin: "http://r12345678-a.localhost:5175", customerEmail: "buyer@r12345678.e2e.test" };
const url = (path: string, origin = scope.adminOrigin) => new URL(origin + path);
test("operator credentials must come from the selected local disposable stack descriptor", () => {
  assert.deepEqual(parseOperatorCredentials(stack, parseStack(stack)), stack.operator);
  for (const value of [null, {}, { ...stack, operator: null }, { ...stack, operator: { ...stack.operator, email: "real@example.com" } }, { ...stack, operator: { ...stack.operator, password: "not-the-stack-password" } }, { ...stack, api: "https://production.example" }, { ...stack, rig: "http://127.0.0.1:5191" }]) {
    assert.throws(() => parseOperatorCredentials(value, parseStack(stack)));
  }
});
test("only the exact run tenant can be suspended or resumed, with no body", () => {
  for (const action of ["suspend", "resume"]) {
    const path = `/api/platform/tenants/${scope.tenantId}/${action}`;
    assert.equal(allowedOperatorWrite(scope, "POST", url(path), null), true);
    for (const body of [{}, { force: true }, "", []]) assert.equal(allowedOperatorWrite(scope, "POST", url(path), body), false);
    for (const method of ["GET", "PUT", "PATCH", "DELETE"]) assert.equal(allowedOperatorWrite(scope, method, url(path), null), false);
    assert.equal(allowedOperatorWrite(scope, "POST", url(path.replace(scope.tenantId, "00000000-0000-0000-0000-000000000002")), null), false);
  }
});
test("staff and operator auth cannot borrow each other's identity or carry extra fields", () => {
  for (const [path, email] of [["/api/platform/auth/login", "operator@e2e.test"], ["/api/admin/auth/login", scope.ownerEmail]]) {
    assert.equal(allowedOperatorWrite(scope, "POST", url(path), { email, password: "fixture" }), true);
    for (const body of [{ email }, { email, password: "" }, { email: "other@e2e.test", password: "fixture" }, { email, password: "fixture", tenantId: scope.tenantId }]) assert.equal(allowedOperatorWrite(scope, "POST", url(path), body), false);
  }
  for (const path of ["/api/platform/auth/logout", "/api/admin/auth/logout"]) {
    assert.equal(allowedOperatorWrite(scope, "POST", url(path), null), true); assert.equal(allowedOperatorWrite(scope, "POST", url(path), {}), false);
  }
});
test("customer setup is limited to this run's first host and buyer, without checkout or account changes", () => {
  const body = { firstName: "Eva", lastName: "Buyer", email: scope.customerEmail, password: "fixture", phone: "123" };
  assert.equal(allowedOperatorWrite(scope, "POST", url("/api/storefront/account/register", scope.customerOrigin), body), true);
  assert.equal(allowedOperatorWrite(scope, "POST", url("/api/storefront/account/verify", scope.customerOrigin), { token: "fixture-token" }), true);
  for (const target of [scope.adminOrigin, scope.customerOrigin.replace("-a.", "-b."), "http://other.localhost:5175"]) assert.equal(allowedOperatorWrite(scope, "POST", url("/api/storefront/account/register", target), body), false);
  assert.equal(allowedOperatorWrite(scope, "POST", url("/api/storefront/account/register", scope.customerOrigin), { ...body, email: "other@e2e.test" }), false);
  for (const path of ["/api/storefront/checkout", "/api/storefront/cart/items", "/api/storefront/account/delete", "/api/storefront/account/email"]) assert.equal(allowedOperatorWrite(scope, "POST", url(path, scope.customerOrigin), body), false);
});
test("other operators, plans, tenants, provisioning, financial and direct API writes stay forbidden", () => {
  for (const path of ["/api/platform/tenants", "/api/platform/operators", "/api/platform/plans", "/api/platform/auth/two-factor", "/api/platform/account/two-factor/start", "/api/admin/stores", `/api/admin/stores/${scope.tenantId}/operations`, `/api/admin/stores/${scope.tenantId}/orders/2026-00001/refund`]) assert.equal(allowedOperatorWrite(scope, "POST", url(path), null), false, path);
  const path = `/api/platform/tenants/${scope.tenantId}/suspend`;
  for (const suffix of ["?force=true", "#fragment", "/", "/other"]) assert.equal(allowedOperatorWrite(scope, "POST", url(path + suffix), null), false);
  for (const target of [stack.api, stack.rig, "https://provider.example"]) assert.equal(allowedOperatorWrite(scope, "POST", url(path, target), null), false);
  assert.equal(allowedOperatorWrite(scope, "POST", url(path.replace("/tenants/", "/tenants//")), null), false);
  assert.equal(allowedOperatorWrite(scope, "POST", url(path.replace("suspend", "%73uspend")), null), false);
});
