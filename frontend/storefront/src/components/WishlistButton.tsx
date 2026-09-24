import { useEffect, useState } from 'react'
import { addToWishlist, getWishlist, removeFromWishlist } from '../account'
import { useCustomer } from '../customerContext'

export function WishlistButton({ storeProductId }: { storeProductId: string }) {
  const { customer } = useCustomer()
  const [wanted, setWanted] = useState<boolean | null>(null)

  useEffect(() => {
    if (!customer) {
      return
    }

    let active = true

    getWishlist()
      .then((items) => active && setWanted(items.some((item) => item.storeProductId === storeProductId)))
      .catch(() => undefined)

    return () => {
      active = false
    }
  }, [customer, storeProductId])

  if (!customer || wanted === null) {
    return null
  }

  async function toggle() {
    const next = !wanted
    setWanted(next)
    await (next ? addToWishlist(storeProductId) : removeFromWishlist(storeProductId)).catch(() => setWanted(!next))
  }

  return (
    <button type="button" className="link-button" aria-pressed={wanted} onClick={() => void toggle()}>
      {wanted ? '♥ On your wishlist' : '♡ Save for later'}
    </button>
  )
}
