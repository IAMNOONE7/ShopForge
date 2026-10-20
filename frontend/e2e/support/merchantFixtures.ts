import { expect, type Page } from "@playwright/test";
import { writeFileSync } from "node:fs";
import { runTest, type RunSession, type StockReading } from "./isolatedFixtures.ts";
import { parseMarker, parseRunOwner } from "./isolatedEnvironment.ts";
import { loadMerchantEnvironment } from "./merchantEnvironment.ts";
import { allowedMerchantWrite, merchantScope, recordMerchantResult } from "./merchantBoundary.ts";
import en from "../../admin/src/i18n/locales/en.ts";
import cs from "../../admin/src/i18n/locales/cs.ts";

export type MerchantSession = {
  origin: string; words: typeof en.resources | typeof cs.resources;
  signIn: (page: Page) => Promise<void>;
  stock: (sku: string) => Promise<StockReading>;
  registeredOrder: (store: string, number: string) => boolean;
};
export const test = runTest.extend<{ merchant: MerchantSession; merchantBrowser: void }>({
  merchant: async ({ request, runSession }, use, testInfo) => {
    const environment = loadMerchantEnvironment(), run = runSession.run;
    const marker = await request.get(`${environment.adminOrigin}/api/e2e/`, { timeout: 5_000, maxRedirects: 0 });
    expect(marker.status()).toBe(200); expect(parseMarker(await marker.json())).toEqual(runSession.marker);
    const response = await request.get(`${environment.rigOrigin}/runs/${run.id}`, { timeout: 5_000, maxRedirects: 0 });
    expect(response.status()).toBe(200); const owner = parseRunOwner(await response.json(), run);
    const words = (testInfo.project.name.startsWith("cs-") ? cs : en).resources;
    await use({ origin: environment.adminOrigin, words, signIn: async page => {
      await page.goto(`${environment.adminOrigin}/admin/stores/${run.stores[0].id}/settings`);
      await page.locator('input[name="email"]').fill(owner.email); await page.locator('input[name="password"]').fill(owner.password);
      const response = page.waitForResponse(answer => new URL(answer.url()).pathname === "/api/admin/auth/login" && answer.request().method() === "POST");
      await page.getByRole("button", { name: words.auth.signIn, exact: true }).click();
      const signedIn = await response; expect(signedIn.status()).toBe(200); expect(await signedIn.json()).toMatchObject({ user: { tenantId: run.tenantId, role: "Owner" } });
      await expect(page.locator(".publication-panel")).toBeVisible();
    }, registeredOrder: () => false, stock: async () => { throw new Error("Browser boundary is not initialized."); } });
  },
  merchantBrowser: [async ({ context, request, runSession, merchant }, use, testInfo) => {
    const scope = merchantScope(runSession.run, merchant.origin, runSession.environment.rigOrigin,
      new Map(runSession.run.stores.map(store => [runSession.origin(store), store.id])), `owner@${runSession.run.emailDomain}`);
    merchant.stock = async sku => {
      if (!scope.skus.has(sku)) throw new Error("Unregistered stock target.");
      const answer = await request.get(`${scope.rigOrigin}/runs/${scope.runId}/stock/${encodeURIComponent(sku)}`, { timeout: 5_000, maxRedirects: 0 });
      expect(answer.status()).toBe(200); return await answer.json() as StockReading;
    };
    merchant.registeredOrder = (store, number) => scope.orders.get(store)?.has(number) === true;
    const blocked: string[] = [], pageErrors: string[] = [], apiErrors: string[] = [], consoleErrors: string[] = [], writeFailures: string[] = [];
    const writes: { method: string; path: string; fields: string[] }[] = [];
    const origins = new Set([scope.adminOrigin, scope.rigOrigin, ...scope.storefronts.keys()]);
    context.on("page", page => {
      page.on("pageerror", error => pageErrors.push(error.message));
      page.on("console", message => { if (message.type() === "error") consoleErrors.push(message.text()); });
      page.on("requestfailed", request => { if (!["GET", "HEAD", "OPTIONS"].includes(request.method())) writeFailures.push(`${request.method()} ${new URL(request.url()).pathname}`); });
      page.on("response", response => {
        const url = new URL(response.url()), method = response.request().method();
        const expectedGuest = method === "GET" && response.status() === 401 && ["/api/admin/auth/me", "/api/storefront/account/me"].includes(url.pathname);
        if (url.pathname.startsWith("/api/") && response.status() >= 400 && !expectedGuest) apiErrors.push(`${response.status()} ${method} ${url.origin}${url.pathname}`);
      });
    });
    await context.route("**/*", async route => {
      const request = route.request(), url = new URL(request.url()), method = request.method(), read = ["GET", "HEAD", "OPTIONS"].includes(method);
      let body: unknown = null; try { body = request.postDataJSON(); } catch { /* The rig payment form is URL encoded. */ }
      if (!origins.has(url.origin) || !read && !allowedMerchantWrite(scope, method, url, body)) {
        blocked.push(`${method} ${url.origin}${url.pathname}`); return route.abort("blockedbyclient");
      }
      if (!read) writes.push({ method, path: url.pathname, fields: body && typeof body === "object" ? Object.keys(body) : [] });
      const learn = url.origin === scope.adminOrigin && (method === "POST" || method === "GET" && /\/(?:shipping|payment)-methods$/.test(url.pathname))
        || method === "POST" && url.pathname === "/api/storefront/checkout";
      if (!learn) return route.continue();
      // Forward once, with no redirect/retry; deliver the real response unchanged after validating new identities.
      const response = await route.fetch({ maxRedirects: 0, maxRetries: 0, timeout: 20_000 });
      if (response.ok() && response.headers()["content-type"]?.includes("application/json")) recordMerchantResult(scope, method, url, body, await response.json());
      return route.fulfill({ response });
    });
    await use();
    const path = testInfo.outputPath("merchant-browser.json");
    writeFileSync(path, JSON.stringify({ blocked, pageErrors, apiErrors, consoleErrors, writeFailures, writes }, null, 2));
    await testInfo.attach("merchant-browser", { path, contentType: "application/json" });
    expect(blocked, "Requests outside this run's merchant/shopper boundary").toEqual([]);
    expect(pageErrors, "Uncaught browser errors").toEqual([]); expect(apiErrors, "Unexpected API failures").toEqual([]); expect(writeFailures, "Failed write transports").toEqual([]);
    expect(consoleErrors.filter(message => !message.startsWith("Failed to load resource: the server responded with a status of 401 ")), "Unexpected browser console errors").toEqual([]);
  }, { auto: true }],
});
export async function adminRead<T>(page: Page, session: RunSession, path: string): Promise<T> {
  const root = "/api/admin/";
  if (!path.startsWith(root) || path.includes("?") || path.includes("..")) throw new Error("Expected an admin read path.");
  const user = await page.evaluate(async () => { const response = await fetch("/api/admin/auth/me"); if (!response.ok) throw new Error("Owner session is unavailable."); return response.json(); });
  expect(user).toMatchObject({ tenantId: session.run.tenantId, role: "Owner" });
  return page.evaluate(async path => { const response = await fetch(path); if (!response.ok) throw new Error(`Admin read returned ${response.status}`); return response.json(); }, path);
}
export { expect };
