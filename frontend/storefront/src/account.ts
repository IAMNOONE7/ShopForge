import { requestJson } from "./api/http";
import type { Order } from "./cart";

export type Customer = {
  email: string;
  firstName: string;
  lastName: string;
  phone: string | null;
};

export type Registration = {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  phone: string | null;
};

export type CustomerOrder = {
  number: string;
  placedAt: string;
  status: string;
  grandTotal: number;
  items: number;
};

export const register = (registration: Registration) =>
  requestJson<void>("/api/storefront/account/register", {
    method: "POST",
    body: registration,
    notifyUnauthorized: false,
  });

export const verifyEmail = (token: string) =>
  requestJson<Customer>("/api/storefront/account/verify", {
    method: "POST",
    body: { token },
    notifyUnauthorized: false,
  });

export const signIn = (email: string, password: string) =>
  requestJson<Customer>("/api/storefront/account/login", {
    method: "POST",
    body: { email, password },
    notifyUnauthorized: false,
  });

export const signOut = () =>
  requestJson<void>("/api/storefront/account/logout", { method: "POST" });

export const getProfile = (signal?: AbortSignal) =>
  requestJson<Customer>("/api/storefront/account/me", {
    signal,
    notifyUnauthorized: false,
  });

export const updateProfile = (profile: {
  firstName: string;
  lastName: string;
  phone: string | null;
}) =>
  requestJson<Customer>("/api/storefront/account/me", {
    method: "PUT",
    body: profile,
  });

export const requestPasswordReset = (email: string) =>
  requestJson<void>("/api/storefront/account/password/forgot", {
    method: "POST",
    body: { email },
    notifyUnauthorized: false,
  });

export const resetPassword = (token: string, password: string) =>
  requestJson<void>("/api/storefront/account/password/reset", {
    method: "POST",
    body: { token, password },
    notifyUnauthorized: false,
  });

export const getOrders = (signal?: AbortSignal) =>
  requestJson<CustomerOrder[]>("/api/storefront/account/orders", { signal });

export const getAccountOrder = (number: string, signal?: AbortSignal) =>
  requestJson<Order>(
    `/api/storefront/account/orders/${encodeURIComponent(number)}`,
    { signal },
  );

export type ReturnableLine = {
  storeProductId: string;
  productName: string;
  quantity: number;
};

export type CustomerReturn = {
  number: string;
  status: string;
  requestedAt: string;
  refundedAmount: number;
  lines: { productName: string; quantity: number }[];
};

export type Returns = {
  closesAt: string | null;
  returnable: ReturnableLine[];
  returns: CustomerReturn[];
};

export const getReturns = (number: string, signal?: AbortSignal) =>
  requestJson<Returns>(
    `/api/storefront/account/orders/${encodeURIComponent(number)}/returns`,
    { signal },
  );

export const requestReturn = (
  number: string,
  lines: { storeProductId: string; quantity: number }[],
  reason: string | null,
) =>
  requestJson<Returns>(
    `/api/storefront/account/orders/${encodeURIComponent(number)}/returns`,
    { method: "POST", body: { lines, reason } },
  );

export type WishlistItem = {
  storeProductId: string;
  name: string;
  slug: string;
  price: number;
  imageUrl: string | null;
};

export const getWishlist = (signal?: AbortSignal) =>
  requestJson<WishlistItem[]>("/api/storefront/account/wishlist", { signal });

export const addToWishlist = (storeProductId: string) =>
  requestJson<void>("/api/storefront/account/wishlist", {
    method: "POST",
    body: { storeProductId },
  });

export const removeFromWishlist = (storeProductId: string) =>
  requestJson<void>(`/api/storefront/account/wishlist/${storeProductId}`, {
    method: "DELETE",
  });

export const writeReview = (
  slug: string,
  review: { rating: number; text: string; author: string },
) =>
  requestJson<void>(
    `/api/storefront/products/${encodeURIComponent(slug)}/reviews`,
    { method: "POST", body: review },
  );
