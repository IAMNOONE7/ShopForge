import { strict as assert } from "node:assert";
import { test } from "node:test";
import { parseEnvironment } from "./environment.ts";

const targets = {
  mode: "read-only", apiOrigin: "http://127.0.0.1:5080",
  stores: [
    { origin: "http://shop-a.localhost:5173", id: "01a0bf45-20b3-7a3b-87e4-c058fd463284" },
    { origin: "http://shop-b.localhost:5173", id: "01a0bf45-20b3-7a3b-87e4-c058fd463285" },
  ],
};

test("accepts two explicitly identified hosts and normalizes origins and IDs", () => {
  assert.deepEqual(parseEnvironment({ ...targets, stores: [
    { ...targets.stores[0], origin: targets.stores[0].origin + "/", id: targets.stores[0].id.toUpperCase() },
    targets.stores[1],
  ] }), targets);
});

test("refuses missing configuration and writable modes in read-only smoke targets", () => {
  for (const value of [null, [], {}, { ...targets, mode: "isolated" }, { ...targets, mode: "write" }]) {
    assert.throws(() => parseEnvironment(value), /read-only/);
  }
});

test("ports and aliases for one store cannot pass the two-host isolation prerequisite", () => {
  assert.throws(() => parseEnvironment({ ...targets, stores: [targets.stores[0], {
    ...targets.stores[1], origin: "http://shop-a.localhost:5174",
  }] }), /different hostnames/);
  assert.throws(() => parseEnvironment({ ...targets, stores: [targets.stores[0], {
    ...targets.stores[1], id: targets.stores[0].id.toUpperCase(),
  }] }), /different hostnames/);
});

test("requires exactly two targets with actual store UUIDs", () => {
  for (const stores of [[], [targets.stores[0]], [...targets.stores, targets.stores[0]]]) {
    assert.throws(() => parseEnvironment({ ...targets, stores }), /exactly two/);
  }
  for (const store of [null, { ...targets.stores[0], id: "store-a" }, { ...targets.stores[0], id: 1 }]) {
    assert.throws(() => parseEnvironment({ ...targets, stores: [store, targets.stores[1]] }), /UUID/);
  }
});

test("rejects credentials and non-origin URLs rather than navigating to another path or protocol", () => {
  for (const origin of ["file:///tmp/index.html", "http://user:password@shop-a.localhost:5173", "http://shop-a.localhost:5173/admin", "http://shop-a.localhost:5173/?q=x", "http://shop-a.localhost:5173/#cart", 5173]) {
    assert.throws(() => parseEnvironment({ ...targets, stores: [{ ...targets.stores[0], origin }, targets.stores[1]] }), /HTTP origin/);
    assert.throws(() => parseEnvironment({ ...targets, apiOrigin: origin }), /HTTP origin/);
  }
});
