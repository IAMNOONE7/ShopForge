import { useState } from 'react'
import { Link } from 'react-router'
import { addToCart } from '../cart'
import { useCart } from '../cartContext'

export function AddToCart({ storeProductId }: { storeProductId: string }) {
  const { apply } = useCart()
  const [added, setAdded] = useState(false)
  const [failed, setFailed] = useState(false)

  async function add() {
    try {
      apply(await addToCart(storeProductId, 1))
      setAdded(true)
      setFailed(false)
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
      {failed && <span className="error">This product could not be added.</span>}
    </p>
  )
}
