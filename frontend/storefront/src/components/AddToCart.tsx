import { useState } from 'react'
import { Link } from 'react-router'
import { addToCart } from '../cart'
import { useCart } from '../cartContext'
import { Button } from './ui/Button'
import { InlineMessage } from './ui/InlineMessage'

export function AddToCart({ storeProductId, available }: { storeProductId: string; available: number }) {
  const { apply } = useCart()
  const [added, setAdded] = useState(false)
  const [capped, setCapped] = useState(false)
  const [failed, setFailed] = useState(false)
  const [pending, setPending] = useState(false)

  async function add() {
    setPending(true)

    try {
      const cart = await addToCart(storeProductId, 1)
      apply(cart)
      setAdded(true)
      setFailed(false)
      setCapped(cart.items.find((line) => line.storeProductId === storeProductId)?.quantity === available)
    } catch {
      setFailed(true)
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="add-to-cart">
      <Button type="button" onClick={() => void add()} busy={pending} busyLabel="Adding…">
        Add to cart
      </Button>
      {added && <Link to="/cart">In your cart — view cart</Link>}
      {capped && <span className="hint">That is all we have in stock.</span>}
      {failed && <InlineMessage tone="error">This product could not be added.</InlineMessage>}
    </div>
  )
}
