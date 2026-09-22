import { useEffect, useState } from 'react'
import { useLocation, useParams, useSearchParams } from 'react-router'
import { getOrder } from '../cart'
import { Message } from '../components/Message'
import { formatPrice, useStore } from '../storeContext'
import { useRequest } from '../useRequest'

export function OrderPage() {
  const { number = '' } = useParams()
  const [parameters] = useSearchParams()
  const token = parameters.get('token') ?? ''
  const store = useStore()
  const { state } = useLocation()
  const instructions = (state as { instructions?: string } | null)?.instructions
  // A hosted payment is confirmed by the provider calling us, which can land a moment after the shopper is back.
  const [attempt, setAttempt] = useState(0)
  const order = useRequest(`order:${number}:${token}:${attempt}`, (signal) => getOrder(number, token, signal))
  const awaitingPayment = order.status === 'ready' && order.data.status === 'AwaitingPayment'

  useEffect(() => {
    if (!awaitingPayment || attempt >= 5) {
      return
    }

    const timer = setTimeout(() => setAttempt((current) => current + 1), 3000)

    return () => clearTimeout(timer)
  }, [awaitingPayment, attempt])

  switch (order.status) {
    case 'loading':
      return null
    case 'not-found':
      return <Message title="Order not found" text="Check the link from your confirmation e-mail." />
    case 'error':
      return <Message title="Something went wrong" text="The order could not be loaded. Please try again." />
    case 'ready': {
      const { lines, shippingMethod, shippingPrice, itemsTotal, vatTotal, grandTotal, email, paymentMethod, status } = order.data

      return (
        <section className="order">
          <h1>Thank you for your order</h1>
          <p>
            Order <strong>{order.data.number}</strong> was placed. A confirmation goes to {email}.
          </p>
          {instructions && <p className="notice">{instructions}</p>}
          {status === 'AwaitingPayment' && !instructions && <p className="notice">We are waiting for your payment to be confirmed.</p>}
          {status === 'Paid' && <p className="notice">Your payment was received. Thank you.</p>}
          {status === 'Cancelled' && <p className="notice">This order was cancelled because it was not paid in time.</p>}
          <table className="order-lines">
            <tbody>
              {lines.map((line) => (
                <tr key={line.productName}>
                  <td>{line.productName}</td>
                  <td>{line.quantity} ×</td>
                  <td>{formatPrice(line.unitPrice, store)}</td>
                  <td className="order-amount">{formatPrice(line.lineTotal, store)}</td>
                </tr>
              ))}
              <tr>
                <td colSpan={3}>Items</td>
                <td className="order-amount">{formatPrice(itemsTotal, store)}</td>
              </tr>
              <tr>
                <td colSpan={3}>{shippingMethod}</td>
                <td className="order-amount">{formatPrice(shippingPrice, store)}</td>
              </tr>
              <tr className="order-total">
                <td colSpan={3}>Total</td>
                <td className="order-amount">{formatPrice(grandTotal, store)}</td>
              </tr>
            </tbody>
          </table>
          <p className="hint">
            Includes {formatPrice(vatTotal, store)} VAT. Paying by {paymentMethod}.
          </p>
        </section>
      )
    }
  }
}
