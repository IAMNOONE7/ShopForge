export type CurrentUser = { id: string; email: string; role: string; tenantId: string }

export type StoreStatus = 'draft' | 'published'

export type StoreTheme = { primaryColor: string; secondaryColor: string; borderRadius: number }

export type AdminStore = {
  id: string
  name: string
  currency: string
  culture: string
  status: StoreStatus
  theme: StoreTheme
  logoUrl: string | null
  primaryHostName: string | null
  company: Company | null
}

export type Company = {
  legalName: string
  line1: string
  city: string
  postalCode: string
  country: string
  registrationNumber: string
  vatNumber: string | null
}

export type StoreSettings = { name: string; currency: string; culture: string; theme: StoreTheme; company?: Company | null }

export type NewStore = StoreSettings & { hostName: string }

export type ProductImage = { id: string; url: string; altText: string | null; position: number }

export type Product = { id: string; sku: string; ean: string | null; weightGrams: number | null; images: ProductImage[] }

export type StoreProduct = {
  id: string
  productId: string
  sku: string
  name: string
  slug: string
  description: string | null
  price: number
  vatRate: number
  isVisible: boolean
  sortOrder: number
  categoryIds: string[]
}

export type Category = { id: string; name: string; slug: string; sortOrder: number; attributeIds: string[] }

export type AttributeType = 'text' | 'integer' | 'decimal' | 'boolean' | 'date' | 'select' | 'multiSelect'

export type AttributeDefinition = {
  id: string
  code: string
  name: string
  type: AttributeType
  unit: string | null
  isFilterable: boolean
  isVisibleOnProductPage: boolean
  sortOrder: number
  options: { id: string; code: string; name: string }[]
}

export type AttributeInput = {
  name: string
  type: AttributeType
  unit: string | null
  isFilterable: boolean
  isVisibleOnProductPage: boolean
  options: string[]
}

export type AttributeValues = Record<string, string | number | boolean | string[]>

export type ImportIssue = { row: number; column: string | null; message: string }

export type ImportReport = {
  created: number
  updated: number
  skipped: number
  invalid: number
  failed: number
  ignoredColumns: string[]
  issues: ImportIssue[]
}

export type StoreProductInput = {
  name: string
  slug?: string | null
  description?: string | null
  price: number
  vatRate: number
  isVisible: boolean
  sortOrder: number
}

export type AdminOrder = {
  number: string
  placedAt: string
  status: string
  email: string
  hasAccount: boolean
  grandTotal: number
  items: number
}

export type AdminAddress = { fullName: string; line1: string; line2: string | null; city: string; postalCode: string; country: string }

export type AdminOrderLine = { productName: string; unitPrice: number; vatRate: number; quantity: number; lineTotal: number }

export type AdminOrderDetail = {
  number: string
  placedAt: string
  status: string
  email: string
  currency: string
  paymentMethod: string
  shippingMethod: string
  shippingPrice: number
  itemsTotal: number
  vatTotal: number
  grandTotal: number
  billingAddress: AdminAddress
  shippingAddress: AdminAddress
  pickupPoint: string | null
  shipment: Shipment | null
  documents: OrderDocument[]
  lines: AdminOrderLine[]
}

export type Stock = { productId: string; onHand: number; reserved: number; available: number }

export type StockMovement = { occurredAt: string; quantity: number; reason: string; reference: string }

export type PaymentMethod = { code: string; name: string; providerKey: string; isActive: boolean }

export type ShippingMethod = {
  code: string
  name: string
  providerKey: string
  price: number
  vatRate: number
  isActive: boolean
  requiresPickupPoint: boolean
}

export type PickupPoint = {
  code: string
  name: string
  line1: string
  city: string
  postalCode: string
  country: string
  isActive: boolean
}

export type PickupPointInput = { name: string; line1: string; city: string; postalCode: string; country: string; isActive: boolean }

export type FailedMessage = { id: string; type: string; attempts: number; createdAt: string; error: string | null }

export type Shipment = { carrier: string; trackingNumber: string; trackingUrl: string | null; shippedAt: string }

export type OrderDocument = { number: string; kind: string; issuedAt: string }

