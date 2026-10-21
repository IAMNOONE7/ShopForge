import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { parseStack, type IsolatedRun, type RunStore } from "./isolatedEnvironment.ts";

export function parseHostedEnvironment(value: unknown, adminHost = "admin.localhost", edgeHost = "edge.localhost") {
  const { apiOrigin, rigOrigin } = parseStack(value);
  const host = (name: string) => {
    if (!/^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+localhost$/.test(name)) throw new Error("Hosted admin addresses must be explicit .localhost hostnames.");
    const url = new URL(apiOrigin); url.hostname = name; return url.origin;
  };
  const adminOrigin = host(adminHost), edgeOrigin = host(edgeHost);
  if (adminOrigin === edgeOrigin) throw new Error("Use distinct configured admin and edge hosts.");
  return { apiOrigin, rigOrigin, adminOrigin, edgeOrigin };
}
export type HostedEnvironment = ReturnType<typeof parseHostedEnvironment>;

export function loadHostedEnvironment() {
  return parseHostedEnvironment(JSON.parse(readFileSync(resolve(process.env.SHOPFORGE_E2E_STACK ?? "../../.e2e/stack.json"), "utf8")) as unknown,
    process.env.SHOPFORGE_E2E_HOSTED_ADMIN_HOST, process.env.SHOPFORGE_E2E_HOSTED_EDGE_HOST);
}
export function hostedStoreOrigin(environment: HostedEnvironment, store: RunStore) {
  const url = new URL(environment.apiOrigin); url.hostname = store.host; return url.origin;
}
export function allowedHostedWrite(run: IsolatedRun, environment: HostedEnvironment, method: string, url: URL, body: unknown) {
  if (method !== "POST" || url.username || url.password || url.search || url.hash || !body || typeof body !== "object" || Array.isArray(body)) return false;
  const data = body as Record<string, unknown>;
  if (url.origin === environment.adminOrigin && url.pathname === "/api/admin/auth/login") {
    return Object.keys(data).sort().join() === "email,password" && data.email === `owner@${run.emailDomain}` && typeof data.password === "string" && data.password.length > 0;
  }
  const store = run.stores.find(store => hostedStoreOrigin(environment, store) === url.origin);
  return !!store && url.pathname === "/api/storefront/cart/items" && Object.keys(data).sort().join() === "quantity,storeProductId,variantId" && data.quantity === 1
    && run.products.some(product => product.listings[store.id] === data.storeProductId && product.variants.some(variant => variant.id === data.variantId));
}

export const storefrontPolicy = "default-src 'self'; script-src 'self' https://widget.packeta.com; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self' https://widget.packeta.com; frame-src https://widget.packeta.com; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
export const adminPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
