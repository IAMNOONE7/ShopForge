import type { APIRequestContext, Page } from "@playwright/test";
import { writeFileSync } from "node:fs";
import { test, expect, operatorRead, type OperatorSession } from "../support/operatorFixtures.ts";
import type { IsolatedRun } from "../support/isolatedEnvironment.ts";
import type { RunSession } from "../support/isolatedFixtures.ts";
import { registerShopper } from "../support/registeredShopper.ts";
import { labels, noOverflow } from "../support/shopper.ts";

type Tenant = { id: string; name: string; status: string; planCode: string | null; usage: { name: string; value: number }[] };
type User = { email: string; tenantId?: string; role?: string };
const text = (value: string, name: string) => value.replace("{{name}}", name);
async function tenants(page: Page) {
  const result = await operatorRead<Tenant[]>(page, "/api/platform/tenants"); expect(result.status).toBe(200);
  expect(Array.isArray(result.body)).toBe(true); return result.body!;
}
async function companyCard(page: Page, operator: OperatorSession, run: IsolatedRun) {
  await page.getByRole("textbox", { name: operator.words.operator.search, exact: true }).fill(run.tenantId);
  await page.getByRole("button", { name: operator.words.operator.searchAction, exact: true }).click();
  const card = page.locator(".operator-tenants > li").filter({ has: page.getByText(run.tenantId, { exact: true }) });
  await expect(card).toHaveCount(1); return card;
}
async function change(page: Page, operator: OperatorSession, run: IsolatedRun, action: "suspend" | "resume") {
  const row = (await tenants(page)).find(item => item.id === run.tenantId)!;
  expect(row.status).toBe(action === "suspend" ? "Active" : "Suspended");
  const words = operator.words.operator, card = await companyCard(page, operator, run);
  await card.getByRole("button", { name: text(action === "suspend" ? words.suspendNamed : words.resumeNamed, row.name), exact: true }).click();
  await expect(page.locator("#operator-confirm-title")).toBeFocused();
  await expect(page.locator(".operator-confirmation")).toContainText(row.name); await expect(page.locator(".operator-confirmation")).toContainText(run.tenantId);
  await noOverflow(page);
  const path = `/api/platform/tenants/${run.tenantId}/${action}`;
  const response = page.waitForResponse(answer => new URL(answer.url()).pathname === path && answer.request().method() === "POST");
  await page.getByRole("button", { name: action === "suspend" ? words.confirmSuspend : words.confirmResume, exact: true }).click();
  const answer = await response; expect(answer.status()).toBe(200); expect(answer.request().postData()).toBe(null);
  const saved = await answer.json() as Tenant; expect(saved).toMatchObject({ id: run.tenantId, status: action === "suspend" ? "Suspended" : "Active", usage: [] });
  await expect(page.getByText(text(words.acknowledged, row.name), { exact: true })).toBeVisible();
  await expect(card.getByRole("button", { name: text(action === "suspend" ? words.resumeNamed : words.suspendNamed, row.name), exact: true })).toBeEnabled();
  const read = (await tenants(page)).find(item => item.id === run.tenantId)!; expect(read.status).toBe(saved.status); expect(read.usage).toEqual(row.usage);
  return read;
}
async function serving(request: APIRequestContext, session: RunSession, run: IsolatedRun, status: number) {
  const results = [];
  for (const store of run.stores) {
    const answer = await request.get(`${session.environment.frontendOrigin}/api/storefront/store`, {
      headers: { Host: `${store.host}:${session.environment.storefrontPort}` }, maxRedirects: 0, maxRetries: 0, timeout: 5_000,
    });
    expect(answer.status(), `Next request for ${store.host}`).toBe(status);
    if (status === 200) expect(await answer.json()).toMatchObject({ id: store.id, name: store.name });
    results.push({ storeId: store.id, status: answer.status() });
  }
  return results;
}
async function session(page: Page, path: "/api/admin/auth/me" | "/api/platform/auth/me", status: number, identity?: Partial<User>) {
  const result = await operatorRead<User>(page, path); expect(result.status).toBe(status); if (identity) expect(result.body).toMatchObject(identity);
}
async function signOut(page: Page, operator: OperatorSession, platform: boolean) {
  const path = `/api/${platform ? "platform" : "admin"}/auth/logout`;
  const response = page.waitForResponse(answer => new URL(answer.url()).pathname === path && answer.request().method() === "POST");
  await page.getByRole("button", { name: operator.words.navigation.signOut, exact: true }).click(); expect((await response).ok()).toBe(true);
  await expect(page.locator('input[name="password"]')).toBeVisible();
}

