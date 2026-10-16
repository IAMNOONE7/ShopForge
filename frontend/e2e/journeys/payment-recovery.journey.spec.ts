import { test, expect, type RunSession } from "../support/isolatedFixtures.ts";
import { addVariant, checkout, downloadOrderDocument, expectReceipt, labels, money, noOverflow } from "../support/shopper.ts";
import type { Page, TestInfo } from "@playwright/test";
import type { RunProduct, RunStore, RunVariant } from "../support/isolatedEnvironment.ts";

async function expectWaiting(page: Page, session: RunSession, store: RunStore, product: RunProduct, variant: RunVariant, number: string, attemptStatus: string, events: number, testInfo: TestInfo) {
  const refreshed = page.waitForResponse(response => new URL(response.url()).pathname === `/api/storefront/orders/${number}` && response.request().method() === "GET");
  await page.getByRole("button", { name: labels(testInfo).orders.refresh, exact: true }).click();
  const response = await refreshed;
  expect(response.status()).toBe(200);
  expect(await response.json()).toMatchObject({ number, status: "AwaitingPayment", grandTotal: product.price, documents: [] });
  await expect(page.locator(".order-status.awaitingpayment")).toHaveText(labels(testInfo).orders.status.AwaitingPayment);
  await expect(page.locator(".order-grand-total dd")).toHaveText(money(product.price));
  await expect(page.locator(".order-documents button")).toHaveCount(0);
  const reading = await session.order(store, number);
  expect(reading.order).toMatchObject({ status: "AwaitingPayment", paidAt: null, refundedTotal: 0 });
  expect(reading.attempts).toEqual([expect.objectContaining({ status: attemptStatus, amount: product.price, currency: "CZK", environment: events === 0 ? null : "test" })]);
  expect(reading.answersRecorded).toBe(events); expect(reading.documents).toEqual([]); expect(reading.kept).toEqual([]); expect(reading.returns).toEqual([]);
  expect(await session.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock, reserved: 1, available: variant.stock - 1 });
  expect((await session.mail(`buyer@${session.run.emailDomain}`)).some(letter => letter.subject === `${store.name}: Payment received for order ${number}`)).toBe(false);
  await noOverflow(page);
  return reading;
}

