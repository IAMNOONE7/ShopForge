import { strict as assert } from "node:assert";
import { test } from "node:test";
import { parseMarker, parseRun, parseRunOwner, parseStack, storeOrigin, verificationUrl } from "./isolatedEnvironment.ts";

const id = (n: number) => `00000000-0000-0000-0000-${String(n).padStart(12, "0")}`;
const stack = { api: "http://127.0.0.1:5180", rig: "http://127.0.0.1:5190", operator: { email: "operator@example.test", password: "not-exported" } };
function describedRun() {
  return {
    id: "r12345678", tenantId: id(1), emailDomain: "r12345678.e2e.test", owner: { email: "owner@example.test", password: "not-exported" },
    stores: [{ id: id(2), name: "Run A", host: "r12345678-a.localhost" }, { id: id(3), name: "Run B", host: "r12345678-b.localhost" }],
    products: [0, 1].map(n => ({ id: id(4 + n), name: `Product ${n}`, slug: `product-${n}`, price: 100, variants: [{ id: id(6 + n), sku: `R12345678-SKU-${n}`, size: n ? null : "M", stock: 5 }], listings: { [id(2)]: id(8 + n * 2), [id(3)]: id(9 + n * 2) } })),
  };
}
test("reads only loopback stack addresses and does not export operator credentials", () => {
  assert.deepEqual(parseStack(stack), { apiOrigin: stack.api, rigOrigin: stack.rig, storefrontPort: 5175, frontendOrigin: "http://127.0.0.1:5175" });
  assert.equal(storeOrigin(parseStack(stack, "5176"), describedRun().stores[0]), "http://r12345678-a.localhost:5176");
});
test("refuses missing, real, credential-bearing or path-based stack targets", () => {
  assert.throws(() => parseStack(null));
  for (const value of ["https://shop.example", "http://192.168.1.1:5180", "http://user:secret@127.0.0.1:5180", "http://127.0.0.1:5180/admin", "http://127.0.0.1:5180/?q=x", "file:///tmp/stack", 5180]) {
    assert.throws(() => parseStack({ ...stack, api: value })); assert.throws(() => parseStack({ ...stack, rig: value }));
  }
  assert.throws(() => parseStack({ ...stack, rig: stack.api }));
});
test("refuses privileged, colliding or shell-shaped frontend ports", () => {
  for (const port of ["0", "80", "65536", "5180", "5190", "5175; true", "5175.1", ""]) assert.throws(() => parseStack(stack, port));
});
test("requires the actual E2E API marker and intended disposable database", () => {
  const marker = { environment: "E2E", database: "shopforge_e2e", startedAt: "2026-10-16T08:00:00Z" };
  assert.deepEqual(parseMarker(marker), marker);
  for (const invalid of [null, {}, { ...marker, environment: "Development" }, { ...marker, database: "shopforge" }, { ...marker, startedAt: "invalid" }]) assert.throws(() => parseMarker(invalid));
});
test("keeps validated run identities and omits the owner's sign-in from artifacts", () => {
  const raw = describedRun(); const { owner: _owner, ...expected } = raw;
  assert.deepEqual(parseRun(raw), expected); assert.equal("owner" in parseRun(raw), false);
});
test("refuses foreign host, store, tenant or mail identities", () => {
  for (const mutate of [
    (run: ReturnType<typeof describedRun>) => { run.id = "../other"; },
    (run: ReturnType<typeof describedRun>) => { run.emailDomain = "real.example"; },
    (run: ReturnType<typeof describedRun>) => { run.tenantId = "wrong"; },
    (run: ReturnType<typeof describedRun>) => { run.stores[0].host = "other-a.localhost"; },
    (run: ReturnType<typeof describedRun>) => { run.stores[0].host += ":5080"; },
    (run: ReturnType<typeof describedRun>) => { run.stores[1].id = run.stores[0].id; },
    (run: ReturnType<typeof describedRun>) => { run.stores.pop(); },
  ]) { const run = describedRun(); mutate(run); assert.throws(() => parseRun(run)); }
});
test("requires unique stocked variants and a real listing for both owned stores", () => {
  for (const mutate of [
    (run: ReturnType<typeof describedRun>) => { run.products[0].variants[0].sku = "OTHER-RUN-SKU"; },
    (run: ReturnType<typeof describedRun>) => { run.products[0].variants[0].stock = -1; },
    (run: ReturnType<typeof describedRun>) => { run.products[0].variants = []; },
    (run: ReturnType<typeof describedRun>) => { run.products[0].listings[id(2)] = "not-a-guid"; },
    (run: ReturnType<typeof describedRun>) => { run.products[0].listings[id(20)] = id(21); },
    (run: ReturnType<typeof describedRun>) => { run.products[1].variants[0].id = run.products[0].variants[0].id; },
    (run: ReturnType<typeof describedRun>) => { run.products[1].listings[id(3)] = run.products[0].listings[id(3)]; },
  ]) { const run = describedRun(); mutate(run); assert.throws(() => parseRun(run)); }
});
test("reads owner credentials separately and refuses credentials outside this run", () => {
  const raw = describedRun(), run = parseRun(raw);
  raw.owner.email = `owner@${run.emailDomain}`;
  assert.deepEqual(parseRunOwner(raw, run), raw.owner);
  assert.equal("owner" in parseRun(raw), false);
  for (const owner of [null, {}, { ...raw.owner, email: "owner@other.e2e.test" }, { ...raw.owner, password: "short" }, { ...raw.owner, password: "x".repeat(129) }]) {
    assert.throws(() => parseRunOwner({ ...raw, owner }, run));
  }
});
test("keeps the mailed verification token and path while mapping this store to its preview port", () => {
  const store = describedRun().stores[0], origin = `http://${store.host}:5175`;
  assert.equal(verificationUrl([`https://${store.host}/account/verify?token=from-the-mail`], store, origin), `${origin}/account/verify?token=from-the-mail`);
  for (const link of ["https://other-a.localhost/account/verify?token=secret", `https://user:secret@${store.host}/account/verify?token=secret`, `https://${store.host}/account/verify`, `https://${store.host}/account/verify?token=secret#fragment`, `ftp://${store.host}/account/verify?token=secret`, `https://${store.host}/account/reset-password?token=secret`]) {
    assert.throws(() => verificationUrl([link], store, origin));
  }
  assert.throws(() => verificationUrl([], store, origin));
  assert.throws(() => verificationUrl([`${origin}/account/verify?token=a`, `${origin}/account/verify?token=b`], store, origin));
  assert.throws(() => verificationUrl([`${origin}/account/verify?token=a`], store, "http://other-a.localhost:5175"));
});