type Problem = { title?: string; errors?: Record<string, string[]>; problems?: string[] }

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, problem: Problem | null) {
    const details = [...Object.values(problem?.errors ?? {}).flat(), ...(problem?.problems ?? [])].join(' ')
    super([problem?.title, details].filter(Boolean).join(': ') || `Request failed with status ${status}.`)
    this.status = status
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const isForm = body instanceof FormData
  const response = await fetch(url, {
    method,
    headers: body === undefined || isForm ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
  })

  if (!response.ok) {
    const isProblem = response.headers.get('Content-Type')?.includes('json')
    throw new ApiError(response.status, isProblem ? ((await response.json()) as Problem) : null)
  }

  return (response.status === 204 || response.headers.get('Content-Length') === '0' ? undefined : await response.json()) as T
}

export const api = {
  me: () => request<CurrentUser>('GET', '/api/admin/auth/me'),
  login: (email: string, password: string) => request<CurrentUser>('POST', '/api/admin/auth/login', { email, password }),
  logout: () => request<void>('POST', '/api/admin/auth/logout'),

  stores: () => request<AdminStore[]>('GET', '/api/admin/stores'),
  createStore: (input: NewStore) => request<AdminStore>('POST', '/api/admin/stores', input),
  updateStore: (storeId: string, input: StoreSettings) => request<AdminStore>('PUT', `/api/admin/stores/${storeId}`, input),
  publishStore: (storeId: string) => request<AdminStore>('POST', `/api/admin/stores/${storeId}/publish`),
  unpublishStore: (storeId: string) => request<AdminStore>('POST', `/api/admin/stores/${storeId}/unpublish`),
  uploadLogo: (storeId: string, file: File) => request<void>('PUT', `/api/admin/stores/${storeId}/logo`, formWith(file)),

  products: () => request<Product[]>('GET', '/api/admin/products'),
  createProduct: (input: { sku: string; ean: string | null; weightGrams: number | null }) =>
    request<Product>('POST', '/api/admin/products', input),
  uploadProductImage: (productId: string, file: File, altText: string) =>
    request<ProductImage>('POST', `/api/admin/products/${productId}/images`, formWith(file, { altText })),
  deleteProductImage: (productId: string, imageId: string) =>
    request<void>('DELETE', `/api/admin/products/${productId}/images/${imageId}`),

  storeProducts: (storeId: string) => request<StoreProduct[]>('GET', `/api/admin/stores/${storeId}/products`),
  listProduct: (storeId: string, productId: string, input: StoreProductInput) =>
    request<StoreProduct>('POST', `/api/admin/stores/${storeId}/products`, { productId, ...input }),
  updateStoreProduct: (storeId: string, storeProductId: string, input: StoreProductInput) =>
    request<StoreProduct>('PUT', `/api/admin/stores/${storeId}/products/${storeProductId}`, input),
  assignCategories: (storeId: string, storeProductId: string, categoryIds: string[]) =>
    request<StoreProduct>('PUT', `/api/admin/stores/${storeId}/products/${storeProductId}/categories`, { categoryIds }),

  importProducts: (storeId: string, file: File) => request<ImportReport>('POST', `/api/admin/stores/${storeId}/import`, formWith(file)),

  attributes: (storeId: string) => request<AttributeDefinition[]>('GET', `/api/admin/stores/${storeId}/attributes`),
  createAttribute: (storeId: string, input: AttributeInput) =>
    request<AttributeDefinition>('POST', `/api/admin/stores/${storeId}/attributes`, { ...input, sortOrder: 0 }),
  addOption: (storeId: string, attributeId: string, name: string) =>
    request<AttributeDefinition>('POST', `/api/admin/stores/${storeId}/attributes/${attributeId}/options`, { name }),
  assignCategoryAttributes: (storeId: string, categoryId: string, attributeIds: string[]) =>
    request<string[]>('PUT', `/api/admin/stores/${storeId}/categories/${categoryId}/attributes`, { attributeIds }),
  productAttributes: (storeId: string, storeProductId: string) =>
    request<{ values: AttributeValues }>('GET', `/api/admin/stores/${storeId}/products/${storeProductId}/attributes`),
  setProductAttributes: (storeId: string, storeProductId: string, values: AttributeValues) =>
    request<{ values: AttributeValues }>('PUT', `/api/admin/stores/${storeId}/products/${storeProductId}/attributes`, { values }),

  failedMessages: (storeId: string) => request<FailedMessage[]>('GET', `/api/admin/stores/${storeId}/failed-messages`),
  requeueMessage: (storeId: string, messageId: string) =>
    request<void>('POST', `/api/admin/stores/${storeId}/failed-messages/${messageId}/requeue`),

  stock: () => request<Stock[]>('GET', '/api/admin/stock'),
  setStock: (productId: string, quantity: number) => request<Stock>('PUT', `/api/admin/stock/${productId}`, { quantity }),
  stockMovements: (productId: string) => request<StockMovement[]>('GET', `/api/admin/stock/${productId}/movements`),

  orders: (storeId: string) => request<AdminOrder[]>('GET', `/api/admin/stores/${storeId}/orders`),
  order: (storeId: string, number: string) => request<AdminOrderDetail>('GET', `/api/admin/stores/${storeId}/orders/${number}`),
  confirmOrderPayment: (storeId: string, number: string) =>
    request<AdminOrderDetail>('POST', `/api/admin/stores/${storeId}/orders/${number}/payment`),
  cancelOrder: (storeId: string, number: string) => request<AdminOrderDetail>('POST', `/api/admin/stores/${storeId}/orders/${number}/cancel`),
  createShipment: (storeId: string, number: string, trackingNumber: string) =>
    request<AdminOrderDetail>('POST', `/api/admin/stores/${storeId}/orders/${number}/shipment`, { trackingNumber }),
  refundOrder: (storeId: string, number: string) => request<AdminOrderDetail>('POST', `/api/admin/stores/${storeId}/orders/${number}/refund`),
  documentUrl: (storeId: string, orderNumber: string, documentNumber: string) =>
    `/api/admin/stores/${storeId}/orders/${orderNumber}/documents/${documentNumber}`,

  paymentProviders: (storeId: string) => request<string[]>('GET', `/api/admin/stores/${storeId}/payment-providers`),
  paymentMethods: (storeId: string) => request<PaymentMethod[]>('GET', `/api/admin/stores/${storeId}/payment-methods`),
  createPaymentMethod: (storeId: string, name: string, providerKey: string) =>
    request<PaymentMethod>('POST', `/api/admin/stores/${storeId}/payment-methods`, { name, providerKey, isActive: true }),
  updatePaymentMethod: (storeId: string, code: string, input: { name: string; isActive: boolean }) =>
    request<PaymentMethod>('PUT', `/api/admin/stores/${storeId}/payment-methods/${code}`, input),

  shippingProviders: (storeId: string) => request<string[]>('GET', `/api/admin/stores/${storeId}/shipping-providers`),
  shippingMethods: (storeId: string) => request<ShippingMethod[]>('GET', `/api/admin/stores/${storeId}/shipping-methods`),
  createShippingMethod: (
    storeId: string,
    input: { name: string; providerKey: string; price: number; vatRate: number; requiresPickupPoint: boolean },
  ) => request<ShippingMethod>('POST', `/api/admin/stores/${storeId}/shipping-methods`, { ...input, isActive: true }),
  updateShippingMethod: (
    storeId: string,
    code: string,
    input: { name: string; price: number; vatRate: number; isActive: boolean; requiresPickupPoint: boolean },
  ) => request<ShippingMethod>('PUT', `/api/admin/stores/${storeId}/shipping-methods/${code}`, input),

  pickupPoints: (storeId: string) => request<PickupPoint[]>('GET', `/api/admin/stores/${storeId}/pickup-points`),
  createPickupPoint: (storeId: string, input: PickupPointInput) =>
    request<PickupPoint>('POST', `/api/admin/stores/${storeId}/pickup-points`, input),
  updatePickupPoint: (storeId: string, code: string, input: PickupPointInput) =>
    request<PickupPoint>('PUT', `/api/admin/stores/${storeId}/pickup-points/${code}`, input),

  categories: (storeId: string) => request<Category[]>('GET', `/api/admin/stores/${storeId}/categories`),
  createCategory: (storeId: string, name: string) =>
    request<Category>('POST', `/api/admin/stores/${storeId}/categories`, { name, sortOrder: 0 }),
}

function formWith(file: File, fields: Record<string, string> = {}) {
  const form = new FormData()
  form.append('file', file)
  Object.entries(fields).forEach(([name, value]) => form.append(name, value))
  return form
}
