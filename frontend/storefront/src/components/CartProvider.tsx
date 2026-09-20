import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { getCart, type Cart } from '../cart'
import { CartContext } from '../cartContext'

export function CartProvider({ children }: { children: ReactNode }) {
  const [cart, setCart] = useState<Cart | null>(null)

  const reload = useCallback(() => {
    getCart()
      .then(setCart)
      .catch(() => setCart(null))
  }, [])

  useEffect(reload, [reload])

  return <CartContext value={{ cart, apply: setCart, reload }}>{children}</CartContext>
}
