import { NotFoundError } from './api'

export type CartLine = {
  storeProductId: string
  name: string
  slug: string
  unitPrice: number
  quantity: number
  lineTotal: number
  available: number
  imageUrl: string | null
}

export type Cart = {
  items: CartLine[]
  count: number
  itemsTotal: number
  vatTotal: number
  changed: boolean
}

export type PaymentMethod = {
  code: string
  name: string
}

export type ShippingMethod = {
  code: string
  name: string
  price: number
}

export type CheckoutMethods = {
  paymentMethods: PaymentMethod[]
  shippingMethods: ShippingMethod[]
}

export type Address = {
  fullName: string
  line1: string
  line2: string | null
  city: string
  postalCode: string
  country: string
}

export type CheckoutRequest = {
  email: string
  billingAddress: Address
  shippingAddress: Address | null
  paymentMethodCode: string
  shippingMethodCode: string
}

export type PlacedOrder = {
  number: string
  token: string
  paymentInstructions: string
  redirectUrl: string | null
}

export type OrderLine = {
  productName: string
  unitPrice: number
  vatRate: number
  quantity: number
  lineTotal: number
}

export type Order = {
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
  lines: OrderLine[]
}

// Carries the messages of a ProblemDetails response so a form can show what the API rejected.
export class RequestFailed extends Error {
  readonly problems: string[]

  constructor(problems: string[]) {
    super(problems[0] ?? 'The request failed.')
    this.problems = problems
  }
}

async function send<T>(method: string, url: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, {
    method,
    signal,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (response.status === 404) {
    throw new NotFoundError(url)
  }

  if (!response.ok) {
    throw new RequestFailed(await problems(response))
  }

  return (await response.json()) as T
}

async function problems(response: Response): Promise<string[]> {
  if (!response.headers.get('Content-Type')?.includes('json')) {
    return []
  }

  const problem = (await response.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> }
  const messages = Object.values(problem.errors ?? {}).flat()

  return messages.length > 0 ? messages : [problem.detail ?? problem.title].filter((message) => message !== undefined)
}

export const getCart = () => send<Cart>('GET', '/api/storefront/cart')

export const addToCart = (storeProductId: string, quantity: number) => send<Cart>('POST', '/api/storefront/cart/items', { storeProductId, quantity })

export const setCartQuantity = (storeProductId: string, quantity: number) =>
  send<Cart>('PUT', `/api/storefront/cart/items/${storeProductId}`, { quantity })

export const removeFromCart = (storeProductId: string) => send<Cart>('DELETE', `/api/storefront/cart/items/${storeProductId}`)

export const getCheckoutMethods = (signal: AbortSignal) =>
  send<CheckoutMethods>('GET', '/api/storefront/checkout/methods', undefined, signal)

export const placeOrder = (request: CheckoutRequest) => send<PlacedOrder>('POST', '/api/storefront/checkout', request)

export const getOrder = (number: string, token: string, signal: AbortSignal) =>
  send<Order>('GET', `/api/storefront/orders/${encodeURIComponent(number)}?token=${encodeURIComponent(token)}`, undefined, signal)
