import { useState } from 'react'
import { Link } from 'react-router'
import { addToCart } from '../cart'
import { useCart } from '../cartContext'

export function AddToCart({ storeProductId, available }: { storeProductId: string; available: number }) {
  const { apply } = useCart()
  const [added, setAdded] = useState(false)
  const [capped, setCapped] = useState(false)
  const [failed, setFailed] = useState(false)

  async function add() {
    try {
      const cart = await addToCart(storeProductId, 1)
      apply(cart)
      setAdded(true)
      setFailed(false)
      setCapped(cart.items.find((line) => line.storeProductId === storeProductId)?.quantity === available)
    } catch {
      setFailed(true)
    }
  }

  return (
    <p className="add-to-cart">
      <button type="button" onClick={() => void add()}>
        Add to cart
      </button>
      {added && <Link to="/cart">In your cart — view cart</Link>}
      {capped && <span className="hint">That is all we have in stock.</span>}
      {failed && <span className="error">This product could not be added.</span>}
    </p>
  )
}
