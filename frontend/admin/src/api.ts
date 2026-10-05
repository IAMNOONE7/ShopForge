import { HttpError, requestBlob, requestJson, saveDownload } from "./api/http";

export type SignInResponse = {
  twoFactorRequired: boolean;
  ticket: string | null;
  user: CurrentUser | null;
};

export type CurrentUser = {
  id: string;
  email: string;
  role: string;
  tenantId: string;
};

export type StoreStatus = "draft" | "published";

export type StoreTheme = {
  primaryColor: string;
  secondaryColor: string;
  borderRadius: number;
};

export type AdminStore = {
  id: string;
  name: string;
  currency: string;
  culture: string;
  status: StoreStatus;
  theme: StoreTheme;
  logoUrl: string | null;
  primaryHostName: string | null;
  returnWindowDays: number;
  company: Company | null;
};

export type Company = {
  legalName: string;
  line1: string;
  city: string;
  postalCode: string;
  country: string;
  registrationNumber: string;
  vatNumber: string | null;
};

export type StoreSettings = {
  name: string;
  currency: string;
  culture: string;
  returnWindowDays?: number;
  theme: StoreTheme;
  company?: Company | null;
};

export type NewStore = StoreSettings & { hostName: string };

export type ProductImage = {
  id: string;
  url: string;
  altText: string | null;
  position: number;
};

export type Product = {
  id: string;
  sku: string;
  ean: string | null;
  weightGrams: number | null;
  optionNames: string[];
  variants: ProductVariant[];
  images: ProductImage[];
};

export type ProductVariant = {
  id: string;
  sku: string;
  ean: string | null;
  weightGrams: number | null;
  optionValues: string[];
  position: number;
};

export type StoreProduct = {
  id: string;
  productId: string;
  sku: string;
  name: string;
  slug: string;
  description: string | null;
  price: number;
  vatRate: number;
  isVisible: boolean;
  sortOrder: number;
  categoryIds: string[];
};

export type Category = {
  id: string;
  name: string;
  slug: string;
  sortOrder: number;
  attributeIds: string[];
};

export type AttributeType =
  | "text"
  | "integer"
  | "decimal"
  | "boolean"
  | "date"
  | "select"
  | "multiSelect";

export type AttributeDefinition = {
  id: string;
  code: string;
  name: string;
  type: AttributeType;
  unit: string | null;
  isFilterable: boolean;
  isVisibleOnProductPage: boolean;
  sortOrder: number;
  options: { id: string; code: string; name: string }[];
};

export type AttributeInput = {
  code: string | null;
  name: string;
  type: AttributeType;
  unit: string | null;
  isFilterable: boolean;
  isVisibleOnProductPage: boolean;
  sortOrder: number;
  options: string[];
};

export type AttributeUpdate = Pick<
  AttributeInput,
  "name" | "unit" | "isFilterable" | "isVisibleOnProductPage" | "sortOrder"
>;

export type AttributeValues = Record<
  string,
  string | number | boolean | string[]
>;

export type ImportIssue = {
  row: number;
  column: string | null;
  message: string;
};

export type ImportReport = {
  created: number;
  updated: number;
  skipped: number;
  invalid: number;
  failed: number;
  ignoredColumns: string[];
  issues: ImportIssue[];
};

export type StoreProductInput = {
  name: string;
  slug?: string | null;
  description?: string | null;
  price: number;
  vatRate: number;
  isVisible: boolean;
  sortOrder: number;
};

export type AdminOrder = {
  number: string;
  placedAt: string;
  status: string;
  email: string;
  hasAccount: boolean;
  grandTotal: number;
  items: number;
};

export type AdminAddress = {
  fullName: string;
  line1: string;
  line2: string | null;
  city: string;
  postalCode: string;
  country: string;
};

export type AdminOrderLine = {
  productName: string;
  unitPrice: number;
  vatRate: number;
  quantity: number;
  lineTotal: number;
};

