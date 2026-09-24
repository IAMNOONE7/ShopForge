import { NotFoundError } from './api'
import { RequestFailed } from './cart'

export type Customer = {
  email: string
  firstName: string
  lastName: string
  phone: string | null
}

export type Registration = {
  email: string
  password: string
  firstName: string
  lastName: string
  phone: string | null
}

export type CustomerOrder = {
  number: string
  placedAt: string
  status: string
  grandTotal: number
  items: number
}

async function send<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (response.status === 404) {
    throw new NotFoundError(url)
  }

  if (!response.ok) {
    const problem = response.headers.get('Content-Type')?.includes('json')
      ? ((await response.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> })
      : null
    const messages = Object.values(problem?.errors ?? {}).flat()

    throw new RequestFailed(messages.length > 0 ? messages : [problem?.detail ?? problem?.title ?? 'The request failed.'])
  }

  // Registration and password endpoints answer without a body.
  return (response.headers.get('Content-Type')?.includes('json') ? await response.json() : undefined) as T
}

export const register = (registration: Registration) => send<void>('POST', '/api/storefront/account/register', registration)

export const verifyEmail = (token: string) => send<Customer>('POST', '/api/storefront/account/verify', { token })

export const signIn = (email: string, password: string) => send<Customer>('POST', '/api/storefront/account/login', { email, password })

export const signOut = () => send<void>('POST', '/api/storefront/account/logout')

export const getProfile = () => send<Customer>('GET', '/api/storefront/account/me')

export const updateProfile = (profile: { firstName: string; lastName: string; phone: string | null }) =>
  send<Customer>('PUT', '/api/storefront/account/me', profile)

export const requestPasswordReset = (email: string) => send<void>('POST', '/api/storefront/account/password/forgot', { email })

export const resetPassword = (token: string, password: string) => send<void>('POST', '/api/storefront/account/password/reset', { token, password })

export const getOrders = () => send<CustomerOrder[]>('GET', '/api/storefront/account/orders')

export type WishlistItem = {
  storeProductId: string
  name: string
  slug: string
  price: number
  imageUrl: string | null
}

export const getWishlist = () => send<WishlistItem[]>('GET', '/api/storefront/account/wishlist')

export const addToWishlist = (storeProductId: string) => send<void>('POST', '/api/storefront/account/wishlist', { storeProductId })

export const removeFromWishlist = (storeProductId: string) =>
  send<void>('DELETE', `/api/storefront/account/wishlist/${storeProductId}`)

export const writeReview = (slug: string, review: { rating: number; text: string; author: string }) =>
  send<void>('POST', `/api/storefront/products/${encodeURIComponent(slug)}/reviews`, review)
