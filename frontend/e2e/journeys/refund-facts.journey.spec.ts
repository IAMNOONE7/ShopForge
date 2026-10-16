import { test, expect } from "../support/isolatedFixtures.ts";
import { addVariant, checkout, downloadOrderDocument, expectPublicRefund, expectReceipt, labels, money, noOverflow } from "../support/shopper.ts";

for (const result of ["accept", "refuse"] as const) {
  test(`a guest sees a store-initiated whole-order refund after the provider ${result}s`, async ({ page, runSession }, testInfo) => {
    const store = runSession.run.stores[0], words = labels(testInfo).orders;
    const product = runSession.run.products.find(product => product.variants.some(variant => variant.size === "M"))!;
    const variant = product.variants.find(variant => variant.size === "M")!;
    const posts: string[] = [];
    page.on("request", request => { if (request.method() === "POST") posts.push(new URL(request.url()).pathname); });
    await runSession.configureGateway({ push: "deliver", statusFails: false, refund: result });
    await addVariant(page, runSession, store, product, variant, testInfo);
    const placed = await checkout(page, runSession, store, product, testInfo);
    await page.getByRole("button", { name: "Pay", exact: true }).click();
    await expectReceipt(page, placed.origin, placed.number);
    await expect(page.locator(".order-status.paid")).toHaveText(words.status.Paid);
    await expect.poll(async () => (await runSession.order(store, placed.number)).documents.some(document => document.kind === "Invoice")).toBe(true);
    const originalPosts = [...posts];

    await runSession.refundOrder(store, placed.number);
    await expect.poll(async () => (await runSession.order(store, placed.number)).documents.some(document => document.kind === "CreditNote")).toBe(true);
    await page.getByRole("button", { name: words.refresh, exact: true }).click();
    await expect(page.locator(".order-status.refunded")).toHaveText(words.status.Refunded);
    const first = await expectPublicRefund(page, placed.number, result === "accept" ? "Settled" : "InProgress", product.price, testInfo);
    const returned = (await runSession.order(store, placed.number)).returns[0];
    expect(first.refunds[0].returnNumber).toBe(returned.number);
    expect(returned).toMatchObject({ status: "Received", refundStatus: result === "accept" ? "Refunded" : "Failed", refundedAmount: product.price });
    expect((await runSession.gateway()).transactions[0].refunded).toBe(result === "accept" ? product.price * 100 : 0);
    if (returned.refundProblem) await expect(page.locator("main")).not.toContainText(returned.refundProblem);
    await expect(page.locator(".order-grand-total dd")).toHaveText(money(product.price));
    await noOverflow(page);
    await testInfo.attach(`guest-refund-${result}`, { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });

    if (result === "refuse") {
      await runSession.configureGateway({ push: "deliver", statusFails: false, refund: "accept" });
      await runSession.advanceReturn(store, placed.number, returned.number, "retry");
      // Bounded receipt polling discovers settlement; the shopper sends no business write.
      await expect(page.locator(".order-refunds .refund-state")).toHaveText(words.refundState.Settled);
    }
    const settled = await expectPublicRefund(page, placed.number, "Settled", product.price, testInfo);
    const reading = await runSession.order(store, placed.number), gateway = await runSession.gateway();
    expect(reading.order).toMatchObject({ status: "Refunded", refundedTotal: product.price });
    expect(Date.parse(settled.paidAt!)).toBe(Date.parse(reading.order.paidAt!));
    expect(reading.returns).toHaveLength(1);
    expect(reading.returns[0]).toMatchObject({ number: returned.number, refundStatus: "Refunded", refundedAmount: product.price });
    expect(reading.attempts).toHaveLength(1); expect(reading.answersRecorded).toBe(1); expect(reading.kept).toEqual([]);
    expect(reading.documents.filter(document => document.kind === "Invoice")).toHaveLength(1);
    expect(reading.documents.filter(document => document.kind === "CreditNote")).toHaveLength(1);
    expect(gateway.transactions).toHaveLength(1);
    expect(gateway.transactions[0]).toMatchObject({ id: placed.transactionId, refunded: product.price * 100 });
    for (const form of product.variants) expect(await runSession.stock(form.sku)).toEqual({ sku: form.sku, onHand: form.stock, reserved: 0, available: form.stock });
    await downloadOrderDocument(page, reading.documents.find(document => document.kind === "CreditNote")!.number, testInfo);
    expect(posts).toEqual(originalPosts);
    await expect(page.locator(".return-form")).toHaveCount(0);
    await noOverflow(page);
    await testInfo.attach("guest-refund-settled", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
    await testInfo.attach("public-refund-facts", { body: JSON.stringify({ first, settled, reading, gateway, stock: await runSession.stock(variant.sku) }), contentType: "application/json" });
  });
}