test("operator suspends and restores its run tenant across both warm hosts without changing another company", async ({ page, context, request, runSession: run, controlRun, operator }, testInfo) => {
  const ownerPage = await context.newPage(), customerPage = await context.newPage();
  await operator.signInOwner(ownerPage);
  const customer = { email: `buyer@${run.run.emailDomain}` };
  try { await registerShopper(customerPage, run, run.run.stores[0], testInfo); }
  catch { await customerPage.close().catch(() => undefined); throw new Error("Run customer registration and verification did not complete."); }
  const profile = await operatorRead<User>(customerPage, "/api/storefront/account/me"); expect(profile.status).toBe(200); expect(profile.body).toMatchObject({ email: customer.email });
  await operator.signIn(page);
  const before = await tenants(page), owned = before.find(item => item.id === run.run.tenantId)!;
  expect(owned).toMatchObject({ name: `Run ${run.run.id}`, status: "Active" }); expect(before.find(item => item.id === controlRun.tenantId)?.status).toBe("Active");
  await serving(request, run, run.run, 200); await serving(request, run, controlRun, 200);
  const card = await companyCard(page, operator, run.run), words = operator.words.operator;
  await card.getByRole("button", { name: text(words.suspendNamed, owned.name), exact: true }).click();
  await expect(page.locator("#operator-confirm-title")).toBeFocused(); await page.locator("#operator-confirm-title").press("Escape");
  await expect(page.locator(".operator-confirmation")).toHaveCount(0); await expect(card.getByRole("button")).toBeFocused();
  expect((await tenants(page)).find(item => item.id === run.run.tenantId)?.status).toBe("Active");
  operator.expectSuspendedReads = true;
  await change(page, operator, run.run, "suspend");
  const denied = await serving(request, run, run.run, 404); await serving(request, run, controlRun, 200);
  // Exercise the already-open merchant shell's protected read, not just a new sign-in page.
  const menu = ownerPage.getByRole("button", { name: operator.words.navigation.menu, exact: true }); if (await menu.isVisible()) await menu.click();
  await ownerPage.getByRole("navigation", { name: operator.words.navigation.administration, exact: true }).getByRole("link", { name: operator.words.navigation.products, exact: true }).click();
  await expect(ownerPage.getByText(operator.words.errors.sessionExpired, { exact: true })).toBeVisible();
  await expect(ownerPage.locator(".admin-main")).toHaveCount(0); await session(ownerPage, "/api/admin/auth/me", 401);
  expect((await operatorRead(customerPage, "/api/storefront/account/me")).status).toBe(404);
  await customerPage.reload(); await expect(customerPage.getByRole("heading", { name: labels(testInfo).errors.storeNotFoundTitle, exact: true })).toBeVisible(); await expect(customerPage.locator(".account-header")).toHaveCount(0);
  const second = await context.newPage(); await second.goto(`${run.origin(run.run.stores[1])}/`);
  await expect(second.getByRole("heading", { name: labels(testInfo).errors.storeNotFoundTitle, exact: true })).toBeVisible();
  await session(page, "/api/platform/auth/me", 200, { email: "operator@e2e.test" });
  await page.screenshot({ path: testInfo.outputPath("suspended-company.png"), caret: "initial" });
  const suspended = await tenants(page);
  expect(suspended.filter(item => item.id !== run.run.tenantId).map(item => [item.id, item.status])).toEqual(before.filter(item => item.id !== run.run.tenantId).map(item => [item.id, item.status]));
  await page.reload(); await expect(page.getByRole("heading", { name: words.companies, exact: true })).toBeVisible();
  await change(page, operator, run.run, "resume");
  const restored = await serving(request, run, run.run, 200); await serving(request, run, controlRun, 200);
  operator.expectSuspendedReads = false;
  expect(await tenants(page)).toEqual(before);
  await session(ownerPage, "/api/admin/auth/me", 401); await operator.signInOwner(ownerPage); await session(ownerPage, "/api/admin/auth/me", 200, { tenantId: run.run.tenantId, role: "Owner" });
  await customerPage.reload(); await expect(customerPage.locator(".account-header")).toContainText(customer.email); expect(await operatorRead(customerPage, "/api/storefront/account/me")).toEqual(profile);
  await second.reload(); await expect(second.getByRole("banner").getByRole("link", { name: run.run.stores[1].name, exact: true })).toBeVisible();
  for (const variant of run.run.products.flatMap(product => product.variants)) expect(await run.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock, reserved: 0, available: variant.stock });
  await noOverflow(page); await noOverflow(ownerPage); await noOverflow(customerPage); await noOverflow(second);
  writeFileSync(testInfo.outputPath("operator-facts.json"), JSON.stringify({ tenantId: run.run.tenantId, controlTenantId: controlRun.tenantId, denied, restored, customerDenied: 404, merchantDenied: 401, states: ["Active", "Suspended", "Active"], otherTenantsUnchanged: true, stockUnchanged: true }, null, 2));
});

