import { requestJson } from "./api/http";

export type CartLine = {
  storeProductId: string;
  variantId: string;
  name: string;
  slug: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
  available: number;
  imageUrl: string | null;
};

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
};

export type PaymentMethod = {
  code: string;
  name: string;
};

export type ShippingMethod = {
  code: string;
  name: string;
  price: number;
  requiresPickupPoint: boolean;
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
  paymentMethods: PaymentMethod[];
  shippingMethods: ShippingMethod[];
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
  billingAddress: Address;
  shippingAddress: Address | null;
  paymentMethodCode: string;
  shippingMethodCode: string;
  pickupPointCode: string | null;
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
};

export const getCart = (signal?: AbortSignal) =>
  requestJson<Cart>("/api/storefront/cart", { signal });

export const addToCart = (storeProductId: string, variantId: string, quantity: number) =>
  requestJson<Cart>("/api/storefront/cart/items", {
    method: "POST",
    body: { storeProductId, variantId, quantity },
  });

export const setCartQuantity = (storeProductId: string, quantity: number) =>
  requestJson<Cart>(`/api/storefront/cart/items/${storeProductId}`, {
    method: "PUT",
    body: { quantity },
  });

export const removeFromCart = (storeProductId: string) =>
  requestJson<Cart>(`/api/storefront/cart/items/${storeProductId}`, {
    method: "DELETE",
  });

export const applyDiscount = (code: string) =>
  requestJson<Cart>("/api/storefront/cart/discount", {
    method: "PUT",
    body: { code },
  });

export const removeDiscount = () =>
  requestJson<Cart>("/api/storefront/cart/discount", { method: "DELETE" });

export const getCheckoutMethods = (signal: AbortSignal) =>
  requestJson<CheckoutMethods>("/api/storefront/checkout/methods", { signal });

export const getPickupPoints = (methodCode: string, signal?: AbortSignal) =>
  requestJson<PickupPoint[]>(
    `/api/storefront/checkout/pickup-points/${encodeURIComponent(methodCode)}`,
    { signal },
  );

export const placeOrder = (request: CheckoutRequest) =>
  requestJson<PlacedOrder>("/api/storefront/checkout", {
    method: "POST",
    body: request,
  });

export const getOrder = (number: string, token: string, signal: AbortSignal) =>
  requestJson<Order>(
    `/api/storefront/orders/${encodeURIComponent(number)}?token=${encodeURIComponent(token)}`,
    { signal },
  );
