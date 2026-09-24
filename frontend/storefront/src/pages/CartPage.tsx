import { Link } from 'react-router'
import { removeFromCart, setCartQuantity, type Cart } from '../cart'
import { useCart } from '../cartContext'
import { DiscountField } from '../components/DiscountField'
import { Message } from '../components/Message'
import { LoadingState } from '../components/ui/LoadingState'
import { formatPrice, useStore } from '../storeContext'

export function CartPage() {
  const store = useStore()
  const { cart, apply, reload } = useCart()

  function change(request: Promise<Cart>) {
    request.then(apply).catch(reload)
  }

  if (!cart) {
    return <LoadingState label="Loading cart…" lines={4} />
  }

  if (cart.items.length === 0) {
    return <Message title="Your cart is empty" text="Add a product to start an order." />
  }

  return (
    <section className="cart">
      <h1>Cart</h1>
      {cart.changed && <p className="notice">Your cart was updated: some products are no longer available in the quantity you picked.</p>}
      <ul className="cart-lines">
        {cart.items.map((line) => (
          <li key={line.storeProductId} className="cart-line">
            {line.imageUrl ? (
              <img src={line.imageUrl} alt="" className="cart-thumbnail" />
            ) : (
              <div className="cart-thumbnail" aria-hidden="true" />
            )}
            <Link to={`/p/${line.slug}`} className="cart-line-name">
              {line.name}
            </Link>
            <span className="cart-unit-price">{formatPrice(line.unitPrice, store)}</span>
            <input
              type="number"
              min="1"
              max={Math.min(line.available, 99)}
              value={line.quantity}
              aria-label={`Quantity of ${line.name}`}
              onChange={(event) => {
                const quantity = Number(event.target.value)
                if (quantity >= 1) {
                  change(setCartQuantity(line.storeProductId, quantity))
                }
              }}
            />
            <span className="cart-line-total">{formatPrice(line.lineTotal, store)}</span>
            <button type="button" className="link-button" onClick={() => change(removeFromCart(line.storeProductId))}>
              Remove
            </button>
          </li>
        ))}
      </ul>
      <DiscountField />
      <p className="cart-total">
        Total <strong>{formatPrice(cart.itemsTotal, store)}</strong>
      </p>
      <p className="hint">Includes {formatPrice(cart.vatTotal, store)} VAT. Shipping is added at checkout.</p>
      <Link to="/checkout" className="button">
        Proceed to checkout
      </Link>
    </section>
  )
}
