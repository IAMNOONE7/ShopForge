import { readFileSync } from "node:fs";
import { resolve } from "node:path";

export type StoreTarget = { origin: string; id: string };
export type SmokeEnvironment = {
  mode: "read-only";
  apiOrigin: string;
  stores: [StoreTarget, StoreTarget];
};

function object(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function origin(value: unknown, label: string) {
  if (typeof value !== "string") throw new Error(`${label} must be an HTTP origin.`);
  const url = new URL(value);
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password ||
      url.pathname !== "/" || url.search || url.hash) {
    throw new Error(`${label} must be an HTTP origin without credentials, path, query or fragment.`);
  }
  return url.origin;
}

export function parseEnvironment(value: unknown): SmokeEnvironment {
  if (!object(value) || value.mode !== "read-only") {
    throw new Error("Smoke targets support read-only checks. Use the separate isolated journey configuration for the E2E stack.");
  }
  if (!Array.isArray(value.stores) || value.stores.length !== 2) {
    throw new Error("Configure exactly two real storefront targets.");
  }
  const stores = value.stores.map((store, index): StoreTarget => {
    if (!object(store) || typeof store.id !== "string" ||
        !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(store.id)) {
      throw new Error(`stores[${index}].id must be the actual store UUID.`);
    }
    return { origin: origin(store.origin, `stores[${index}].origin`), id: store.id.toLowerCase() };
  });
  if (new URL(stores[0].origin).hostname === new URL(stores[1].origin).hostname || stores[0].id === stores[1].id) {
    throw new Error("The two storefronts must have different hostnames and store IDs; ports do not isolate cookies.");
  }
  return {
    mode: "read-only", apiOrigin: origin(value.apiOrigin, "apiOrigin"),
    stores: [stores[0], stores[1]],
  };
}

export function loadEnvironment() {
  const path = process.env.SHOPFORGE_E2E_TARGETS;
  if (!path) throw new Error("Set SHOPFORGE_E2E_TARGETS to a read-only targets JSON file. See frontend/e2e/README.md.");
  return parseEnvironment(JSON.parse(readFileSync(resolve(path), "utf8")) as unknown);
}
