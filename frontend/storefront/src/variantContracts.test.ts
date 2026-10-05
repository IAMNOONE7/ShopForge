import { afterEach, expect, it, vi } from "vitest";
import { requestReturn } from "./account";
import { placeOrder, removeFromCart, setCartQuantity } from "./cart";

afterEach(() => vi.unstubAllGlobals());

it("sends variant identities and idempotency headers on commerce writes", async () => {
  const fetcher = vi.fn().mockImplementation(() => Promise.resolve(
    new Response("{}", { headers: { "Content-Type": "application/json" } }),
  ));
  vi.stubGlobal("fetch", fetcher);

  await setCartQuantity("listing-id", "red-id", 2);
  await removeFromCart("listing-id", "blue-id");
  await requestReturn("2026-00023", {
    lines: [{ storeProductId: "listing-id", variantId: "blue-id", quantity: 1 }],
    reason: "Wrong size",
  }, "return-key");
  await placeOrder({
    email: "shopper@example.test", phone: "+420123456789",
    billingAddress: { fullName: "Shopper", line1: "1 Main St", line2: null, city: "Prague", postalCode: "10000", country: "CZ" },
    shippingAddress: null, paymentMethodCode: "bank", shippingMethodCode: "pickup", pickupPointCode: null,
  }, "checkout-key");

  expect(fetcher).toHaveBeenNthCalledWith(1,
    "/api/storefront/cart/items/listing-id?variantId=red-id",
    expect.objectContaining({ method: "PUT", body: '{"quantity":2}' }),
  );
  expect(fetcher).toHaveBeenNthCalledWith(2,
    "/api/storefront/cart/items/listing-id?variantId=blue-id",
    expect.objectContaining({ method: "DELETE" }),
  );
  expect(fetcher).toHaveBeenNthCalledWith(3,
    "/api/storefront/account/orders/2026-00023/returns",
    expect.objectContaining({
      method: "POST",
      body: '{"lines":[{"storeProductId":"listing-id","variantId":"blue-id","quantity":1}],"reason":"Wrong size"}',
      headers: { "Idempotency-Key": "return-key", "Content-Type": "application/json" },
    }),
  );
  expect(fetcher).toHaveBeenNthCalledWith(4,
    "/api/storefront/checkout",
    expect.objectContaining({ method: "POST", headers: { "Idempotency-Key": "checkout-key", "Content-Type": "application/json" } }),
  );
});
