import { test, expect } from "../support/isolatedFixtures.ts";
import { addVariant, cartReading, checkout, downloadInvoice, expectReceipt, labels, money, noOverflow } from "../support/shopper.ts";

test("a guest buys the selected variant, pays once and downloads the issued invoice", async ({ page, runSession }, testInfo) => {
  const store = runSession.run.stores[0], product = runSession.run.products.find(product => product.variants.some(variant => variant.size === "M"))!;
  const variant = product.variants.find(variant => variant.size === "M")!;
  const checkoutRequests: string[] = [];
  page.on("request", request => { if (new URL(request.url()).pathname === "/api/storefront/checkout" && request.method() === "POST") checkoutRequests.push(request.headers()["idempotency-key"]); });
  await addVariant(page, runSession, store, product, variant, testInfo);
  const placed = await checkout(page, runSession, store, product, testInfo);
  expect(await runSession.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock, reserved: 1, available: variant.stock - 1 });
  await page.getByRole("button", { name: "Pay", exact: true }).click();
  await expectReceipt(page, placed.origin, placed.number);
  await expect(page.locator(".order-status.paid")).toHaveText(labels(testInfo).orders.status.Paid);
  await expect.poll(async () => (await runSession.order(store, placed.number)).documents.some(document => document.kind === "Invoice")).toBe(true);
  const reading = await runSession.order(store, placed.number);
  expect(reading.order).toMatchObject({ number: placed.number, status: "Paid", email: placed.email, currency: "CZK", refundedTotal: 0 });
  expect(reading.order.paidAt).not.toBeNull();
  expect(reading.lines).toEqual([{ productName: `${product.name} (${variant.size})`, variantId: variant.id, quantity: 1, unitPrice: product.price, discount: 0 }]);
  expect(reading.attempts).toEqual([expect.objectContaining({ provider: "comgate", reference: placed.transactionId, status: "Paid", amount: product.price, currency: "CZK", environment: "test" })]);
  expect(reading.answersRecorded).toBe(1); expect(reading.kept).toEqual([]); expect(reading.returns).toEqual([]);
  await runSession.pushAgain(placed.transactionId);
  expect((await runSession.order(store, placed.number)).answersRecorded).toBe(1);
  expect(await runSession.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock - 1, reserved: 0, available: variant.stock - 1 });
  const invoice = reading.documents.find(document => document.kind === "Invoice")!;
  await page.reload(); await expect(page.locator(".order-grand-total dd")).toHaveText(money(product.price));
  await downloadInvoice(page, invoice.number, testInfo); await noOverflow(page);
  await expect.poll(async () => (await runSession.mail(placed.email)).some(letter => letter.subject === `${store.name}: Payment received for order ${placed.number}` && letter.attachments.some(name => name.endsWith(".pdf")))).toBe(true);
  const letters = await runSession.mail(placed.email);
  expect(letters.some(letter => letter.subject === `${store.name}: Your order ${placed.number}`)).toBe(true);
  expect(checkoutRequests).toHaveLength(1);
  await testInfo.attach("paid-receipt", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
  await testInfo.attach("order-facts", { body: JSON.stringify({ run: runSession.run.id, store: store.id, ...reading, stock: await runSession.stock(variant.sku), letters: letters.map(({ id, subject, attachments }) => ({ id, subject, attachments })) }), contentType: "application/json" });
});

for (const outcome of ["decline", "pending"] as const) {
  test(`a ${outcome} gateway answer preserves the correct order, attempt and stock facts`, async ({ page, runSession }, testInfo) => {
    const store = runSession.run.stores[0], product = runSession.run.products.find(product => product.variants.some(variant => variant.size === "S"))!;
    const variant = product.variants.find(variant => variant.size === "S")!;
    await addVariant(page, runSession, store, product, variant, testInfo);
    const placed = await checkout(page, runSession, store, product, testInfo);
    await page.getByRole("button", { name: outcome === "decline" ? "Decline" : "Leave pending", exact: true }).click();
    if (outcome === "decline") {
      await expect(page).toHaveURL(`${placed.origin}/cart`);
      await page.locator(".cart-order-recovery").getByRole("link", { name: labels(testInfo).cart.orderRecoveryAction, exact: true }).click();
    }
    await expectReceipt(page, placed.origin, placed.number);
    const status = outcome === "decline" ? "Cancelled" : "AwaitingPayment";
    await expect(page.locator(`.order-status.${status.toLowerCase()}`)).toHaveText(labels(testInfo).orders.status[status]);
    const reading = await runSession.order(store, placed.number);
    expect(reading.order.status).toBe(status); expect(reading.order.paidAt).toBeNull();
    expect(reading.attempts).toEqual([expect.objectContaining({ status: outcome === "decline" ? "Failed" : "Pending", amount: product.price, currency: "CZK" })]);
    expect(reading.kept).toEqual([]); expect(reading.documents).toEqual([]);
    expect(await runSession.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock, reserved: outcome === "decline" ? 0 : 1, available: variant.stock - (outcome === "decline" ? 0 : 1) });
    await expect(page.locator(".order-grand-total dd")).toHaveText(money(product.price)); await noOverflow(page);
    await testInfo.attach("order-facts", { body: JSON.stringify({ run: runSession.run.id, ...reading, stock: await runSession.stock(variant.sku) }), contentType: "application/json" });
  });
}

test("one browser keeps independent carts on the two run hosts", async ({ page, context, runSession }, testInfo) => {
  const [first, second] = runSession.run.stores, other = await context.newPage();
  const product = runSession.run.products.find(product => product.variants.some(variant => variant.size === "M"))!;
  const medium = product.variants.find(variant => variant.size === "M")!, large = product.variants.find(variant => variant.size === "L")!;
  await addVariant(page, runSession, first, product, medium, testInfo);
  await addVariant(other, runSession, second, product, large, testInfo);
  for (const [current, store, variant] of [[page, first, medium], [other, second, large]] as const) {
    await current.goto(`${runSession.origin(store)}/cart`);
    expect((await cartReading(current)).items).toEqual([expect.objectContaining({ storeProductId: product.listings[store.id], variantId: variant.id, quantity: 1, optionValues: [variant.size] })]);
    await expect(current.locator(".cart-line-options")).toHaveText(variant.size!); await noOverflow(current);
  }
  const removed = page.waitForResponse(response => response.request().method() === "DELETE" && new URL(response.url()).pathname.startsWith("/api/storefront/cart/items/"));
  await page.locator(".cart-line").getByRole("button", { name: labels(testInfo).cart.removeItem.replace("{{name}}", `${product.name} (${medium.size})`), exact: true }).click();
  expect((await removed).status()).toBe(200);
  await page.reload(); expect((await cartReading(page)).items).toEqual([]);
  await other.reload(); expect((await cartReading(other)).items).toEqual([expect.objectContaining({ storeProductId: product.listings[second.id], variantId: large.id, quantity: 1 })]);
  for (const variant of [medium, large]) expect(await runSession.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock, reserved: 0, available: variant.stock });
});
