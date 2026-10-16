import { test as base, expect, type APIRequestContext } from "@playwright/test";
import { writeFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { loadStack, parseMarker, parseRun, parseRunOwner, storeOrigin, type IsolatedEnvironment, type IsolatedRun, type Marker, type RunStore } from "./isolatedEnvironment.ts";
import { allowedBrowserWrite } from "./writeBoundary.ts";

export type OrderReading = {
  order: { number: string; status: string; email: string; currency: string; paidAt: string | null; paymentReference: string | null; refundedTotal: number };
  lines: { productName: string; variantId: string | null; quantity: number; unitPrice: number; discount: number }[];
  attempts: { provider: string; reference: string | null; status: string; amount: number; currency: string; environment: string | null }[];
  answersRecorded: number; kept: unknown[];
  returns: { number: string; status: string; refundStatus: string | null; refundedAmount: number | null; refundProblem: string | null }[];
  documents: { number: string; kind: string }[]; letters: { type: string; status: string }[];
};
export type StockReading = { sku: string; onHand: number; reserved: number; available: number };
export type Letter = { id: string; to: string; subject: string; attachments: string[]; links: string[] };
export type GatewayReading = { merchant: string; transactions: { id: string; referenceId: string; status: string; price: number; currency: string; test: boolean; refunded: number }[] };
type MerchantReturn = { id: string; number: string; orderNumber: string; status: string; refundedAmount: number; refund: string | null; refundProblem: string | null };
export type RunSession = {
  environment: IsolatedEnvironment; run: IsolatedRun; marker: Marker;
  origin: (store: RunStore) => string;
  order: (store: RunStore, number: string) => Promise<OrderReading>;
  stock: (sku: string) => Promise<StockReading>;
  mail: (to: string) => Promise<Letter[]>;
  gateway: () => Promise<GatewayReading>;
  pushAgain: (id: string) => Promise<void>;
  advanceReturn: (store: RunStore, orderNumber: string, returnNumber: string, action: "accept" | "receive") => Promise<MerchantReturn>;
};

async function json<T>(client: APIRequestContext, url: string, status = 200, method: "GET" | "POST" = "GET", timeout = 5_000): Promise<T> {
  const response = await client.fetch(url, { method, maxRedirects: 0, maxRetries: 0, timeout });
  expect(response.status(), `${method} ${new URL(url).pathname}`).toBe(status);
  expect(response.headers()["content-type"]).toContain("application/json");
  return await response.json() as T;
}

export const test = base.extend<{ runSession: RunSession; isolatedBrowser: void }>({
  runSession: async ({ request, playwright }, use, testInfo) => {
    const environment = loadStack();
    for (const path of ["/health/live", "/health/ready"]) expect((await request.get(environment.apiOrigin + path, { maxRedirects: 0, timeout: 5_000 })).status()).toBe(200);
    const marker = parseMarker(await json(request, `${environment.apiOrigin}/api/e2e/`));
    await json(request, `${environment.rigOrigin}/health`);
    const created = await json<{ id?: unknown }>(request, `${environment.rigOrigin}/runs`, 201, "POST", 60_000);
    if (!created || typeof created.id !== "string" || !/^r[0-9a-f]{8}$/.test(created.id)) throw new Error("The rig returned no usable run ID for cleanup.");
    const root = `${environment.rigOrigin}/runs/${created.id}`;
    try {
      const run = parseRun(created);
      const described = parseRun(await json(request, root));
      expect(described).toEqual(run);
      for (const store of run.stores) {
        // The proxy must reach the same API process and preserve each real store's Host header.
        const headers = { Host: `${store.host}:${environment.storefrontPort}` };
        const proxy = await request.get(`${environment.frontendOrigin}/api/e2e/`, { headers, maxRedirects: 0 });
        expect(proxy.status()).toBe(200); expect(parseMarker(await proxy.json())).toEqual(marker);
        const response = await request.get(`${environment.frontendOrigin}/api/storefront/store`, { headers, maxRedirects: 0 });
        expect(response.status()).toBe(200);
        const served = await response.json() as { id: string; name: string; currency: string; culture: string };
        expect({ id: served.id, name: served.name }).toEqual({ id: store.id, name: store.name });
        expect({ currency: served.currency, culture: served.culture }).toEqual({ currency: "CZK", culture: "cs-CZ" });
      }
      const session: RunSession = {
        environment, run, marker, origin: store => {
          if (!run.stores.some(candidate => candidate.id === store.id && candidate.host === store.host)) throw new Error("Foreign store target.");
          return storeOrigin(environment, store);
        },
        order: (store, number) => {
          if (!run.stores.some(candidate => candidate.id === store.id)) throw new Error("Foreign order target.");
          return json(request, `${root}/stores/${store.id}/orders/${encodeURIComponent(number)}`);
        },
        stock: sku => {
          if (!run.products.some(product => product.variants.some(variant => variant.sku === sku))) throw new Error("Foreign stock target.");
          return json(request, `${root}/stock/${encodeURIComponent(sku)}`);
        },
        mail: to => {
          if (!to.endsWith(`@${run.emailDomain}`)) throw new Error("Foreign mail target.");
          return json(request, `${root}/mail?to=${encodeURIComponent(to)}`);
        },
        gateway: () => json(request, `${root}/comgate/`),
        pushAgain: async id => { await json(request, `${root}/comgate/transactions/${encodeURIComponent(id)}/push`, 200, "POST"); },
        advanceReturn: async (store, orderNumber, returnNumber, action) => {
          if (!run.stores.some(candidate => candidate.id === store.id)) throw new Error("Foreign return target.");
          const owner = parseRunOwner(created, run);
          const admin = await playwright.request.newContext({ timeout: 10_000 });
          try {
            const signedIn = await admin.post(`${environment.apiOrigin}/api/admin/auth/login`, { data: owner, maxRedirects: 0, maxRetries: 0 });
            expect(signedIn.status(), "Run owner sign-in").toBe(200);
            const path = `${environment.apiOrigin}/api/admin/stores/${store.id}/returns`;
            const found = (await json<MerchantReturn[]>(admin, path)).filter(candidate => candidate.orderNumber === orderNumber && candidate.number === returnNumber);
            expect(found, "Exactly this run store's requested return").toHaveLength(1);
            expect(found[0].id).toMatch(/^[0-9a-f-]{36}$/i);
            expect(found[0].status).toBe(action === "accept" ? "Requested" : "Accepted");
            const response = await admin.post(`${path}/${found[0].id}/${action}`, {
              headers: action === "receive" ? { "Idempotency-Key": randomUUID() } : {}, maxRedirects: 0, maxRetries: 0,
            });
            expect(response.status(), `Run owner ${action}`).toBe(200);
            const advanced = await response.json() as MerchantReturn;
            expect(advanced).toMatchObject({ id: found[0].id, number: returnNumber, orderNumber, status: action === "accept" ? "Accepted" : "Received" });
            return advanced;
          } finally { await admin.dispose(); }
        },
      };
      for (const variant of run.products.flatMap(product => product.variants)) {
        expect(await session.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock, reserved: 0, available: variant.stock });
      }
      await testInfo.attach("isolated-run", { body: JSON.stringify({ ...run, marker, apiOrigin: environment.apiOrigin, rigOrigin: environment.rigOrigin }), contentType: "application/json" });
      await use(session);
    } finally {
      const ended = await request.delete(root, { maxRedirects: 0, maxRetries: 0, timeout: 15_000 });
      expect(ended.status(), "End only this test's run").toBe(204);
      expect((await request.get(root, { maxRedirects: 0 })).status()).toBe(404);
    }
  },
  isolatedBrowser: [async ({ context, runSession }, use, testInfo) => {
    const { environment, run } = runSession;
    const origins = new Set(run.stores.map(store => runSession.origin(store)));
    const blocked: string[] = [], pageErrors: string[] = [], consoleErrors: string[] = [], apiErrors: string[] = [], writes: string[] = [];
    context.on("page", page => {
      page.on("pageerror", error => pageErrors.push(error.message));
      page.on("console", message => { if (message.type() === "error") consoleErrors.push(message.text()); });
      page.on("response", response => {
        const path = new URL(response.url()).pathname;
        const guest = path === "/api/storefront/account/me" && response.status() === 401 && response.request().method() === "GET";
        if (path.startsWith("/api/") && response.status() >= 400 && !guest) apiErrors.push(`${response.status()} ${response.request().method()} ${path}`);
      });
    });
    await context.route("**/*", async route => {
      const request = route.request(), url = new URL(request.url());
      const read = ["GET", "HEAD", "OPTIONS"].includes(request.method());
      if (!read && !allowedBrowserWrite(request.method(), url, origins, environment.rigOrigin)) { blocked.push(`${request.method()} ${url.origin}${url.pathname}`); await route.abort("blockedbyclient"); }
      else { if (!read) writes.push(`${request.method()} ${url.origin}${url.pathname}`); await route.continue(); }
    });
    await use();
    const path = testInfo.outputPath("browser-errors.json");
    writeFileSync(path, JSON.stringify({ blocked, pageErrors, consoleErrors, apiErrors, writes }, null, 2));
    await testInfo.attach("browser-errors", { path, contentType: "application/json" });
    expect(blocked, "Write outside this run's storefronts or fake payment page").toEqual([]);
    expect(pageErrors, "Uncaught browser errors").toEqual([]);
    expect(apiErrors, "Unexpected API errors").toEqual([]);
    expect(consoleErrors.filter(message => message.includes("Encountered two children with the same key")), "Duplicate React keys").toEqual([]);
  }, { auto: true }],
});
export { expect };
