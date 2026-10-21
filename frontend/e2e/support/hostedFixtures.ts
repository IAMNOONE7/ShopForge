import { test as base, expect, type APIResponse, type Page, type TestInfo } from "@playwright/test";
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { loadHostedEnvironment, hostedStoreOrigin, allowedHostedWrite, adminPolicy, storefrontPolicy, type HostedEnvironment } from "./hostedEnvironment.ts";
import { parseMarker, parseRun, parseRunOwner, type IsolatedRun } from "./isolatedEnvironment.ts";
import en from "../../admin/src/i18n/locales/en.ts";
import cs from "../../admin/src/i18n/locales/cs.ts";

type App = "admin" | "storefront";
type HostedSession = {
  environment: HostedEnvironment; run: IsolatedRun; words: typeof en.resources | typeof cs.resources;
  origin: (store: IsolatedRun["stores"][number]) => string;
  read: (origin: string, path: string, headers?: Record<string, string>) => Promise<APIResponse>;
  entry: (origin: string, path: string, app: App) => Promise<APIResponse>;
  signIn: (page: Page) => Promise<void>;
};
const digest = (body: Buffer) => createHash("sha256").update(body).digest("hex");

export async function attachHostedFacts(testInfo: TestInfo, name: string, facts: unknown) {
  const path = testInfo.outputPath(`${name}.json`); writeFileSync(path, JSON.stringify(facts, null, 2));
  await testInfo.attach(name, { path, contentType: "application/json" });
}
export function buildFile(app: App, file: string) {
  if (file !== "index.html" && !/^assets\/[a-zA-Z0-9_.-]+\.(?:js|css)$/.test(file)) throw new Error("Expected a built entry or asset path.");
  const root = process.env.SHOPFORGE_E2E_HOSTED_ROOT;
  if (!root) throw new Error("Set SHOPFORGE_E2E_HOSTED_ROOT to the packaged frontends directory.");
  return readFileSync(resolve(root, app, file));
}
export function expectPageHeaders(response: APIResponse, app: App) {
  expect(response.status()).toBe(200); expect(response.headers()["content-type"]).toMatch(/^text\/html\b/);
  expect(response.headers()["cache-control"]).toBe("no-cache");
  expect(response.headers()["content-security-policy"]).toBe(app === "admin" ? adminPolicy : storefrontPolicy);
  expect(response.headers()["referrer-policy"]).toBe(app === "admin" ? "no-referrer" : "strict-origin-when-cross-origin");
}
export function expectAssetHeaders(headers: Record<string, string>, path: string) {
  expect(headers["cache-control"]).toBe("public, max-age=31536000, immutable");
  expect(headers["content-type"]).toMatch(path.endsWith(".css") ? /^text\/css\b/ : /^(?:text|application)\/javascript\b/);
}

