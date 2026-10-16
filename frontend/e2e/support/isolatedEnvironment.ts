import { readFileSync } from "node:fs";
import { resolve } from "node:path";

export type IsolatedEnvironment = { apiOrigin: string; rigOrigin: string; storefrontPort: number; frontendOrigin: string };
export type RunStore = { id: string; name: string; host: string };
export type RunVariant = { id: string; sku: string; size: string | null; stock: number };
export type RunProduct = { id: string; name: string; slug: string; price: number; variants: RunVariant[]; listings: Record<string, string> };
export type IsolatedRun = { id: string; emailDomain: string; tenantId: string; stores: RunStore[]; products: RunProduct[] };
export type Marker = { environment: "E2E"; database: "shopforge_e2e"; startedAt: string };

function object(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function uuid(value: unknown): value is string { return typeof value === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value); }
function word(value: unknown): value is string { return typeof value === "string" && value.trim().length > 0; }
function loopbackOrigin(value: unknown): string {
  if (typeof value !== "string") throw new Error("Stack addresses must be loopback HTTP origins.");
  const url = new URL(value);
  if (url.protocol !== "http:" || !["127.0.0.1", "[::1]", "localhost"].includes(url.hostname) || url.username || url.password || url.pathname !== "/" || url.search || url.hash) {
    throw new Error("Stack addresses must be loopback HTTP origins without credentials or paths.");
  }
  return url.origin;
}

export function parseStack(value: unknown, port = "5175"): IsolatedEnvironment {
  if (!object(value)) throw new Error("The E2E stack descriptor is required.");
  const apiOrigin = loopbackOrigin(value.api), rigOrigin = loopbackOrigin(value.rig);
  if (apiOrigin === rigOrigin) throw new Error("API and rig need separate origins.");
  if (!/^\d+$/.test(port) || Number(port) < 1024 || Number(port) > 65535 || [new URL(apiOrigin).port, new URL(rigOrigin).port].includes(String(Number(port)))) {
    throw new Error("Choose a distinct unprivileged storefront port.");
  }
  return { apiOrigin, rigOrigin, storefrontPort: Number(port), frontendOrigin: `http://127.0.0.1:${Number(port)}` };
}

export function loadStack() {
  const path = process.env.SHOPFORGE_E2E_STACK ?? "../../.e2e/stack.json";
  return parseStack(JSON.parse(readFileSync(resolve(path), "utf8")) as unknown, process.env.SHOPFORGE_E2E_STOREFRONT_PORT);
}

export function parseMarker(value: unknown): Marker {
  if (!object(value) || value.environment !== "E2E" || value.database !== "shopforge_e2e" || !word(value.startedAt) || !Number.isFinite(Date.parse(value.startedAt))) {
    throw new Error("The target must be the E2E API over shopforge_e2e.");
  }
  return { environment: value.environment, database: value.database, startedAt: value.startedAt };
}

export function parseRun(value: unknown): IsolatedRun {
  if (!object(value) || typeof value.id !== "string" || !/^r[0-9a-f]{8}$/.test(value.id) || value.emailDomain !== `${value.id}.e2e.test` || !uuid(value.tenantId)) throw new Error("Invalid run identity.");
  const id = value.id;
  if (!Array.isArray(value.stores) || value.stores.length !== 2 || !value.stores.every((store, index) => object(store) && uuid(store.id) && word(store.name) && store.host === `${id}-${index === 0 ? "a" : "b"}.localhost`)
    || new Set(value.stores.map(store => store.id)).size !== 2) throw new Error("The run must describe two distinct run-owned stores.");
  const stores = value.stores as RunStore[];
  if (!Array.isArray(value.products) || value.products.length < 2 || !value.products.every(product => object(product) && uuid(product.id) && word(product.name) && word(product.slug)
    && typeof product.price === "number" && Number.isFinite(product.price) && product.price > 0 && object(product.listings) && Object.keys(product.listings).length === stores.length && stores.every(store => uuid((product.listings as Record<string, unknown>)[store.id]))
    && Array.isArray(product.variants) && product.variants.length > 0 && product.variants.every(variant => object(variant) && uuid(variant.id) && typeof variant.sku === "string" && variant.sku.startsWith(id.toUpperCase() + "-")
      && (variant.size === null || word(variant.size)) && Number.isInteger(variant.stock) && (variant.stock as number) > 0))) throw new Error("The run must describe stocked products, variants and store listings.");
  const products = value.products as RunProduct[];
  for (const ids of [products.map(product => product.id), products.map(product => product.slug), products.flatMap(product => Object.values(product.listings)), products.flatMap(product => product.variants.map(variant => variant.id)), products.flatMap(product => product.variants.map(variant => variant.sku))]) {
    if (new Set(ids).size !== ids.length) throw new Error("Run product identities must be unique.");
  }
  return { id, emailDomain: value.emailDomain as string, tenantId: value.tenantId, stores, products };
}

export function storeOrigin(environment: IsolatedEnvironment, store: RunStore) { return `http://${store.host}:${environment.storefrontPort}`; }
