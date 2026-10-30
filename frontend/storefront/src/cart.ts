import { validFreeShipping, type FreeShipping } from "./freeShipping";
import { HttpError, requestJson } from "./api/http";
import { withOrderFacts, type CustomerRefund } from "./refundFacts";
import { validShippingTerms, validUnavailableMethod, type MapParcel, type UnavailableShippingMethod } from "./shippingQuotes";

export type CartLine = {
  storeProductId: string;
  variantId: string;
  optionValues: string[];
  name: string;
  slug: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
  available: number;
  imageUrl: string | null;
};

export const cartLineKey = (line: Pick<CartLine, "storeProductId" | "variantId">) =>
  `${line.storeProductId}:${line.variantId}`;

export const cartLineName = (line: Pick<CartLine, "name" | "optionValues">) =>
  line.optionValues.length > 0
    ? `${line.name} (${line.optionValues.join(" / ")})`
    : line.name;

export type CartDiscount = {
  code: string;
  name: string;
  amount: number;
};

export type Cart = {
  items: CartLine[];
  count: number;
  itemsTotal: number;
  vatTotal: number;
  changed: boolean;
  discount: CartDiscount | null;
  discountProblem: string | null;
  freeShipping?: FreeShipping | null;
};

export type PaymentMethod = {
  code: string;
  name: string;
};

export type ShippingMethod = {
  code: string;
  name: string;
  price: number;
  fullPrice?: number;
  requiresPickupPoint: boolean;
  // Where the shopper chooses the point: nowhere, a list this shop can produce, or the carrier's own map.
  pickupPointChoice: "none" | "list" | "carrier-map";
  pricing?: "flat" | "perParcel";
  parcels?: number | null;
  mapParcel?: MapParcel | null;
};

export type PickupPoint = {
  code: string;
  name: string;
  line1: string;
  city: string;
  postalCode: string;
  country: string;
};

export type CheckoutMethods = {
  destinationCountry: string | null;
  paymentMethods: PaymentMethod[];
  shippingMethods: ShippingMethod[];
  unavailableShippingMethods?: UnavailableShippingMethod[];
  freeShipping?: FreeShipping | null;
};

export type Address = {
  fullName: string;
  line1: string;
  line2: string | null;
  city: string;
  postalCode: string;
  country: string;
};

export type CheckoutRequest = {
  email: string;
  phone: string;
  billingAddress: Address;
  shippingAddress: Address | null;
  paymentMethodCode: string;
  shippingMethodCode: string;
  pickupPointCode: string | null;
  expectedShippingPrice: number;
};

export type PlacedOrder = {
  number: string;
  token: string;
  paymentInstructions: string;
  redirectUrl: string | null;
};

export type OrderLine = {
  productName: string;
  unitPrice: number;
  vatRate: number;
  quantity: number;
  lineTotal: number;
};

export type Shipment = {
  carrier: string;
  trackingNumber: string;
  trackingUrl: string | null;
};

export type OrderDocument = {
  number: string;
  kind: string;
  issuedAt: string;
};

export type OrderDiscount = {
  code: string;
  name: string;
  amount: number;
};

export type Order = {
  number: string;
  placedAt: string;
  status: string;
  email: string;
  currency: string;
  paymentMethod: string;
  shippingMethod: string;
  shippingPrice: number;
  itemsTotal: number;
  vatTotal: number;
  grandTotal: number;
  discount: OrderDiscount | null;
  pickupPoint: string | null;
  shipment: Shipment | null;
  documents: OrderDocument[];
  lines: OrderLine[];
  paidAt: string | null;
  refunds: CustomerRefund[];
};

function checkedCart(cart: Cart): Cart {
  if (!cart || typeof cart !== "object" || Array.isArray(cart) || !validFreeShipping(cart.freeShipping) ||
    cart.discountProblem !== undefined && cart.discountProblem !== null && typeof cart.discountProblem !== "string")
    throw new HttpError("invalid-response", null);
  return cart;
}

