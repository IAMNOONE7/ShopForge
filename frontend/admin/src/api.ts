export type CurrentUser = { id: string; email: string; role: string; tenantId: string }

export type AdminStore = {
  id: string
  name: string
  currency: string
  culture: string
  logoUrl: string | null
  primaryHostName: string | null
}

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
  isVisible: boolean
  sortOrder: number
  categoryIds: string[]
}

export type Category = { id: string; name: string; slug: string; sortOrder: number }

export type StoreProductInput = {
  name: string
  slug?: string | null
  description?: string | null
  price: number
  isVisible: boolean
  sortOrder: number
}

type Problem = { title?: string; errors?: Record<string, string[]> }

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, problem: Problem | null) {
    const details = problem?.errors ? Object.values(problem.errors).flat().join(' ') : ''
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
