import type { IsolatedRun } from "./isolatedEnvironment.ts";
import { allowedBrowserWrite } from "./writeBoundary.ts";

export type MerchantScope = {
  adminOrigin: string; rigOrigin: string; ownerEmail: string; runId: string;
  storefronts: Map<string, string>; stores: Set<string>; products: Set<string>; variants: Set<string>; skus: Set<string>;
  listings: Map<string, Set<string>>; methods: Map<string, Set<string>>; orders: Map<string, Set<string>>;
};
export function merchantScope(run: IsolatedRun, adminOrigin: string, rigOrigin: string, storefronts: Map<string, string>, ownerEmail: string): MerchantScope {
  return { adminOrigin, rigOrigin, storefronts, ownerEmail, runId: run.id, stores: new Set(run.stores.map(store => store.id)),
    products: new Set(run.products.map(product => product.id)), variants: new Set(run.products.flatMap(product => product.variants.map(variant => variant.id))),
    skus: new Set(run.products.flatMap(product => product.variants.map(variant => variant.sku))),
    listings: new Map(run.stores.map(store => [store.id, new Set(run.products.map(product => product.listings[store.id]))])),
    methods: new Map(), orders: new Map() };
}
const record = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const uuid = (value: unknown): value is string => typeof value === "string" && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
function member(map: Map<string, Set<string>>, key: string, value: string) { return map.get(key)?.has(value) === true; }
function remember(map: Map<string, Set<string>>, key: string, value: string) { if (!map.has(key)) map.set(key, new Set()); map.get(key)!.add(value); }

export function allowedMerchantWrite(scope: MerchantScope, method: string, url: URL, body: unknown) {
  if (url.username || url.password || url.search || url.hash || /%|\/\//.test(url.pathname)) return false;
  if (url.origin !== scope.adminOrigin) {
    if (!allowedBrowserWrite(method, url, new Set(scope.storefronts.keys()), scope.rigOrigin)) return false;
    const store = scope.storefronts.get(url.origin);
    if (url.pathname === "/api/storefront/cart/items") return record(body) && typeof body.storeProductId === "string" && member(scope.listings, store!, body.storeProductId) && typeof body.variantId === "string" && scope.variants.has(body.variantId);
    if (url.pathname === "/api/storefront/checkout") return record(body) && typeof body.email === "string" && body.email.endsWith(`@${scope.runId}.e2e.test`);
    return method === "POST" && url.origin === scope.rigOrigin && /^\/comgate\/pay\/[^/]+$/.test(url.pathname);
  }
  const path = url.pathname;
  if (method === "POST" && path === "/api/admin/auth/login") return record(body) && body.email === scope.ownerEmail;
  if (method === "POST" && path === "/api/admin/auth/logout") return true;
  if (!record(body) && !(method === "POST" && /\/(?:publish|unpublish|refund)$/.test(path))) return false;
  if (method === "POST" && path === "/api/admin/products") return record(body) && typeof body.sku === "string" && body.sku.startsWith(`${scope.runId.toUpperCase()}-`);
  const stock = path.match(/^\/api\/admin\/stock\/([^/]+)$/);
  if (method === "PUT" && stock) return scope.variants.has(stock[1]) && record(body) && Number.isInteger(body.quantity) && Number(body.quantity) >= 0;
  const target = path.match(/^\/api\/admin\/stores\/([^/]+)\/(.+)$/);
  if (!target || !scope.stores.has(target[1])) return false;
  const [, store, tail] = target;
  if (method === "POST" && ["publish", "unpublish"].includes(tail)) return true;
  if (tail === "products" && method === "POST") return record(body) && typeof body.productId === "string" && scope.products.has(body.productId);
  const listing = tail.match(/^products\/([^/]+)$/);
  if (listing && method === "PUT") return member(scope.listings, store, listing[1]);
  if (tail === "shipping-methods" && method === "POST") return record(body) && body.providerKey === "packeta";
  const shipping = tail.match(/^shipping-methods\/([a-z0-9-]+)$/);
  if (shipping && method === "PUT") return member(scope.methods, `${store}:shipping`, shipping[1]);
  const payment = tail.match(/^payment-methods\/([a-z0-9-]+)$/);
  if (payment && method === "PUT") return member(scope.methods, `${store}:payment`, payment[1]);
  if (method === "PUT" && /^provider-connections\/(?:comgate|packeta)$/.test(tail)) return record(body) && body.environment === "Test";
  if (method === "PUT" && tail === "provider-connections/packeta/secret") return record(body) && typeof body.secret === "string" && body.secret.startsWith(`${scope.runId}-local-only-`);
  const refund = tail.match(/^orders\/(\d{4}-\d+)\/refund$/);
  return method === "POST" && refund !== null && member(scope.orders, store, refund[1]);
}

// Learn identities only from successful, scoped responses; write permission never comes from a guessed URL.
export function recordMerchantResult(scope: MerchantScope, method: string, url: URL, request: unknown, value: unknown) {
  if (method !== "GET" && !allowedMerchantWrite(scope, method, url, request)) return;
  if (url.origin === scope.adminOrigin) {
    if (method === "POST" && url.pathname === "/api/admin/products" && record(value) && uuid(value.id) && value.sku === (request as Record<string, unknown>).sku && Array.isArray(value.variants) && value.variants.every(variant => record(variant) && uuid(variant.id) && typeof variant.sku === "string" && variant.sku.startsWith(`${scope.runId.toUpperCase()}-`))) {
      scope.products.add(value.id); for (const variant of value.variants) { scope.variants.add(variant.id); scope.skus.add(variant.sku); }
    }
    const target = url.pathname.match(/^\/api\/admin\/stores\/([^/]+)\/(.+)$/);
    if (!target || !scope.stores.has(target[1])) return;
    const [, store, tail] = target;
    if (tail === "products" && method === "POST" && record(value) && uuid(value.id) && value.productId === (request as Record<string, unknown>).productId) remember(scope.listings, store, value.id);
    const family = tail === "shipping-methods" ? "shipping" : tail === "payment-methods" ? "payment" : null;
    if (family && (method === "GET" || method === "POST")) for (const row of Array.isArray(value) ? value : [value]) {
      if (record(row) && typeof row.code === "string" && /^[a-z0-9-]+$/.test(row.code)) remember(scope.methods, `${store}:${family}`, row.code);
    }
  } else if (method === "POST" && url.pathname === "/api/storefront/checkout" && record(value) && typeof value.number === "string" && /^\d{4}-\d+$/.test(value.number)) {
    const store = scope.storefronts.get(url.origin); if (store) remember(scope.orders, store, value.number);
  }
}