export const getCart = (signal?: AbortSignal) =>
  requestJson<Cart>("/api/storefront/cart", { signal }).then(checkedCart);

export const addToCart = (storeProductId: string, variantId: string, quantity: number) =>
  requestJson<Cart>("/api/storefront/cart/items", {
    method: "POST",
    body: { storeProductId, variantId, quantity },
  }).then(checkedCart);

export const setCartQuantity = (storeProductId: string, variantId: string, quantity: number) =>
  requestJson<Cart>(`/api/storefront/cart/items/${encodeURIComponent(storeProductId)}?variantId=${encodeURIComponent(variantId)}`, {
    method: "PUT",
    body: { quantity },
  }).then(checkedCart);

export const removeFromCart = (storeProductId: string, variantId: string) =>
  requestJson<Cart>(`/api/storefront/cart/items/${encodeURIComponent(storeProductId)}?variantId=${encodeURIComponent(variantId)}`, {
    method: "DELETE",
  }).then(checkedCart);

export const applyDiscount = (code: string) =>
  requestJson<Cart>("/api/storefront/cart/discount", {
    method: "PUT",
    body: { code },
  }).then(checkedCart);

export const removeDiscount = () =>
  requestJson<Cart>("/api/storefront/cart/discount", { method: "DELETE" }).then(checkedCart).then(cart => {
    if (cart.discount !== null || cart.discountProblem !== null) throw new HttpError("invalid-response", null);
    return cart;
  });

export async function getCheckoutMethods(country: string | null, signal?: AbortSignal) {
  const destinationCountry = country?.trim().toUpperCase() || null;
  const query = destinationCountry ? "?country=" + encodeURIComponent(destinationCountry) : "";
  const result = await requestJson<CheckoutMethods>("/api/storefront/checkout/methods" + query, { signal });
  if (!result || result.destinationCountry !== destinationCountry || !validFreeShipping(result.freeShipping) ||
    !Array.isArray(result.paymentMethods) || !Array.isArray(result.shippingMethods) ||
    !result.paymentMethods.every(validPaymentMethod) ||
    !result.shippingMethods.every(method => validPaymentMethod(method) &&
      Number.isFinite(method.price) && method.price >= 0 && typeof method.requiresPickupPoint === "boolean" &&
      ["none", "list", "carrier-map"].includes(method.pickupPointChoice) && validShippingTerms(method)) ||
    new Set(result.paymentMethods.map(method => method.code)).size !== result.paymentMethods.length ||
    new Set(result.shippingMethods.map(method => method.code)).size !== result.shippingMethods.length ||
    (result.unavailableShippingMethods !== undefined && (!Array.isArray(result.unavailableShippingMethods) ||
      !result.unavailableShippingMethods.every(validUnavailableMethod) ||
      new Set([...result.shippingMethods, ...result.unavailableShippingMethods].map(method => method.code)).size !==
        result.shippingMethods.length + result.unavailableShippingMethods.length)))
    throw new HttpError("invalid-response", null);
  return result;
}

function validPaymentMethod(method: PaymentMethod) {
  return method && typeof method.code === "string" && method.code.trim().length > 0 &&
    typeof method.name === "string" && method.name.trim().length > 0;
}

export const getPickupPoints = (methodCode: string, signal?: AbortSignal) =>
  requestJson<PickupPoint[]>(
    `/api/storefront/checkout/pickup-points/${encodeURIComponent(methodCode)}`,
    { signal },
  );

export const placeOrder = (request: CheckoutRequest, idempotencyKey: string) =>
  requestJson<PlacedOrder>("/api/storefront/checkout", {
    method: "POST",
    body: request,
    headers: { "Idempotency-Key": idempotencyKey },
  });

export const getOrder = (number: string, token: string, signal: AbortSignal) =>
  requestJson<Order>(
    `/api/storefront/orders/${encodeURIComponent(number)}?token=${encodeURIComponent(token)}`,
    { signal },
  ).then(withOrderFacts);
