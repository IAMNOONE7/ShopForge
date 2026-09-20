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
  const order = useRequest(`order:${number}:${token}`, (signal) => getOrder(number, token, signal))

  switch (order.status) {
    case 'loading':
      return null
    case 'not-found':
      return <Message title="Order not found" text="Check the link from your confirmation e-mail." />
    case 'error':
      return <Message title="Something went wrong" text="The order could not be loaded. Please try again." />
    case 'ready': {
      const { lines, shippingMethod, shippingPrice, itemsTotal, vatTotal, grandTotal, email, paymentMethod } = order.data

      return (
        <section className="order">
          <h1>Thank you for your order</h1>
          <p>
            Order <strong>{order.data.number}</strong> was placed. A confirmation goes to {email}.
          </p>
          {instructions && <p className="notice">{instructions}</p>}
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
