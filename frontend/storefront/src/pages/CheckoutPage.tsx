import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { getCheckoutMethods, getPickupPoints, placeOrder, RequestFailed, type Address, type PickupPoint, type ShippingMethod } from '../cart'
import { useCart } from '../cartContext'
import { useCustomer } from '../customerContext'
import { Message } from '../components/Message'
import { formatPrice, useStore } from '../storeContext'
import { useRequest } from '../useRequest'

export function CheckoutPage() {
  const store = useStore()
  const navigate = useNavigate()
  const { cart, reload } = useCart()
  const { customer } = useCustomer()
  const methods = useRequest('checkout-methods', getCheckoutMethods)
  const [shipElsewhere, setShipElsewhere] = useState(false)
  const [shipping, setShipping] = useState<ShippingMethod | null>(null)
  const [pickupPoints, setPickupPoints] = useState<PickupPoint[]>([])
  const [problems, setProblems] = useState<string[]>([])
  const firstShippingMethod = methods.status === 'ready' ? (methods.data.shippingMethods[0] ?? null) : null

  // The first method is selected for the shopper, so its pickup points have to be loaded without a click.
  useEffect(() => {
    if (firstShippingMethod) {
      chooseShipping(firstShippingMethod)
    }
  }, [firstShippingMethod?.code]) // eslint-disable-line react-hooks/exhaustive-deps

  function chooseShipping(method: ShippingMethod) {
    setShipping(method)
    setPickupPoints([])

    if (method.requiresPickupPoint) {
      getPickupPoints(method.code)
        .then(setPickupPoints)
        .catch(() => setPickupPoints([]))
    }
  }

  async function submit(form: FormData) {
    setProblems([])

    try {
      const order = await placeOrder({
        email: String(form.get('email')).trim(),
        billingAddress: address(form, 'billing'),
        shippingAddress: shipElsewhere ? address(form, 'shipping') : null,
        paymentMethodCode: String(form.get('paymentMethodCode')),
        shippingMethodCode: String(form.get('shippingMethodCode')),
        pickupPointCode: form.get('pickupPointCode') === null ? null : String(form.get('pickupPointCode')),
      })

      reload()

      if (order.redirectUrl) {
        window.location.assign(order.redirectUrl)
        return
      }

      void navigate(`/order/${order.number}?token=${order.token}`, { state: { instructions: order.paymentInstructions } })
    } catch (exception) {
      const messages = exception instanceof RequestFailed ? exception.problems : []
      setProblems(messages.length > 0 ? messages : ['The order could not be placed. Please try again.'])
      reload()
    }
  }

  if (methods.status !== 'ready' || !cart) {
    return methods.status === 'error' ? <Message title="Checkout unavailable" text="Please try again in a moment." /> : null
  }

  if (cart.items.length === 0) {
    return <Message title="Your cart is empty" text="Add a product before checking out." />
  }

  const { paymentMethods, shippingMethods } = methods.data

  if (paymentMethods.length === 0 || shippingMethods.length === 0) {
    return <Message title="Checkout unavailable" text="This store is not taking orders at the moment." />
  }

  const chosenShipping = shipping ?? shippingMethods[0]

  return (
    <form action={submit} className="checkout">
      <div className="checkout-fields">
        <h1>Checkout</h1>
        <label>
          E-mail <input name="email" type="email" defaultValue={customer?.email ?? ''} readOnly={customer !== null} required />
        </label>
        {customer === null && (
          <p className="hint">
            <Link to="/account/sign-in">Sign in</Link> to keep your orders in one place, or order as a guest.
          </p>
        )}
        <fieldset>
          <legend>Billing address</legend>
          <AddressFields prefix="billing" fullName={customer ? `${customer.firstName} ${customer.lastName}` : ''} />
        </fieldset>
        <label className="checkout-toggle">
          <input type="checkbox" checked={shipElsewhere} onChange={(event) => setShipElsewhere(event.target.checked)} /> Ship to a different address
        </label>
        {shipElsewhere && (
          <fieldset>
            <legend>Shipping address</legend>
            <AddressFields prefix="shipping" fullName="" />
          </fieldset>
        )}
        <fieldset>
          <legend>Shipping</legend>
          {shippingMethods.map((method, index) => (
            <label key={method.code} className="checkout-option">
              <input
                type="radio"
                name="shippingMethodCode"
                value={method.code}
                defaultChecked={index === 0}
                onChange={() => chooseShipping(method)}
                required
              />
              {method.name} <span>{formatPrice(method.price, store)}</span>
            </label>
          ))}
          {chosenShipping.requiresPickupPoint && (
            <label>
              Pickup point{' '}
              <select name="pickupPointCode" required>
                <option value="">Choose a pickup point…</option>
                {pickupPoints.map((point) => (
                  <option key={point.code} value={point.code}>
                    {point.name} — {point.line1}, {point.city}
                  </option>
                ))}
              </select>
            </label>
          )}
        </fieldset>
        <fieldset>
          <legend>Payment</legend>
          {paymentMethods.map((method, index) => (
            <label key={method.code} className="checkout-option">
              <input type="radio" name="paymentMethodCode" value={method.code} defaultChecked={index === 0} required /> {method.name}
            </label>
          ))}
        </fieldset>
        <button type="submit">Place order</button>
        {problems.map((problem) => (
          <p key={problem} className="error">
            {problem}
          </p>
        ))}
      </div>
      <aside className="checkout-summary">
        <h2>Your order</h2>
        <ul>
          {cart.items.map((line) => (
            <li key={line.storeProductId}>
              <span>
                {line.quantity} × {line.name}
              </span>
              <span>{formatPrice(line.lineTotal, store)}</span>
            </li>
          ))}
          <li>
            <span>{chosenShipping.name}</span>
            <span>{formatPrice(chosenShipping.price, store)}</span>
          </li>
        </ul>
        <p className="checkout-total">
          Total <strong>{formatPrice(cart.itemsTotal + chosenShipping.price, store)}</strong>
        </p>
        <p className="hint">Prices include VAT.</p>
      </aside>
    </form>
  )
}

function AddressFields({ prefix, fullName }: { prefix: string; fullName: string }) {
  return (
    <>
      <label>
        Full name <input name={`${prefix}.fullName`} defaultValue={fullName} autoComplete="name" required />
      </label>
      <label>
        Street and number <input name={`${prefix}.line1`} autoComplete="address-line1" required />
      </label>
      <label>
        Address line 2 <input name={`${prefix}.line2`} autoComplete="address-line2" />
      </label>
      <label>
        City <input name={`${prefix}.city`} autoComplete="address-level2" required />
      </label>
      <label>
        Postal code <input name={`${prefix}.postalCode`} autoComplete="postal-code" required />
      </label>
      <label>
        Country <input name={`${prefix}.country`} maxLength={2} placeholder="IE" autoComplete="country" required />
      </label>
    </>
  )
}

function address(form: FormData, prefix: string): Address {
  const value = (field: string) => String(form.get(`${prefix}.${field}`) ?? '').trim()
  const line2 = value('line2')

  return {
    fullName: value('fullName'),
    line1: value('line1'),
    line2: line2.length > 0 ? line2 : null,
    city: value('city'),
    postalCode: value('postalCode'),
    country: value('country').toUpperCase(),
  }
}
