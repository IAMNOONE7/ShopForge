import { useState } from 'react'
import { api, type AdminAddress, type AdminOrder } from '../api'
import { useRequest } from '../useRequest'

type OrdersSectionProps = {
  storeId: string
  money: Intl.NumberFormat
  culture: string
}

export function OrdersSection({ storeId, money, culture }: OrdersSectionProps) {
  const [orders] = useRequest(`orders:${storeId}`, () => api.orders(storeId))
  const [open, setOpen] = useState<string | null>(null)
  const list: AdminOrder[] = orders.status === 'ready' ? orders.data : []

  return (
    <section>
      <h2>Orders</h2>
      {orders.status === 'error' && <p className="error">{orders.message}</p>}
      {orders.status === 'ready' && list.length === 0 && <p className="hint">No orders yet.</p>}
      {list.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Number</th>
              <th>Placed</th>
              <th>Customer</th>
              <th>Items</th>
              <th>Total</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {list.map((order) => (
              <Rows
                key={order.number}
                storeId={storeId}
                order={order}
                money={money}
                culture={culture}
                isOpen={open === order.number}
                onToggle={() => setOpen(open === order.number ? null : order.number)}
              />
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

type RowsProps = {
  storeId: string
  order: AdminOrder
  money: Intl.NumberFormat
  culture: string
  isOpen: boolean
  onToggle: () => void
}

function Rows({ storeId, order, money, culture, isOpen, onToggle }: RowsProps) {
  return (
    <>
      <tr>
        <td>{order.number}</td>
        <td>{new Date(order.placedAt).toLocaleString(culture)}</td>
        <td>{order.email}</td>
        <td>{order.items}</td>
        <td>{money.format(order.grandTotal)}</td>
        <td>
          <button type="button" onClick={onToggle}>
            {isOpen ? 'Hide' : 'Details'}
          </button>
        </td>
      </tr>
      {isOpen && (
        <tr>
          <td colSpan={6}>
            <OrderDetail storeId={storeId} number={order.number} money={money} />
          </td>
        </tr>
      )}
    </>
  )
}

function OrderDetail({ storeId, number, money }: { storeId: string; number: string; money: Intl.NumberFormat }) {
  const [detail] = useRequest(`order:${storeId}:${number}`, () => api.order(storeId, number))

  if (detail.status !== 'ready') {
    return <p className={detail.status === 'error' ? 'error' : 'hint'}>{detail.status === 'error' ? detail.message : 'Loading…'}</p>
  }

  const order = detail.data

  return (
    <div className="order-detail">
      <table>
        <tbody>
          {order.lines.map((line) => (
            <tr key={line.productName}>
              <td>{line.productName}</td>
              <td>{line.quantity} ×</td>
              <td>{money.format(line.unitPrice)}</td>
              <td>{line.vatRate}% VAT</td>
              <td>{money.format(line.lineTotal)}</td>
            </tr>
          ))}
          <tr>
            <td colSpan={4}>{order.shippingMethod}</td>
            <td>{money.format(order.shippingPrice)}</td>
          </tr>
          <tr>
            <td colSpan={4}>Total, including {money.format(order.vatTotal)} VAT</td>
            <td>
              <strong>{money.format(order.grandTotal)}</strong>
            </td>
          </tr>
        </tbody>
      </table>
      <p className="hint">Paying by {order.paymentMethod}.</p>
      <div className="chips">
        <AddressBlock title="Billing" address={order.billingAddress} />
        <AddressBlock title="Shipping" address={order.shippingAddress} />
      </div>
    </div>
  )
}

function AddressBlock({ title, address }: { title: string; address: AdminAddress }) {
  return (
    <address>
      <strong>{title}</strong>
      <br />
      {address.fullName}
      <br />
      {address.line1}
      <br />
      {address.line2 && (
        <>
          {address.line2}
          <br />
        </>
      )}
      {address.postalCode} {address.city}
      <br />
      {address.country}
    </address>
  )
}
