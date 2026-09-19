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

export type ProductPage = {
  items: ProductSummary[]
  totalCount: number
  page: number
  pageSize: number
}

export type ProductDetail = {
  id: string
  slug: string
  name: string
  description: string | null
  price: number
  images: { url: string; altText: string | null }[]
  categories: Category[]
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

export function getProducts(category: string | undefined, signal: AbortSignal) {
  const query = category ? `?category=${encodeURIComponent(category)}` : ''
  return getJson<ProductPage>(`/api/storefront/products${query}`, signal)
}

export function getProduct(slug: string, signal: AbortSignal) {
  return getJson<ProductDetail>(`/api/storefront/products/${encodeURIComponent(slug)}`, signal)
}
