import { test, expect, buildFile, expectAssetHeaders } from "../support/hostedFixtures.ts";
import { cartReading, labels } from "../support/shopper.ts";

test("serves separate package entries and excludes API, health, crawler documents and missing files from SPA fallback", async ({ hosted }) => {
  const storeOrigin = hosted.origin(hosted.run.stores[0]), adminOrigin = hosted.environment.adminOrigin;
  for (const [origin, app, paths] of [
    [storeOrigin, "storefront", ["/", `/p/${hosted.run.products[0].slug}`, "/checkout"]],
    [adminOrigin, "admin", ["/admin", "/admin/", `/admin/stores/${hosted.run.stores[0].id}/orders`, "/admin/platform/tenants"]],
    [hosted.environment.edgeOrigin, "admin", ["/admin/", "/admin/platform/tenants"]],
  ] as const) {
    for (const path of paths) await hosted.entry(origin, path, app);
    const html = buildFile(app, "index.html").toString();
    const scripts = [...html.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi)];
    expect(scripts.length).toBeGreaterThan(0); expect(scripts.every(match => match[1].trim() === "" && /\bsrc="[^"]+"/.test(match[0]))).toBe(true);
    expect(html).not.toMatch(/\son[a-z]+\s*=/i);
    if (app === "admin") expect(html).not.toMatch(/<style\b|\sstyle\s*=/i);
    const entries = [...html.matchAll(/(?:src|href)="([^"]+\.(?:js|css))"/g)].map(match => match[1]);
    expect(entries.length).toBeGreaterThanOrEqual(2);
    for (const path of entries) {
      expect(path).toMatch(app === "admin" ? /^\/admin\/assets\/[^/]+\.(js|css)$/ : /^\/assets\/[^/]+\.(js|css)$/);
      const asset = await hosted.read(origin, path); expect(asset.status()).toBe(200); expectAssetHeaders(asset.headers(), path);
      expect(await asset.body()).toEqual(buildFile(app, app === "admin" ? path.slice(7) : path.slice(1)));
      expect(asset.headers().etag).toBeTruthy();
      const head = await hosted.read(origin, path, { "If-None-Match": asset.headers().etag });
      expect(head.status()).toBe(304); expect(head.headers()["cache-control"]).toContain("immutable");
    }
  }
  for (const path of ["/admin", "/admin/", "/admin/platform", "/admin/assets/missing.js"]) expect((await hosted.read(storeOrigin, path)).status()).toBe(404);
  for (const origin of [storeOrigin, adminOrigin]) {
    for (const path of ["/api", "/api/hosting-smoke-missing", "/health", "/health/hosting-smoke-missing", "/missing.js", "/assets/missing.js", "/admin/assets/missing.js"]) {
      const answer = await hosted.read(origin, path); expect(answer.status()).toBe(404); expect(answer.headers()["content-type"] ?? "").not.toContain("text/html");
    }
  }
  for (const path of ["/robots.txt", "/sitemap.xml"]) {
    const answer = await hosted.read(storeOrigin, path); expect(answer.status()).toBe(200);
    expect(answer.headers()["content-type"]).toContain(path.endsWith("txt") ? "text/plain" : "xml"); expect(await answer.text()).not.toContain('<div id="root">');
  }
});