export const test = base.extend<{ hosted: HostedSession; hostedBrowser: void }>({
  hosted: async ({ request }, use, testInfo) => {
    const environment = loadHostedEnvironment();
    const read = (origin: string, path: string, headers: Record<string, string> = {}) => {
      const target = new URL(origin);
      if (target.port !== new URL(environment.apiOrigin).port || target.protocol !== "http:" || !target.hostname.endsWith(".localhost")) throw new Error("Expected an API-hosted loopback origin.");
      if (!path.startsWith("/") || path.startsWith("//")) throw new Error("Expected a local request path.");
      return request.get(environment.apiOrigin + path, { headers: { ...headers, Host: target.host }, timeout: 10_000, maxRedirects: 0, maxRetries: 0 });
    };
    for (const path of ["/health/live", "/health/ready"]) expect((await request.get(environment.apiOrigin + path, { timeout: 5_000 })).status()).toBe(200);
    const markerResponse = await request.get(`${environment.apiOrigin}/api/e2e/`, { timeout: 5_000 });
    expect(markerResponse.status()).toBe(200); const marker = parseMarker(await markerResponse.json());
    expect((await request.get(`${environment.rigOrigin}/health`, { timeout: 5_000 })).status()).toBe(200);
    // Fail before creating data when the API is not configured to host the actual package.
    for (const origin of [environment.adminOrigin, environment.edgeOrigin]) {
      const root = await read(origin, "/"); expect(root.status()).toBe(302); expect(root.headers().location).toBe("/admin/");
    }
    const entry = async (origin: string, path: string, app: App) => {
      const response = await read(origin, path); expectPageHeaders(response, app);
      expect(digest(await response.body()), "Served HTML must match the package").toBe(digest(buildFile(app, "index.html")));
      return response;
    };
    await entry(environment.adminOrigin, "/admin/", "admin");
    const createdResponse = await request.post(`${environment.rigOrigin}/runs`, { timeout: 60_000, maxRetries: 0, maxRedirects: 0 });
    expect(createdResponse.status()).toBe(201); const created: unknown = await createdResponse.json();
    const id = created && typeof created === "object" && "id" in created ? created.id : null;
    if (typeof id !== "string" || !/^r[0-9a-f]{8}$/.test(id)) throw new Error("The rig returned no usable run ID.");
    try {
      const run = parseRun(created), owner = parseRunOwner(created, run);
      const words = (testInfo.project.name.startsWith("cs-") ? cs : en).resources;
      const origin = (store: IsolatedRun["stores"][number]) => {
        if (!run.stores.some(item => item.id === store.id && item.host === store.host)) throw new Error("Foreign store target.");
        return hostedStoreOrigin(environment, store);
      };
      for (const store of run.stores) {
        const actual = await read(origin(store), "/api/storefront/store"); expect(actual.status()).toBe(200);
        expect(await actual.json()).toMatchObject({ id: store.id, name: store.name, currency: "CZK", culture: "cs-CZ" });
        const checked = await read(origin(store), "/api/e2e/"); expect(checked.status()).toBe(200); expect(parseMarker(await checked.json())).toEqual(marker);
        await entry(origin(store), "/", "storefront");
      }
      await attachHostedFacts(testInfo, "hosted-targets", { backend: marker, runId: run.id, stores: run.stores, adminOrigin: environment.adminOrigin, edgeOrigin: environment.edgeOrigin,
        entries: { admin: digest(buildFile("admin", "index.html")), storefront: digest(buildFile("storefront", "index.html")) } });
      await use({ environment, run, origin, read, entry, words, signIn: async page => {
        try {
          await page.locator('[name="email"]').fill(owner.email); await page.locator('[name="password"]').fill(owner.password);
          const signedIn = page.waitForResponse(response => new URL(response.url()).pathname === "/api/admin/auth/login" && response.request().method() === "POST");
          await page.getByRole("button", { name: words.auth.signIn, exact: true }).click();
          const response = await signedIn; expect(response.status()).toBe(200); expect(await response.json()).toMatchObject({ user: { tenantId: run.tenantId, role: "Owner" } });
        } catch { await page.close(); throw new Error("Run-owner sign-in failed; credential-bearing diagnostics were omitted."); }
      } });
    } finally {
      const response = await request.delete(`${environment.rigOrigin}/runs/${id}`, { timeout: 15_000, maxRetries: 0, maxRedirects: 0 });
      expect([204, 404]).toContain(response.status());
      const reading = await request.get(`${environment.rigOrigin}/runs/${id}`, { timeout: 5_000, maxRetries: 0, maxRedirects: 0 });
      await attachHostedFacts(testInfo, "hosted-cleanup", { runId: id, status: response.status(), readAfterCleanup: reading.status() });
      expect(reading.status()).toBe(404);
    }
  },
  hostedBrowser: [async ({ context, hosted }, use, testInfo) => {
    const origins = new Set([hosted.environment.adminOrigin, hosted.environment.edgeOrigin, ...hosted.run.stores.map(hosted.origin)]);
    const blocked: string[] = [], pageErrors: string[] = [], apiErrors: string[] = [], consoleErrors: string[] = [], violations: unknown[] = [], assetFailures: string[] = [], readFailures: string[] = [];
    const writes: string[] = [], assets: { path: string; sha256: string }[] = [], checks: Promise<void>[] = [];
    await context.exposeBinding("recordHostedPolicyViolation", (_source, value: unknown) => { violations.push(value); });
    await context.addInitScript(language => {
      if (location.protocol !== "http:") return;
      localStorage.setItem("shopforge.admin.uiLanguage", language); localStorage.setItem("shopforge.storefront.uiLanguage", language);
      document.addEventListener("securitypolicyviolation", event => {
        (window as unknown as { recordHostedPolicyViolation: (value: unknown) => void }).recordHostedPolicyViolation({ directive: event.effectiveDirective, blocked: event.blockedURI });
      });
    }, testInfo.project.name.split("-")[0]);
    context.on("page", page => {
      page.on("pageerror", error => pageErrors.push(error.message));
      page.on("console", message => { if (message.type() === "error") consoleErrors.push(message.text()); });
      page.on("requestfailed", request => {
        const path = new URL(request.url()).pathname;
        if (/\.(?:js|css)$/.test(path)) assetFailures.push(path);
        if (path.startsWith("/api/") && request.failure()?.errorText !== "net::ERR_ABORTED") readFailures.push(`${request.method()} ${path}`);
      });
      page.on("response", response => {
        const url = new URL(response.url()), path = url.pathname;
        if (path.startsWith("/api/") && response.status() >= 400 && !(response.status() === 401 && response.request().method() === "GET"
          && ["/api/admin/auth/me", "/api/platform/auth/me", "/api/storefront/account/me"].includes(path))) apiErrors.push(`${response.status()} ${path}`);
        if (!/^(?:\/admin)?\/assets\/[^/]+\.(?:js|css)$/.test(path)) return;
        checks.push((async () => {
          try {
            expect(response.status()).toBe(200); expectAssetHeaders(response.headers(), path);
            const app = path.startsWith("/admin/") ? "admin" : "storefront", file = app === "admin" ? path.slice(7) : path.slice(1);
            const sha256 = digest(await response.body()); expect(sha256).toBe(digest(buildFile(app, file))); assets.push({ path, sha256 });
          } catch { assetFailures.push(path); }
        })());
      });
    });
    await context.route("**/*", async route => {
      const request = route.request(), url = new URL(request.url()), read = ["GET", "HEAD", "OPTIONS"].includes(request.method());
      let body: unknown = null; try { body = request.postDataJSON(); } catch { /* Non-JSON writes are refused below. */ }
      if (!origins.has(url.origin) || !read && !allowedHostedWrite(hosted.run, hosted.environment, request.method(), url, body)) {
        blocked.push(`${request.method()} ${url.origin}${url.pathname}`); return route.abort("blockedbyclient");
      }
      if (!read) writes.push(`${request.method()} ${url.pathname}`);
      return route.continue();
    });
    await use();
    await Promise.all(checks);
    await attachHostedFacts(testInfo, "hosted-browser", { blocked, pageErrors, apiErrors, readFailures, consoleErrors, violations, assetFailures, assets, writes });
    expect(blocked).toEqual([]); expect(pageErrors).toEqual([]); expect(apiErrors).toEqual([]); expect(readFailures).toEqual([]); expect(violations).toEqual([]); expect(assetFailures).toEqual([]);
    expect(consoleErrors.filter(message => !message.startsWith("Failed to load resource: the server responded with a status of 401 "))).toEqual([]);
  }, { auto: true }],
});
export { expect };