test("merchant and platform sign-in, unauthorized reads and sign-out preserve the other session", async ({ page, context, runSession: run, operator }, testInfo) => {
  const owner = { email: `owner@${run.run.emailDomain}`, tenantId: run.run.tenantId, role: "Owner" };
  await operator.signInOwner(page); await session(page, "/api/admin/auth/me", 200, owner);
  await page.goto(`${operator.origin}/admin/platform/tenants`); await expect(page.getByRole("heading", { name: operator.words.operator.signInTitle, exact: true })).toBeVisible();
  await session(page, "/api/platform/auth/me", 401); await session(page, "/api/admin/auth/me", 200, owner);
  await operator.signIn(page); await session(page, "/api/platform/auth/me", 200, { email: "operator@e2e.test" });
  expect((await context.cookies(operator.origin)).map(cookie => cookie.name).filter(name => ["shopforge_admin", "shopforge_platform"].includes(name)).sort()).toEqual(["shopforge_admin", "shopforge_platform"]);
  await signOut(page, operator, true); await session(page, "/api/platform/auth/me", 401); await session(page, "/api/admin/auth/me", 200, owner);
  await operator.signIn(page);
  await page.goto(`${operator.origin}/admin/stores/${run.run.stores[0].id}/settings`); await expect(page.locator(".publication-panel")).toBeVisible(); await noOverflow(page);
  await signOut(page, operator, false); await session(page, "/api/admin/auth/me", 401); await session(page, "/api/platform/auth/me", 200, { email: "operator@e2e.test" });
  await page.goto(`${operator.origin}/admin/products`); await expect(page.locator('input[name="password"]')).toBeVisible(); await session(page, "/api/platform/auth/me", 200);
  await page.goto(`${operator.origin}/admin/platform/tenants`); const card = await companyCard(page, operator, run.run); await expect(card).toContainText(operator.words.operator.status.Active);
  await noOverflow(page); await page.screenshot({ path: testInfo.outputPath(`separate-sessions-${testInfo.project.name}.png`), caret: "initial" });
});
