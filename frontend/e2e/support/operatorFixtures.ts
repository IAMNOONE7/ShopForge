import { expect, type Page } from "@playwright/test";
import { writeFileSync } from "node:fs";
import { runTest } from "./isolatedFixtures.ts";
import { parseMarker, parseRun, parseRunOwner, type IsolatedRun } from "./isolatedEnvironment.ts";
import { loadMerchantEnvironment } from "./merchantEnvironment.ts";
import { loadOperatorCredentials } from "./operatorEnvironment.ts";
import { allowedOperatorWrite, operatorScope } from "./operatorBoundary.ts";
import en from "../../admin/src/i18n/locales/en.ts";
import cs from "../../admin/src/i18n/locales/cs.ts";

export type OperatorSession = {
  origin: string; words: typeof en.resources | typeof cs.resources;
  signIn: (page: Page) => Promise<void>; signInOwner: (page: Page) => Promise<void>;
  expectSuspendedReads: boolean;
};
export const test = runTest.extend<{ operator: OperatorSession; operatorBrowser: void; controlRun: IsolatedRun }>({
  operator: async ({ request, runSession }, use, testInfo) => {
    const environment = loadMerchantEnvironment(), credentials = loadOperatorCredentials(environment), run = runSession.run;
    const marker = await request.get(`${environment.adminOrigin}/api/e2e/`, { timeout: 5_000, maxRedirects: 0 });
    expect(marker.status()).toBe(200); expect(parseMarker(await marker.json())).toEqual(runSession.marker);
    const described = await request.get(`${environment.rigOrigin}/runs/${run.id}`, { timeout: 5_000, maxRedirects: 0 });
    expect(described.status()).toBe(200); const owner = parseRunOwner(await described.json(), run);
    const words = (testInfo.project.name.startsWith("cs-") ? cs : en).resources;
    async function submitCredentials(page: Page, email: string, password: string, path: string) {
      // A failed fill/click must not copy the password into Playwright's assertion call log.
      try {
        await page.locator('input[name="email"]').fill(email); await page.locator('input[name="password"]').fill(password);
        const response = page.waitForResponse(answer => new URL(answer.url()).pathname === path && answer.request().method() === "POST");
        await page.getByRole("button", { name: words.auth.signIn, exact: true }).click();
        const answer = await response; expect(answer.status()).toBe(200); return await answer.json() as unknown;
      } catch {
        await page.close().catch(() => undefined);
        throw new Error("Disposable sign-in did not complete successfully.");
      }
    }
    await use({ origin: environment.adminOrigin, words, expectSuspendedReads: false,
      signIn: async page => {
        await page.goto(`${environment.adminOrigin}/admin/platform/tenants`);
        expect(await submitCredentials(page, credentials.email, credentials.password, "/api/platform/auth/login")).toMatchObject({ twoFactorRequired: false, ticket: null, user: { email: credentials.email } });
        await expect(page.locator(".operator-tenants")).toBeVisible();
      },
      signInOwner: async page => {
        await page.goto(`${environment.adminOrigin}/admin/stores/${run.stores[0].id}/settings`);
        expect(await submitCredentials(page, owner.email, owner.password, "/api/admin/auth/login")).toMatchObject({ user: { tenantId: run.tenantId, role: "Owner" } });
        await expect(page.locator(".publication-panel")).toBeVisible();
      },
    });
  },
  controlRun: async ({ request, runSession }, use) => {
    const root = runSession.environment.rigOrigin;
    const created = await request.post(`${root}/runs`, { maxRedirects: 0, maxRetries: 0, timeout: 60_000 }); expect(created.status()).toBe(201);
    const run = parseRun(await created.json()); expect(run.tenantId).not.toBe(runSession.run.tenantId);
    try { await use(run); }
    finally {
      expect((await request.delete(`${root}/runs/${run.id}`, { maxRedirects: 0, maxRetries: 0, timeout: 15_000 })).status()).toBe(204);
      expect((await request.get(`${root}/runs/${run.id}`, { maxRedirects: 0 })).status()).toBe(404);
    }
  },
  operatorBrowser: [async ({ context, runSession, operator }, use, testInfo) => {
    const scope = operatorScope(runSession.run, operator.origin, runSession.origin(runSession.run.stores[0]));
    const storefronts = new Set(runSession.run.stores.map(store => runSession.origin(store))), origins = new Set([operator.origin, ...storefronts]);
    const blocked: string[] = [], pageErrors: string[] = [], apiErrors: string[] = [], consoleErrors: string[] = [], writeFailures: string[] = [], expectedDenials: string[] = [];
    const writes: { method: string; path: string; fields: string[] }[] = [];
    await context.addInitScript(language => {
      if (location.protocol !== "http:") return;
      localStorage.setItem("shopforge.admin.uiLanguage", language); localStorage.setItem("shopforge.storefront.uiLanguage", language);
    }, testInfo.project.name.split("-")[0]);
    context.on("page", page => {
      page.on("pageerror", error => pageErrors.push(error.message));
      page.on("console", message => { if (message.type() === "error") consoleErrors.push(message.text()); });
      page.on("requestfailed", request => { if (!["GET", "HEAD", "OPTIONS"].includes(request.method())) writeFailures.push(`${request.method()} ${new URL(request.url()).pathname}`); });
      page.on("response", response => {
        const url = new URL(response.url()), method = response.request().method();
        if (!url.pathname.startsWith("/api/") || response.status() < 400) return;
        const anonymous = method === "GET" && response.status() === 401 && ["/api/platform/auth/me", "/api/admin/auth/me", "/api/storefront/account/me"].includes(url.pathname);
        const suspended = method === "GET" && operator.expectSuspendedReads && (
          response.status() === 404 && storefronts.has(url.origin) && url.pathname.startsWith("/api/storefront/")
          || response.status() === 401 && url.origin === operator.origin && url.pathname.startsWith("/api/admin/"));
        const description = `${response.status()} ${method} ${url.origin}${url.pathname}`;
        if (anonymous || suspended) expectedDenials.push(description); else apiErrors.push(description);
      });
    });
    await context.route("**/*", async route => {
      const request = route.request(), url = new URL(request.url()), method = request.method(), read = ["GET", "HEAD", "OPTIONS"].includes(method);
      let body: unknown = null; try { body = request.postDataJSON(); } catch { if (request.postData()) body = "non-json"; }
      if (request.postData() !== null && body === null) body = "unexpected-body";
      if (!origins.has(url.origin) || !read && !allowedOperatorWrite(scope, method, url, body)) {
        blocked.push(`${method} ${url.origin}${url.pathname}`); return route.abort("blockedbyclient");
      }
      if (!read) writes.push({ method, path: url.pathname, fields: body && typeof body === "object" ? Object.keys(body) : [] });
      return route.continue();
    });
    await use();
    const path = testInfo.outputPath("operator-browser.json");
    writeFileSync(path, JSON.stringify({ runId: runSession.run.id, tenantId: scope.tenantId, blocked, pageErrors, apiErrors, consoleErrors, writeFailures, expectedDenials, writes }, null, 2));
    await testInfo.attach("operator-browser", { path, contentType: "application/json" });
    expect(blocked, "Requests outside the run-owned operator boundary").toEqual([]); expect(pageErrors, "Uncaught browser errors").toEqual([]);
    expect(apiErrors, "Unexpected API failures").toEqual([]); expect(writeFailures, "Failed write transports").toEqual([]);
    expect(consoleErrors.filter(message => !/^Failed to load resource: the server responded with a status of (401|404) /.test(message)), "Unexpected console errors").toEqual([]);
  }, { auto: true }],
});
export async function operatorRead<T>(page: Page, path: string): Promise<{ status: number; body: T | null }> {
  if (!/^\/api\/(?:platform\/(?:auth\/me|tenants)|admin\/(?:auth\/me|stores)|storefront\/(?:store|account\/me))$/.test(path)) throw new Error("Expected an operator-journey read path.");
  return page.evaluate(async path => { const answer = await fetch(path, { method: "GET" }); return { status: answer.status, body: answer.headers.get("content-type")?.includes("json") ? await answer.json() : null }; }, path);
}
export { expect };
