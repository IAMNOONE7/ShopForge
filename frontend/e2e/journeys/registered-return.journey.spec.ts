import { test, expect } from "../support/isolatedFixtures.ts";
import { registerShopper, signInShopper, signOutShopper } from "../support/registeredShopper.ts";
import { addVariant, checkout, downloadOrderDocument, expectPublicRefund, expectReceipt, labels, money, noOverflow } from "../support/shopper.ts";

type ReturnsReading = { returnable: unknown[]; returns: { number: string; status: string; refundedAmount: number; lines: { productName: string; quantity: number }[] }[] };

test("a registered shopper returns the purchased variant and downloads the credit note", async ({ page, context, runSession }, testInfo) => {
  const [store, otherStore] = runSession.run.stores, origin = runSession.origin(store), words = labels(testInfo);
  const shopper = await registerShopper(page, runSession, store, testInfo);
  await signOutShopper(page, origin, testInfo);
  await page.goto(`${origin}/account/sign-in`);
  await signInShopper(page, shopper, testInfo);
  await expect(page).toHaveURL(`${origin}/account`);
  await expect(page.locator(".account-header")).toContainText(shopper.email);

  const product = runSession.run.products.find(product => product.variants.some(variant => variant.size === "L"))!;
  const variant = product.variants.find(variant => variant.size === "L")!, savedName = `${product.name} (${variant.size})`;
  const returnWrites: string[] = [], checkouts: string[] = [];
  page.on("request", request => {
    if (request.method() !== "POST") return;
    const path = new URL(request.url()).pathname;
    if (path.endsWith("/returns")) returnWrites.push(request.headers()["idempotency-key"]);
    if (path === "/api/storefront/checkout") checkouts.push(request.headers()["idempotency-key"]);
  });
  await addVariant(page, runSession, store, product, variant, testInfo);
  const placed = await checkout(page, runSession, store, product, testInfo);
  await page.getByRole("button", { name: "Pay", exact: true }).click();
  await expectReceipt(page, origin, placed.number);
  await expect(page.locator(".order-status.paid")).toHaveText(words.orders.status.Paid);
  expect(await runSession.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock - 1, reserved: 0, available: variant.stock - 1 });

  const accountOrder = `${origin}/account/orders/${placed.number}`;
  await page.goto(`${origin}/account`);
  await page.locator(".account-order-list").getByRole("link", { name: words.account.viewOrder.replace("{{number}}", placed.number), exact: true }).click();
  await expect(page).toHaveURL(accountOrder);
  const quantity = page.getByRole("combobox", { name: words.orders.returnQuantity.replace("{{name}}", savedName), exact: true });
  await quantity.selectOption("1");
  const reason = "Velikost mi nesedí / The size does not fit";
  await page.getByRole("textbox", { name: words.orders.returnReason, exact: true }).fill(reason);
  const response = page.waitForResponse(answer => new URL(answer.url()).pathname === `/api/storefront/account/orders/${placed.number}/returns` && answer.request().method() === "POST");
  await page.getByRole("button", { name: words.orders.returnAsk, exact: true }).click();
  const requested = await response;
  expect(requested.status()).toBe(200);
  expect(requested.request().postDataJSON()).toEqual({ lines: [{ storeProductId: product.listings[store.id], variantId: variant.id, quantity: 1 }], reason });
  const returns = await requested.json() as ReturnsReading;
  expect(returns.returns).toHaveLength(1); expect(returns.returnable).toEqual([]);
  const returned = returns.returns[0];
  expect(returned).toMatchObject({ status: "Requested", refundedAmount: 0, refundState: null, refundSettledAt: null,
    lines: [{ storeProductId: product.listings[store.id], variantId: variant.id, productName: savedName, quantity: 1 }] });
  await expect(page.locator(".return-list")).toContainText(returned.number);
  await expect(page.locator(".return-list .return-handling-status")).toHaveText(words.orders.returnStatus.Requested);
  await expect(page.locator(".returns .return-form")).toHaveCount(0);
  await noOverflow(page);

  await runSession.advanceReturn(store, placed.number, returned.number, "accept");
  await page.reload();
  await expect(page.locator(".return-list .return-handling-status")).toHaveText(words.orders.returnStatus.Accepted);
  expect(await runSession.stock(variant.sku)).toEqual({ sku: variant.sku, onHand: variant.stock - 1, reserved: 0, available: variant.stock - 1 });
  const received = await runSession.advanceReturn(store, placed.number, returned.number, "receive");
  expect(received).toMatchObject({ status: "Received", refund: "Refunded", refundedAmount: product.price, refundProblem: null });
  await expect.poll(async () => (await runSession.order(store, placed.number)).documents.some(document => document.kind === "CreditNote")).toBe(true);
  const reading = await runSession.order(store, placed.number);
  expect(reading.order).toMatchObject({ status: "Refunded", refundedTotal: product.price, email: shopper.email, currency: "CZK" });
  expect(reading.lines).toEqual([{ productName: savedName, variantId: variant.id, quantity: 1, unitPrice: product.price, discount: 0 }]);
  expect(reading.returns).toEqual([{ number: returned.number, status: "Received", refundStatus: "Refunded", refundedAmount: product.price, refundProblem: null }]);
  expect(reading.attempts).toEqual([expect.objectContaining({ provider: "comgate", reference: placed.transactionId, status: "Paid", amount: product.price, currency: "CZK", environment: "test" })]);
  expect(reading.answersRecorded).toBe(1); expect(reading.kept).toEqual([]);
  expect(reading.documents.filter(document => document.kind === "Invoice")).toHaveLength(1);
  expect(reading.documents.filter(document => document.kind === "CreditNote")).toHaveLength(1);
  for (const form of product.variants) expect(await runSession.stock(form.sku)).toEqual({ sku: form.sku, onHand: form.stock, reserved: 0, available: form.stock });
  const transaction = (await runSession.gateway()).transactions.find(transaction => transaction.id === placed.transactionId);
  expect(transaction).toMatchObject({ refunded: product.price * 100, test: true });

  await page.reload();
  await expect(page.locator(".order-status.refunded")).toHaveText(words.orders.status.Refunded);
  await expect(page.locator(".return-list")).toContainText(savedName);
  await expect(page.locator(".return-list .return-handling-status")).toHaveText(words.orders.returnStatus.Received.replace("{{amount}}", money(product.price)));
  await expect(page.locator(".returns .refund-state")).toHaveText(words.orders.refundState.Settled);
  const publicFacts = await expectPublicRefund(page, placed.number, "Settled", product.price, testInfo);
  await expect(page.locator(".return-list time")).toHaveAttribute("datetime", publicFacts.refunds[0].settledAt!);
  const credit = reading.documents.find(document => document.kind === "CreditNote")!;
  await downloadOrderDocument(page, credit.number, testInfo);
  await noOverflow(page);
  await expect.poll(async () => (await runSession.mail(shopper.email)).some(letter => letter.subject === `${store.name}: Refund for order ${placed.number}` && letter.attachments.some(name => name.endsWith(".pdf")))).toBe(true);
  expect((await runSession.mail(shopper.email)).some(letter => letter.subject === `${store.name}: Return ${returned.number} for order ${placed.number}`)).toBe(true);

  const other = await context.newPage();
  await other.goto(`${runSession.origin(otherStore)}/account/orders/${placed.number}`);
  await expect(other.getByRole("heading", { name: words.account.privateOrderTitle, exact: true })).toBeVisible();
  await expect(other.locator(".order-documents")).toHaveCount(0);
  await other.close();
  await signOutShopper(page, origin, testInfo);
  await page.goto(accountOrder);
  await expect(page.getByRole("heading", { name: words.account.privateOrderTitle, exact: true })).toBeVisible();
  await expect(page.locator(".order-documents")).toHaveCount(0);
  await page.locator("main").getByRole("link", { name: words.auth.signIn, exact: true }).click();
  await signInShopper(page, shopper, testInfo);
  await expect(page).toHaveURL(accountOrder);
  await expect(page.locator(".return-list .return-handling-status")).toHaveText(words.orders.returnStatus.Received.replace("{{amount}}", money(product.price)));
  await downloadOrderDocument(page, credit.number, testInfo);
  expect(returnWrites).toHaveLength(1); expect(returnWrites[0]).toBeTruthy();
  expect(checkouts).toHaveLength(1); expect(checkouts[0]).toBeTruthy();
  await testInfo.attach("credited-account-order", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
  await testInfo.attach("return-facts", { body: JSON.stringify({ run: runSession.run.id, store: store.id, ...reading, transaction, stock: await runSession.stock(variant.sku) }), contentType: "application/json" });
});
