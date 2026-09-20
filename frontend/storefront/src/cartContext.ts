import { createContext, useContext } from 'react'
import type { Cart } from './cart'

export type CartState = {
  cart: Cart | null
  apply: (cart: Cart) => void
  reload: () => void
}

export const CartContext = createContext<CartState | null>(null)

export function useCart(): CartState {
  const state = useContext(CartContext)

  if (!state) {
    throw new Error('useCart must be used inside CartContext.')
  }

  return state
}