export type AdminOrderDetail = {
  number: string;
  placedAt: string;
  status: string;
  email: string;
  currency: string;
  paymentMethod: string;
  shippingMethod: string;
  // Who is carrying it, as the order recorded at the time rather than whatever the method says today.
  carrier: string | null;
  phone: string | null;
  shippingPrice: number;
  itemsTotal: number;
  vatTotal: number;
  grandTotal: number;
  billingAddress: AdminAddress;
  shippingAddress: AdminAddress;
  discount: OrderDiscount | null;
  pickupPoint: string | null;
  shipment: Shipment | null;
  documents: OrderDocument[];
  lines: AdminOrderLine[];
};

export type Stock = {
  variantId: string;
  onHand: number;
  reserved: number;
  available: number;
};

export type StockMovement = {
  occurredAt: string;
  quantity: number;
  reason: string;
  reference: string;
};

export type PaymentMethod = {
  code: string;
  name: string;
  providerKey: string;
  isActive: boolean;
};

export type ShippingMethod = {
  code: string;
  name: string;
  providerKey: string;
  price: number;
  vatRate: number;
  isActive: boolean;
  requiresPickupPoint: boolean;
  // What this method will take. Null and empty mean no limit, which is every method until a store sets one.
  maxWeightGrams: number | null;
  countries: string[];
};

export type PickupPoint = {
  code: string;
  name: string;
  line1: string;
  city: string;
  postalCode: string;
  country: string;
  isActive: boolean;
};

export type PickupPointInput = {
  name: string;
  line1: string;
  city: string;
  postalCode: string;
  country: string;
  isActive: boolean;
};

export type OrderReturn = {
  id: string;
  number: string;
  orderNumber: string;
  status: string;
  requestedAt: string;
  refundedAmount: number;
  reason: string | null;
  lines: { productName: string; quantity: number }[];
};

export type Review = {
  id: string;
  productName: string;
  productSlug: string;
  author: string;
  rating: number;
  text: string;
  writtenAt: string;
  status: string;
};

export type FailedMessage = {
  id: string;
  type: string;
  attempts: number;
  createdAt: string;
  error: string | null;
};

export type Shipment = {
  carrier: string;
  trackingNumber: string;
  trackingUrl: string | null;
  shippedAt: string;
};

export type OrderDocument = { number: string; kind: string; issuedAt: string };

export type OrderDiscount = { code: string; name: string; amount: number };

export type Discount = {
  code: string;
  name: string;
  kind: string;
  value: number;
  minimumOrderAmount: number | null;
  startsAt: string | null;
  endsAt: string | null;
  maxRedemptions: number | null;
  maxRedemptionsPerCustomer: number | null;
  redemptions: number;
  isActive: boolean;
};

export type DiscountInput = {
  code: string;
  name: string;
  kind: string;
  value: number;
  minimumOrderAmount: number | null;
  startsAt: string | null;
  endsAt: string | null;
  maxRedemptions: number | null;
  maxRedemptionsPerCustomer: number | null;
  isActive: boolean;
};

export { HttpError as ApiError };

function request<T>(
  method: string,
  url: string,
  body?: unknown,
  signal?: AbortSignal,
) {
  return requestJson<T>(url, { method, body, signal });
}

