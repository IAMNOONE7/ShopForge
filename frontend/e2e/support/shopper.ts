import { expect, type Page, type TestInfo } from "@playwright/test";
import { readFile } from "node:fs/promises";
import en from "../../storefront/src/i18n/locales/en.ts";
import cs from "../../storefront/src/i18n/locales/cs.ts";
import type { RunProduct, RunStore, RunVariant } from "./isolatedEnvironment.ts";
import type { RunSession } from "./isolatedFixtures.ts";

export function labels(testInfo: TestInfo) { return (testInfo.project.name.startsWith("cs-") ? cs : en).resources; }
export const money = (amount: number) => new Intl.NumberFormat("cs-CZ", { style: "currency", currency: "CZK" }).format(amount);

export async function addVariant(page: Page, session: RunSession, store: RunStore, product: RunProduct, variant: RunVariant, testInfo: TestInfo) {
  const words = labels(testInfo);
  await page.goto(`${session.origin(store)}/p/${product.slug}`);
  await expect(page.locator("html")).toHaveAttribute("lang", testInfo.project.name.split("-")[0]);
  await expect(page.locator("main h1")).toHaveText(product.name);
  if (variant.size !== null) await page.getByRole("radio", { name: variant.size, exact: true }).check();
  const response = page.waitForResponse(answer => new URL(answer.url()).pathname === "/api/storefront/cart/items" && answer.request().method() === "POST");
  await page.getByRole("button", { name: words.catalog.addProductToCart.replace("{{name}}", product.name), exact: true }).click();
  const added = await response;
  expect(added.status()).toBe(200);
  expect(added.request().postDataJSON()).toEqual({ storeProductId: product.listings[store.id], variantId: variant.id, quantity: 1 });
  const cart = await added.json() as CartReading;
  expect(cart.items).toEqual([expect.objectContaining({ storeProductId: product.listings[store.id], variantId: variant.id, quantity: 1, unitPrice: product.price })]);
  await expect(page.locator(".add-to-cart-status")).toContainText(words.catalog.viewCart);
  await noOverflow(page);
}

export type CartReading = { count: number; itemsTotal: number; items: { storeProductId: string; variantId: string; quantity: number; unitPrice: number; optionValues: string[] }[] };
export async function cartReading(page: Page): Promise<CartReading> {
  return page.evaluate(async () => {
    const response = await fetch("/api/storefront/cart", { credentials: "same-origin" });
    if (!response.ok) throw new Error(`Cart read returned ${response.status}`);
    return response.json();
  });
}

export async function checkout(page: Page, session: RunSession, store: RunStore, product: RunProduct, testInfo: TestInfo) {
  const words = labels(testInfo), origin = session.origin(store), email = `buyer@${session.run.emailDomain}`;
  await page.goto(`${origin}/checkout`);
  const emailField = page.locator('input[name="email"]');
  if (await emailField.getAttribute("readonly") !== null) await expect(emailField).toHaveValue(email);
  else await emailField.fill(email);
  await page.locator('input[name="phone"]').fill("+420 777 123 456");
  for (const [field, value] of Object.entries({ fullName: "Eva Kupující", line1: "Testovací 1", city: "Brno", postalCode: "602 00", country: "CZ" })) {
    await page.locator(`input[name="billing.${field}"]`).fill(value);
  }
  await page.locator('input[name="shippingMethodCode"][value="personal-pickup"]').check();
  await page.locator('input[name="paymentMethodCode"][value="comgate"]').check();
  await expect(page.locator(".checkout-final-review")).toContainText(email);
  await expect(page.locator(".checkout-total strong")).toHaveText(money(product.price));
  await noOverflow(page);
  const response = page.waitForResponse(answer => new URL(answer.url()).pathname === "/api/storefront/checkout" && answer.request().method() === "POST");
  await page.getByRole("button", { name: words.checkout.placeOrder, exact: true }).click();
  const placed = await response;
  expect(placed.status()).toBe(201);
  expect(placed.request().headers()["idempotency-key"]).toBeTruthy();
  // Follow the browser redirect; its departure discards the checkout response body.
  await page.waitForURL(url => url.origin === session.environment.rigOrigin && /^\/comgate\/pay\/[^/]+$/.test(url.pathname));
  const redirect = new URL(page.url());
  const gateway = await session.gateway();
  const transaction = gateway.transactions.find(candidate => candidate.id === redirect.pathname.split("/").at(-1));
  expect(transaction).toMatchObject({ price: product.price * 100, currency: "CZK", test: true });
  expect(transaction!.referenceId).toMatch(/^\d{4}-\d+$/);
  await expect(page.getByRole("heading", { name: "Comgate stand-in", exact: true })).toBeVisible();
  return { number: transaction!.referenceId, transactionId: transaction!.id, email, origin };
}

export async function expectReceipt(page: Page, origin: string, number: string) {
  await expect(page).toHaveURL(url => url.origin === origin && url.pathname === `/order/${number}` && /^[0-9a-f-]{36}$/i.test(url.searchParams.get("token") ?? ""));
}

export async function noOverflow(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
}

export async function downloadOrderDocument(page: Page, number: string, testInfo: TestInfo) {
  const download = page.waitForEvent("download");
  const response = page.waitForResponse(answer => new URL(answer.url()).pathname.includes("/documents/") && answer.request().method() === "GET");
  await page.locator(".order-documents").getByRole("button", { name: new RegExp(number) }).click();
  const document = await download, answer = await response;
  expect(answer.status()).toBe(200); expect(answer.headers()["content-type"]).toContain("application/pdf");
  const path = testInfo.outputPath(`document-${number}.pdf`); await document.saveAs(path);
  expect((await readFile(path)).subarray(0, 5).toString()).toBe("%PDF-");
}
