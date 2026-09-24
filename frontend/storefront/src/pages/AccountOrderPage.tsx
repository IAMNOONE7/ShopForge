import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { getAccountOrder } from '../account'
import { Message } from '../components/Message'
import { OrderReturns } from '../components/OrderReturns'
import { LoadingState } from '../components/ui/LoadingState'
import { formatPrice, useStore } from '../storeContext'
import { useRequest } from '../useRequest'

export function AccountOrderPage() {
  const { number = '' } = useParams()
  const store = useStore()
  const [attempt, setAttempt] = useState(0)
  const order = useRequest(`account-order:${number}:${attempt}`, () => getAccountOrder(number))

  switch (order.status) {
    case 'loading':
      return <LoadingState label="Loading order…" lines={6} />
    case 'not-found':
      return <Message title="Order not found" text="This order is not one of yours." />
    case 'error':
      return <Message title="Something went wrong" text="The order could not be loaded. Please try again." />
    case 'ready':
      return (
        <section className="order">
          <h1>Order {order.data.number}</h1>
          <p className="hint">
            Placed on {new Date(order.data.placedAt).toLocaleDateString(store.culture)} · <Link to="/account">back to your account</Link>
          </p>
          <table className="order-lines">
            <tbody>
              {order.data.lines.map((line) => (
                <tr key={line.productName}>
                  <td>{line.productName}</td>
                  <td>{line.quantity} ×</td>
                  <td>{formatPrice(line.unitPrice, store)}</td>
                  <td className="order-amount">{formatPrice(line.unitPrice * line.quantity, store)}</td>
                </tr>
              ))}
              <tr>
                <td colSpan={3}>{order.data.shippingMethod}</td>
                <td className="order-amount">{formatPrice(order.data.shippingPrice, store)}</td>
              </tr>
              <tr className="order-total">
                <td colSpan={3}>Total</td>
                <td className="order-amount">{formatPrice(order.data.grandTotal, store)}</td>
              </tr>
            </tbody>
          </table>
          {order.data.documents.length > 0 && (
            <p className="documents">
              {order.data.documents.map((document) => (
                <a key={document.number} href={`/api/storefront/account/orders/${order.data.number}/documents/${document.number}`}>
                  {document.kind === 'CreditNote' ? 'Credit note' : 'Invoice'} {document.number} (PDF)
                </a>
              ))}
            </p>
          )}

          <OrderReturns number={order.data.number} onReturned={() => setAttempt((current) => current + 1)} />
        </section>
      )
  }
}