export const api = {
  me: (signal?: AbortSignal) =>
    requestJson<CurrentUser>("/api/admin/auth/me", {
      signal,
      notifyUnauthorized: false,
    }),
  login: (email: string, password: string) =>
    requestJson<SignInResponse>("/api/admin/auth/login", {
      method: "POST",
      body: { email, password },
      notifyUnauthorized: false,
    }),
  completeTwoFactor: (ticket: string, code: string) =>
    requestJson<SignInResponse>("/api/admin/auth/two-factor", {
      method: "POST",
      body: { ticket, code },
      notifyUnauthorized: false,
    }),
  logout: () => request<void>("POST", "/api/admin/auth/logout"),

  stores: (signal?: AbortSignal) =>
    request<AdminStore[]>("GET", "/api/admin/stores", undefined, signal),
  createStore: (input: NewStore) =>
    request<AdminStore>("POST", "/api/admin/stores", input),
  updateStore: (storeId: string, input: StoreSettings) =>
    request<AdminStore>("PUT", `/api/admin/stores/${storeId}`, input),
  publishStore: (storeId: string) =>
    request<AdminStore>("POST", `/api/admin/stores/${storeId}/publish`),
  unpublishStore: (storeId: string) =>
    request<AdminStore>("POST", `/api/admin/stores/${storeId}/unpublish`),
  uploadLogo: (storeId: string, file: File) =>
    request<void>("PUT", `/api/admin/stores/${storeId}/logo`, formWith(file)),

  products: (signal?: AbortSignal) =>
    request<Product[]>("GET", "/api/admin/products", undefined, signal),
  createProduct: (input: {
    sku: string;
    ean: string | null;
    weightGrams: number | null;
  }) => request<Product>("POST", "/api/admin/products", input),
  updateProduct: (productId: string, input: { ean: string | null; weightGrams: number | null }) =>
    request<Product>("PUT", `/api/admin/products/${productId}`, input),
  uploadProductImage: (productId: string, file: File, altText: string) =>
    request<ProductImage>(
      "POST",
      `/api/admin/products/${productId}/images`,
      formWith(file, { altText }),
    ),
  deleteProductImage: (productId: string, imageId: string) =>
    request<void>(
      "DELETE",
      `/api/admin/products/${productId}/images/${imageId}`,
    ),

  storeProducts: (storeId: string, signal?: AbortSignal) =>
    request<StoreProduct[]>(
      "GET",
      `/api/admin/stores/${storeId}/products`,
      undefined,
      signal,
    ),
  listProduct: (storeId: string, productId: string, input: StoreProductInput) =>
    request<StoreProduct>("POST", `/api/admin/stores/${storeId}/products`, {
      productId,
      ...input,
    }),
  updateStoreProduct: (
    storeId: string,
    storeProductId: string,
    input: StoreProductInput,
  ) =>
    request<StoreProduct>(
      "PUT",
      `/api/admin/stores/${storeId}/products/${storeProductId}`,
      input,
    ),
  assignCategories: (
    storeId: string,
    storeProductId: string,
    categoryIds: string[],
  ) =>
    request<StoreProduct>(
      "PUT",
      `/api/admin/stores/${storeId}/products/${storeProductId}/categories`,
      { categoryIds },
    ),

  importProducts: (storeId: string, file: File) =>
    request<ImportReport>(
      "POST",
      `/api/admin/stores/${storeId}/import`,
      formWith(file),
    ),

  attributes: (storeId: string, signal?: AbortSignal) =>
    request<AttributeDefinition[]>(
      "GET",
      `/api/admin/stores/${storeId}/attributes`,
      undefined,
      signal,
    ),
  createAttribute: (storeId: string, input: AttributeInput) =>
    request<AttributeDefinition>(
      "POST",
      `/api/admin/stores/${storeId}/attributes`,
      input,
    ),
  updateAttribute: (storeId: string, attributeId: string, input: AttributeUpdate) =>
    request<AttributeDefinition>(
      "PUT",
      `/api/admin/stores/${storeId}/attributes/${attributeId}`,
      input,
    ),
  addOption: (storeId: string, attributeId: string, name: string) =>
    request<AttributeDefinition>(
      "POST",
      `/api/admin/stores/${storeId}/attributes/${attributeId}/options`,
      { name },
    ),
  assignCategoryAttributes: (
    storeId: string,
    categoryId: string,
    attributeIds: string[],
  ) =>
    request<string[]>(
      "PUT",
      `/api/admin/stores/${storeId}/categories/${categoryId}/attributes`,
      { attributeIds },
    ),
  productAttributes: (
    storeId: string,
    storeProductId: string,
    signal?: AbortSignal,
  ) =>
    request<{ values: AttributeValues }>(
      "GET",
      `/api/admin/stores/${storeId}/products/${storeProductId}/attributes`,
      undefined,
      signal,
    ),
  setProductAttributes: (
    storeId: string,
    storeProductId: string,
    values: AttributeValues,
  ) =>
    request<{ values: AttributeValues }>(
      "PUT",
      `/api/admin/stores/${storeId}/products/${storeProductId}/attributes`,
      { values },
    ),

  failedMessages: (storeId: string, signal?: AbortSignal) =>
    request<FailedMessage[]>(
      "GET",
      `/api/admin/stores/${storeId}/failed-messages`,
      undefined,
      signal,
    ),
  requeueMessage: (storeId: string, messageId: string) =>
    request<void>(
      "POST",
      `/api/admin/stores/${storeId}/failed-messages/${messageId}/requeue`,
    ),

  returns: (storeId: string, signal?: AbortSignal) =>
    request<OrderReturn[]>(
      "GET",
      `/api/admin/stores/${storeId}/returns`,
      undefined,
      signal,
    ),
  decideReturn: (
    storeId: string,
    returnId: string,
    decision: "accept" | "refuse" | "receive",
  ) =>
    request<OrderReturn>(
      "POST",
      `/api/admin/stores/${storeId}/returns/${returnId}/${decision}`,
    ),

  reviews: (storeId: string, status?: string, signal?: AbortSignal) =>
    request<Review[]>(
      "GET",
      `/api/admin/stores/${storeId}/reviews${status ? `?status=${status}` : ""}`,
      undefined,
      signal,
    ),
  publishReview: (storeId: string, reviewId: string) =>
    request<void>(
      "POST",
      `/api/admin/stores/${storeId}/reviews/${reviewId}/publish`,
    ),
  rejectReview: (storeId: string, reviewId: string) =>
    request<void>(
      "POST",
      `/api/admin/stores/${storeId}/reviews/${reviewId}/reject`,
    ),

  stock: (signal?: AbortSignal) =>
    request<Stock[]>("GET", "/api/admin/stock", undefined, signal),
  setStock: (variantId: string, quantity: number) =>
    request<Stock>("PUT", `/api/admin/stock/${variantId}`, { quantity }),
  stockMovements: (variantId: string, signal?: AbortSignal) =>
    request<StockMovement[]>(
      "GET",
      `/api/admin/stock/${variantId}/movements`,
      undefined,
      signal,
    ),

  orders: (storeId: string, signal?: AbortSignal) =>
    request<AdminOrder[]>(
      "GET",
      `/api/admin/stores/${storeId}/orders`,
      undefined,
      signal,
    ),
  order: (storeId: string, number: string, signal?: AbortSignal) =>
    request<AdminOrderDetail>(
      "GET",
      `/api/admin/stores/${storeId}/orders/${number}`,
      undefined,
      signal,
    ),
  confirmOrderPayment: (storeId: string, number: string) =>
    request<AdminOrderDetail>(
      "POST",
      `/api/admin/stores/${storeId}/orders/${number}/payment`,
    ),
  cancelOrder: (storeId: string, number: string) =>
    request<AdminOrderDetail>(
      "POST",
      `/api/admin/stores/${storeId}/orders/${number}/cancel`,
    ),
  createShipment: (storeId: string, number: string, trackingNumber: string) =>
    request<AdminOrderDetail>(
      "POST",
      `/api/admin/stores/${storeId}/orders/${number}/shipment`,
      { trackingNumber },
    ),
  refundOrder: (storeId: string, number: string) =>
    request<AdminOrderDetail>(
      "POST",
      `/api/admin/stores/${storeId}/orders/${number}/refund`,
    ),
  downloadDocument: async (
    storeId: string,
    orderNumber: string,
    documentNumber: string,
  ) => {
    const download = await requestBlob(
      `/api/admin/stores/${storeId}/orders/${orderNumber}/documents/${documentNumber}`,
      `${documentNumber}.pdf`,
    );
    saveDownload(download);
  },

  paymentProviders: (storeId: string, signal?: AbortSignal) =>
    request<string[]>(
      "GET",
      `/api/admin/stores/${storeId}/payment-providers`,
      undefined,
      signal,
    ),
  paymentMethods: (storeId: string, signal?: AbortSignal) =>
    request<PaymentMethod[]>(
      "GET",
      `/api/admin/stores/${storeId}/payment-methods`,
      undefined,
      signal,
    ),
  createPaymentMethod: (storeId: string, name: string, providerKey: string) =>
    request<PaymentMethod>(
      "POST",
      `/api/admin/stores/${storeId}/payment-methods`,
      { name, providerKey, isActive: true },
    ),
  updatePaymentMethod: (
    storeId: string,
    code: string,
    input: { name: string; isActive: boolean },
  ) =>
    request<PaymentMethod>(
      "PUT",
      `/api/admin/stores/${storeId}/payment-methods/${code}`,
      input,
    ),

  shippingProviders: (storeId: string, signal?: AbortSignal) =>
    request<string[]>(
      "GET",
      `/api/admin/stores/${storeId}/shipping-providers`,
      undefined,
      signal,
    ),
  shippingMethods: (storeId: string, signal?: AbortSignal) =>
    request<ShippingMethod[]>(
      "GET",
      `/api/admin/stores/${storeId}/shipping-methods`,
      undefined,
      signal,
    ),
  createShippingMethod: (
    storeId: string,
    input: {
      name: string;
      providerKey: string;
      price: number;
      vatRate: number;
      requiresPickupPoint: boolean;
      maxWeightGrams: number | null;
      countries: string[];
    },
  ) =>
    request<ShippingMethod>(
      "POST",
      `/api/admin/stores/${storeId}/shipping-methods`,
      { ...input, isActive: true },
    ),
  updateShippingMethod: (
    storeId: string,
    code: string,
    input: {
      name: string;
      price: number;
      vatRate: number;
      isActive: boolean;
      requiresPickupPoint: boolean;
      maxWeightGrams: number | null;
      countries: string[];
    },
  ) =>
    request<ShippingMethod>(
      "PUT",
      `/api/admin/stores/${storeId}/shipping-methods/${code}`,
      input,
    ),

  discounts: (storeId: string, signal?: AbortSignal) =>
    request<Discount[]>(
      "GET",
      `/api/admin/stores/${storeId}/discounts`,
      undefined,
      signal,
    ),
  createDiscount: (storeId: string, input: DiscountInput) =>
    request<Discount>("POST", `/api/admin/stores/${storeId}/discounts`, input),
  updateDiscount: (storeId: string, code: string, input: DiscountInput) =>
    request<Discount>(
      "PUT",
      `/api/admin/stores/${storeId}/discounts/${code}`,
      input,
    ),

  pickupPoints: (storeId: string, signal?: AbortSignal) =>
    request<PickupPoint[]>(
      "GET",
      `/api/admin/stores/${storeId}/pickup-points`,
      undefined,
      signal,
    ),
  createPickupPoint: (storeId: string, input: PickupPointInput) =>
    request<PickupPoint>(
      "POST",
      `/api/admin/stores/${storeId}/pickup-points`,
      input,
    ),
  updatePickupPoint: (storeId: string, code: string, input: PickupPointInput) =>
    request<PickupPoint>(
      "PUT",
      `/api/admin/stores/${storeId}/pickup-points/${code}`,
      input,
    ),

  categories: (storeId: string, signal?: AbortSignal) =>
    request<Category[]>(
      "GET",
      `/api/admin/stores/${storeId}/categories`,
      undefined,
      signal,
    ),
  createCategory: (storeId: string, name: string) =>
    request<Category>("POST", `/api/admin/stores/${storeId}/categories`, {
      name,
      sortOrder: 0,
    }),
};

function formWith(file: File, fields: Record<string, string> = {}) {
  const form = new FormData();
  form.append("file", file);
  Object.entries(fields).forEach(([name, value]) => form.append(name, value));
  return form;
}