for (const scenario of ["lost notification", "failed status lookup", "unrecognised status"] as const) {
  test(`payment recovery after ${scenario} keeps one checkout and applies payment once`, async ({ page, runSession }, testInfo) => {
    const store = runSession.run.stores[0], words = labels(testInfo);
    const size = scenario === "lost notification" ? "S" : scenario === "failed status lookup" ? "L" : "M";
    const product = runSession.run.products.find(product => product.variants.some(variant => variant.size === size))!;
    const variant = product.variants.find(variant => variant.size === size)!;
    const checkoutKeys: string[] = [], posts: string[] = [];
    page.on("request", request => {
      if (request.method() !== "POST") return;
      const path = new URL(request.url()).pathname; posts.push(path);
      if (path === "/api/storefront/checkout") checkoutKeys.push(request.headers()["idempotency-key"]);
    });
    await runSession.configureGateway({ push: scenario === "failed status lookup" ? "deliver" : "drop", statusFails: scenario === "failed status lookup" });
    await addVariant(page, runSession, store, product, variant, testInfo);
    const placed = await checkout(page, runSession, store, product, testInfo);
    await expect(runSession.pushAgain("foreign-transaction")).rejects.toThrow("Foreign gateway transaction");
    await page.getByRole("button", { name: scenario === "unrecognised status" ? "Leave pending" : "Pay", exact: true }).click();
    await expectReceipt(page, placed.origin, placed.number);
    const waiting = await expectWaiting(page, runSession, store, product, variant, placed.number, "Started", 0, testInfo);
    const intermediate = [waiting];
    const initialGateway = await runSession.gateway();
    expect(initialGateway.transactions).toHaveLength(1);
    const initial = initialGateway.transactions[0];
    expect(initial).toMatchObject({ id: placed.transactionId, referenceId: placed.number, status: scenario === "unrecognised status" ? "PENDING" : "PAID", price: product.price * 100, currency: "CZK", test: true, refunded: 0 });
    expect(initial.pushes).toHaveLength(1);
    if (scenario === "failed status lookup") {
      expect(initial.pushes[0].what).toBe("sent");
      expect(initial.pushes[0].answered).toBeGreaterThanOrEqual(500);
      expect(initial.pushes[0].answered).toBeLessThan(600);
    } else expect(initial.pushes[0]).toMatchObject({ what: "dropped", answered: null });
    await testInfo.attach("payment-waiting", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });

    if (scenario === "unrecognised status") {
      await runSession.configureGateway({ push: "deliver", statusFails: false });
      await runSession.setGatewayStatus(placed.transactionId, "REVERSAL_PENDING");
      intermediate.push(await expectWaiting(page, runSession, store, product, variant, placed.number, "Unrecognised", 1, testInfo));
      await runSession.pushAgain(placed.transactionId);
      intermediate.push(await expectWaiting(page, runSession, store, product, variant, placed.number, "Unrecognised", 1, testInfo));
    }
    const beforeRecoveryPosts = [...posts];
    await runSession.configureGateway({ push: "twice", statusFails: false });
    if (scenario === "unrecognised status") await runSession.setGatewayStatus(placed.transactionId, "PAID");
    else await runSession.pushAgain(placed.transactionId);
    // The receipt's real GET polling must discover payment without another checkout.
    await expect(page.locator(".order-status.paid")).toHaveText(words.orders.status.Paid);
    await expect.poll(async () => (await runSession.order(store, placed.number)).documents.filter(document => document.kind === "Invoice").length).toBe(1);
    const paid = await runSession.order(store, placed.number);
    expect(paid.order).toMatchObject({ status: "Paid", email: placed.email, currency: "CZK", paymentReference: placed.transactionId, refundedTotal: 0 });
    expect(paid.order.paidAt).not.toBeNull();
    expect(paid.lines).toEqual([{ productName: `${product.name} (${variant.size})`, variantId: variant.id, quantity: 1, unitPrice: product.price, discount: 0 }]);
    expect(paid.attempts).toEqual([expect.objectContaining({ provider: "comgate", reference: placed.transactionId, status: "Paid", amount: product.price, currency: "CZK", environment: "test" })]);
    expect(paid.answersRecorded).toBe(scenario === "unrecognised status" ? 2 : 1);
    expect(paid.kept).toEqual([]); expect(paid.returns).toEqual([]);
    expect(paid.documents).toHaveLength(1);
    expect(await runSession.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock - 1, reserved: 0, available: variant.stock - 1 });
    const recoveredGateway = await runSession.gateway();
    expect(recoveredGateway.transactions).toHaveLength(1);
    const recovered = recoveredGateway.transactions[0];
    expect(recovered).toMatchObject({ id: placed.transactionId, status: "PAID", refunded: 0, test: true });
    expect(recovered.pushes.slice(-2)).toEqual([expect.objectContaining({ what: "sent", answered: 200 }), expect.objectContaining({ what: "sent", answered: 200 })]);
    await expect(page.locator(".order-grand-total dd")).toHaveText(money(product.price));
    await downloadOrderDocument(page, paid.documents[0].number, testInfo);
    await expect.poll(async () => (await runSession.mail(placed.email)).filter(letter => letter.subject === `${store.name}: Payment received for order ${placed.number}` && letter.attachments.some(name => name.endsWith(".pdf"))).length).toBe(1);
    expect(posts).toEqual(beforeRecoveryPosts);
    expect(checkoutKeys).toHaveLength(1); expect(checkoutKeys[0]).toBeTruthy();
    await noOverflow(page);
    await testInfo.attach("payment-recovered", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
    await testInfo.attach("recovery-facts", { body: JSON.stringify({ scenario, run: runSession.run.id, store: store.id, intermediate, initialGateway, paid, gateway: recoveredGateway, stock: await runSession.stock(variant.sku) }), contentType: "application/json" });
  });
}