test("boots product deep links on both store hosts and keeps real cart cookies separate", async ({ page, context, hosted }, testInfo) => {
  const [a, b] = hosted.run.stores, product = hosted.run.products.find(product => product.variants.length === 1)!;
  const origin = hosted.origin(a), words = labels(testInfo), variant = product.variants[0];
  await page.goto(`${origin}/p/${product.slug}`); await expect(page.locator("main h1")).toHaveText(product.name);
  await expect(page.locator("html")).toHaveAttribute("lang", testInfo.project.name.split("-")[0]);
  await page.reload(); await expect(page.locator("main h1")).toHaveText(product.name);
  const added = page.waitForResponse(response => new URL(response.url()).pathname === "/api/storefront/cart/items" && response.request().method() === "POST");
  await page.getByRole("button", { name: words.catalog.addProductToCart.replace("{{name}}", product.name), exact: true }).click();
  expect((await added).status()).toBe(200); await expect(page.locator(".add-to-cart-status")).toContainText(words.catalog.viewCart);
  expect((await cartReading(page)).items).toEqual([expect.objectContaining({ storeProductId: product.listings[a.id], variantId: variant.id, quantity: 1 })]);
  const other = await context.newPage(); await other.goto(`${hosted.origin(b)}/p/${product.slug}`); await expect(other.locator("main h1")).toHaveText(product.name);
  expect((await cartReading(other)).items).toEqual([]);
  for (const [target, store] of [[page, a], [other, b]] as const) {
    expect(await target.evaluate(async () => (await (await fetch("/api/storefront/store")).json()).id)).toBe(store.id);
  }
  await page.reload(); expect((await cartReading(page)).items).toHaveLength(1);
  await page.locator('header a[href="/cart"]').click();
  await expect(page).toHaveURL(`${origin}/cart`); await page.reload(); await expect(page.locator("main")).toContainText(product.name);
  const screenshot = testInfo.outputPath("hosted-storefront.png"); await page.screenshot({ path: screenshot, caret: "initial" });
  await testInfo.attach("hosted-storefront", { path: screenshot, contentType: "image/png" });
});

test("refreshes authenticated admin deep links and loads lazy screens under the production policy", async ({ page, context, hosted }, testInfo) => {
  const origin = hosted.environment.adminOrigin, store = hosted.run.stores[0], orders = `${origin}/admin/stores/${store.id}/orders`;
  await page.goto(orders); await hosted.signIn(page);
  await expect(page.getByRole("heading", { name: hosted.words.orders.noOrdersTitle, exact: true })).toBeVisible();
  const cookie = (await context.cookies(origin)).find(cookie => cookie.name === "shopforge_admin");
  expect(cookie).toMatchObject({ domain: new URL(origin).hostname, path: "/", httpOnly: true });
  await expect(page).toHaveURL(orders); await page.reload();
  await expect(page.getByRole("heading", { name: hosted.words.orders.noOrdersTitle, exact: true })).toBeVisible();
  const menu = page.getByRole("button", { name: hosted.words.navigation.menu, exact: true });
  if (await menu.isVisible()) { await menu.focus(); await page.keyboard.press("Enter"); }
  await page.locator(`a[href="/admin/stores/${store.id}/domains"]:visible`).click();
  await expect(page.getByRole("heading", { name: hosted.words.domains.title, exact: true })).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("lang", testInfo.project.name.split("-")[0]);
  expect(await page.locator("[style], style, script:not([src])").count()).toBe(0);
  await page.reload(); await expect(page.getByRole("heading", { name: hosted.words.domains.title, exact: true })).toBeVisible();
  const screenshot = testInfo.outputPath("hosted-admin.png"); await page.screenshot({ path: screenshot, caret: "initial" });
  await testInfo.attach("hosted-admin", { path: screenshot, contentType: "image/png" });
  const other = await context.newPage(); await other.goto(`${hosted.environment.edgeOrigin}/admin/`);
  await expect(other.locator('[name="password"]')).toBeVisible();
  expect(await other.evaluate(async () => (await fetch("/api/admin/auth/me")).status)).toBe(401);
  await other.goto(`${hosted.origin(store)}/p/${hosted.run.products[0].slug}`);
  await expect(other.locator("main h1")).toHaveText(hosted.run.products[0].name);
  expect(await other.evaluate(async () => (await fetch("/api/admin/auth/me")).status)).toBe(401);
  await other.goto(`${origin}/admin/platform/tenants`); await expect(other.locator('[name="password"]')).toBeVisible();
  expect(await other.evaluate(async () => (await fetch("/api/platform/auth/me")).status)).toBe(401);
  expect(await page.evaluate(async () => (await fetch("/api/admin/auth/me")).status)).toBe(200);
});
