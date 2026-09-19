export type Category = {
  name: string
  slug: string
}

export type ProductSummary = {
  id: string
  slug: string
  name: string
  price: number
  imageUrl: string | null
}

export type AttributeType = 'text' | 'integer' | 'decimal' | 'boolean' | 'date' | 'select' | 'multiSelect'

export type Facet = {
  code: string
  name: string
  type: AttributeType
  unit: string | null
  options: { code: string; name: string; count: number; selected: boolean }[] | null
  min: number | string | null
  max: number | string | null
  selectedMin: number | string | null
  selectedMax: number | string | null
  trueCount: number | null
  falseCount: number | null
  selected: boolean | null
}

export type ProductPage = {
  items: ProductSummary[]
  totalCount: number
  page: number
  pageSize: number
  filters: Facet[]
}

export type ProductAttribute = {
  code: string
  name: string
  type: AttributeType
  unit: string | null
  value: string | number | boolean | string[]
}

export type ProductDetail = {
  id: string
  slug: string
  name: string
  description: string | null
  price: number
  images: { url: string; altText: string | null }[]
  categories: Category[]
  attributes: ProductAttribute[]
}

export class NotFoundError extends Error {}

async function getJson<T>(url: string, signal: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal })

  if (response.status === 404) {
    throw new NotFoundError(url)
  }

  if (!response.ok) {
    throw new Error(`Request to ${url} failed with status ${response.status}.`)
  }

  return (await response.json()) as T
}

export function getCategories(signal: AbortSignal) {
  return getJson<Category[]>('/api/storefront/categories', signal)
}

// `query` carries filters (f.<code>), sort and page exactly as they appear in the page URL.
export function getProducts(category: string | undefined, query: URLSearchParams, signal: AbortSignal) {
  const parameters = new URLSearchParams(query)
  if (category) {
    parameters.set('category', category)
  }
  return getJson<ProductPage>(`/api/storefront/products?${parameters}`, signal)
}

export function getProduct(slug: string, signal: AbortSignal) {
  return getJson<ProductDetail>(`/api/storefront/products/${encodeURIComponent(slug)}`, signal)
}
