import { expect, type Page, type TestInfo } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { verificationUrl, type RunStore } from "./isolatedEnvironment.ts";
import type { RunSession } from "./isolatedFixtures.ts";
import { labels, noOverflow } from "./shopper.ts";

export type Shopper = { email: string; password: string };

export async function registerShopper(page: Page, session: RunSession, store: RunStore, testInfo: TestInfo): Promise<Shopper> {
  const words = labels(testInfo), origin = session.origin(store);
  const shopper = { email: `buyer@${session.run.emailDomain}`, password: `Customer-${randomUUID()}!1` };
  await page.goto(`${origin}/account/register`);
  await expect(page.locator("html")).toHaveAttribute("lang", testInfo.project.name.split("-")[0]);
  for (const [name, value] of Object.entries({ firstName: "Eva", lastName: "Kupující", email: shopper.email, password: shopper.password, phone: "+420 777 123 456" })) {
    await page.locator(`input[name="${name}"]`).fill(value);
  }
  await noOverflow(page);
  const registered = page.waitForResponse(response => new URL(response.url()).pathname === "/api/storefront/account/register" && response.request().method() === "POST");
  await page.getByRole("button", { name: words.auth.createAccountAction, exact: true }).click();
  expect((await registered).status()).toBe(202);
  await expect(page.getByRole("heading", { name: words.auth.checkEmail, exact: true })).toBeVisible();
  const subject = `Confirm your e-mail address for ${store.name}`;
  await expect.poll(async () => (await session.mail(shopper.email)).filter(letter => letter.subject === subject).length).toBe(1);
  const letter = (await session.mail(shopper.email)).find(letter => letter.subject === subject)!;
  await page.goto(verificationUrl(letter.links, store, origin));
  const verified = page.waitForResponse(response => new URL(response.url()).pathname === "/api/storefront/account/verify" && response.request().method() === "POST");
  await page.getByRole("button", { name: words.auth.confirmEmailAction, exact: true }).click();
  expect((await verified).status()).toBe(200);
  await expect(page).toHaveURL(`${origin}/account`);
  await expect(page.locator(".account-header")).toContainText(shopper.email);
  await noOverflow(page);
  return shopper;
}

export async function signOutShopper(page: Page, origin: string, testInfo: TestInfo) {
  await page.goto(`${origin}/account`);
  const left = page.waitForResponse(response => new URL(response.url()).pathname === "/api/storefront/account/logout" && response.request().method() === "POST");
  await page.getByRole("button", { name: labels(testInfo).navigation.signOut, exact: true }).click();
  expect((await left).ok()).toBe(true);
  await expect(page).toHaveURL(`${origin}/`);
}

export async function signInShopper(page: Page, shopper: Shopper, testInfo: TestInfo) {
  await page.locator('input[name="email"]').fill(shopper.email);
  await page.locator('input[name="password"]').fill(shopper.password);
  const signedIn = page.waitForResponse(response => new URL(response.url()).pathname === "/api/storefront/account/login" && response.request().method() === "POST");
  await page.getByRole("button", { name: labels(testInfo).auth.signIn, exact: true }).click();
  expect((await signedIn).status()).toBe(200);
}
