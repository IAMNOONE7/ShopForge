import { test, expect } from "../support/fixtures.ts";

test("boots two independent store hosts with the selected language and responsive shopping controls", async ({ page, context, environment }, testInfo) => {
  const language = testInfo.project.name.split("-")[0];
  const labels = language === "cs"
    ? { search: "Hledat produkty", cart: "Košík", account: "Přihlásit se", menu: "Nabídka" }
    : { search: "Search products", cart: "Cart", account: "Sign in", menu: "Menu" };
  const pages = [page, await context.newPage()];
  for (const [index, target] of environment.stores.entries()) {
    const current = pages[index];
    const responsePromise = current.waitForResponse((response) => new URL(response.url()).pathname === "/api/storefront/store");
    await current.goto(target.origin);
    const response = await responsePromise;
    expect(response.status()).toBe(200);
    const store = await response.json() as { id: string; name: string };
    expect(store.id).toBe(target.id);
    await expect(current.locator("html")).toHaveAttribute("lang", language);
    await expect(current.getByRole("heading", { level: 1 })).toHaveText(store.name);
    await expect(current.getByRole("searchbox", { name: labels.search })).toBeVisible();
    await expect(current.locator(".store-header").getByRole("link", { name: labels.cart, exact: true })).toBeVisible();
    await expect(current.locator(".store-header").getByRole("link", { name: labels.account, exact: true })).toBeVisible();
    await expect(current.locator(".product-card").first()).toBeVisible();
    if (testInfo.project.name.endsWith("phone")) {
      const menu = current.locator(".menu-trigger");
      await expect(menu).toHaveAttribute("aria-label", labels.menu);
      await menu.click();
      await expect(menu).toHaveAttribute("aria-expanded", "true");
      await current.keyboard.press("Escape");
      await expect(menu).toHaveAttribute("aria-expanded", "false");
      await expect(menu).toBeFocused();
    }
    const productPath = await current.locator(".product-card").first().getAttribute("href");
    const productName = await current.locator(".product-card").first().locator(".product-name").innerText();
    expect(productPath).toMatch(/^\/p\/[^/]+$/);
    await current.locator(".product-card").first().click();
    await expect(current.locator("main h1")).toHaveText(productName);
    await expect(current).toHaveURL(target.origin + productPath);
    await current.reload();
    await expect(current.locator("main h1")).toHaveText(productName);
    expect(new URL(current.url()).origin).toBe(target.origin);
    await expect.poll(() => current.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    await testInfo.attach(`store-${index + 1}`, { body: JSON.stringify({ origin: target.origin, id: store.id, name: store.name, productPath, productName }), contentType: "application/json" });
  }
  expect(environment.stores[0].id).not.toBe(environment.stores[1].id);
});
