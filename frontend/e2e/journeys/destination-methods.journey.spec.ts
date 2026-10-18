import { test, expect } from "../support/isolatedFixtures.ts";
import { addVariant, expectReceipt, fillCheckout, labels, money, noOverflow, submitCheckout } from "../support/shopper.ts";

test("checkout follows the address country and explicitly reselects a removed delivery method", async ({ page, runSession }, testInfo) => {
  const words = labels(testInfo), store = runSession.run.stores[0];
  const product = runSession.run.products.find(product => product.variants.some(variant => variant.size === "M"))!;
  const variant = product.variants.find(variant => variant.size === "M")!;
  await runSession.limitShippingToCzechia(store);
  const writes: { key: string; body: Record<string, unknown> }[] = [];
  page.on("request", request => {
    if (new URL(request.url()).pathname === "/api/storefront/checkout" && request.method() === "POST")
      writes.push({ key: request.headers()["idempotency-key"], body: request.postDataJSON() as Record<string, unknown> });
  });
  await addVariant(page, runSession, store, product, variant, testInfo);
  await fillCheckout(page, runSession, store, product);
  const billingCountry = page.locator('[name="billing.country"]'), deliveryCountry = page.locator('[name="shipping.country"]');
  const sameCountryAnswer = (country: string) => page.waitForResponse(answer => {
    const url = new URL(answer.url());
    return url.pathname === "/api/storefront/checkout/methods" && url.searchParams.get("country") === country;
  });
  const read = sameCountryAnswer("SK");
  await billingCountry.fill(" sk ");
  const answer = await read;
  expect(answer.status()).toBe(200);
  expect(await answer.json()).toMatchObject({ destinationCountry: "SK", shippingMethods: [] });
  await expect(page.getByText(words.checkout.noShippingMethods, { exact: true })).toBeVisible();
  await expect(page.locator('[name="shippingMethodCode"]')).toHaveCount(0);
  await expect(page.locator(".checkout-total")).toHaveCount(0);
  await expect(page.locator('[name="billing.line1"]')).toHaveValue("Testovací 1");
  await expect(page.locator('[name="email"]')).toHaveValue(`buyer@${runSession.run.emailDomain}`);
  await page.locator("form.checkout").evaluate((form: HTMLFormElement) => form.requestSubmit());
  expect(writes).toHaveLength(0);
  await noOverflow(page);
  await testInfo.attach("destination-unavailable", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });

  const enlargedText = await page.addStyleTag({ content: "html { font-size: 200% !important; }" });
  await noOverflow(page);
  expect(await page.locator(".checkout-method-status > div").evaluateAll(panels => panels.every(panel => {
    const bounds = panel.getBoundingClientRect();
    return [...panel.querySelectorAll("*")].every(child => {
      const box = child.getBoundingClientRect();
      return box.width === 0 || (box.left >= bounds.left - 1 && box.right <= bounds.right + 1);
    });
  }))).toBe(true);
  await testInfo.attach("destination-unavailable-large-text", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
  await enlargedText.evaluate(element => { element.parentNode?.removeChild(element); });

  await page.getByRole("checkbox", { name: words.checkout.shipElsewhere, exact: true }).check();
  for (const [field, value] of Object.entries({ fullName: "Eva Příjemce", line1: "Doručovací 2", city: "Praha", postalCode: "110 00" }))
    await page.locator(`[name="shipping.${field}"]`).fill(value);
  await deliveryCountry.fill("C");
  await expect(page.getByText(words.checkout.destinationCountryNeeded, { exact: true })).toBeVisible();
  await page.locator("form.checkout").evaluate((form: HTMLFormElement) => form.requestSubmit());
  expect(writes).toHaveLength(0);
  const available = sameCountryAnswer("CZ");
  await deliveryCountry.fill("cz");
  const returned = await available;
  expect(returned.status()).toBe(200);
  expect(await returned.json()).toMatchObject({ destinationCountry: "CZ", shippingMethods: expect.arrayContaining([expect.objectContaining({ code: "personal-pickup" })]) });
  const pickup = page.locator('[name="shippingMethodCode"][value="personal-pickup"]');
  await expect(pickup).not.toBeChecked();
  await expect(page.getByText(words.checkout.shippingSelectionChanged, { exact: true })).toBeVisible();
  await pickup.focus(); await page.keyboard.press("Space");
  await expect(pickup).toBeChecked();
  await page.locator('[name="paymentMethodCode"][value="comgate"]').check();
  await expect(page.locator(".checkout-total strong")).toHaveText(money(product.price));

  const methodReads: string[] = [];
  page.on("request", request => { if (new URL(request.url()).pathname === "/api/storefront/checkout/methods") methodReads.push(request.url()); });
  await billingCountry.fill("DE");
  await expect(page.locator(".checkout-total strong")).toHaveText(money(product.price));
  expect(methodReads).toEqual([]);
  const unavailableAgain = sameCountryAnswer("DE");
  await page.getByRole("checkbox", { name: words.checkout.shipElsewhere, exact: true }).uncheck();
  expect((await unavailableAgain).status()).toBe(200);
  await expect(page.getByText(words.checkout.noShippingMethods, { exact: true })).toBeVisible();
  const restored = sameCountryAnswer("CZ");
  await page.getByRole("checkbox", { name: words.checkout.shipElsewhere, exact: true }).check();
  expect((await restored).status()).toBe(200);
  await expect(deliveryCountry).toHaveValue("cz");
  await expect(pickup).not.toBeChecked();
  await pickup.check();
  await expect(page.locator(".checkout-final-review")).toContainText("Doručovací 2");
  await expect(page.locator(".checkout-final-review")).not.toContainText("Testovací 1");
  await noOverflow(page);
  await testInfo.attach("destination-ready", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
  const placed = await submitCheckout(page, runSession, store, product, testInfo);
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatchObject({ key: expect.stringMatching(/^sf-[a-f0-9]{32}$/), body: {
    billingAddress: expect.objectContaining({ country: "DE" }), shippingAddress: expect.objectContaining({ country: "CZ" }),
    shippingMethodCode: "personal-pickup", paymentMethodCode: "comgate", pickupPointCode: null,
  } });
  await page.getByRole("button", { name: "Pay", exact: true }).click();
  await expectReceipt(page, placed.origin, placed.number);
  await expect(page.locator(".order-status.paid")).toHaveText(words.orders.status.Paid);
  const reading = await runSession.order(store, placed.number);
  expect(reading.order.status).toBe("Paid"); expect(reading.attempts).toHaveLength(1); expect(reading.answersRecorded).toBe(1);
  expect(await runSession.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock - 1, reserved: 0, available: variant.stock - 1 });
});
