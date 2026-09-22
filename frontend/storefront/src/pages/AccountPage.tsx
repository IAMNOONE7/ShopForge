import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { getOrders, signOut, updateProfile } from '../account'
import { useCustomer } from '../customerContext'
import { formatPrice, useStore } from '../storeContext'
import { useRequest } from '../useRequest'

export function AccountPage() {
  const store = useStore()
  const navigate = useNavigate()
  const { customer, apply } = useCustomer()
  const orders = useRequest('account-orders', getOrders)
  const [saved, setSaved] = useState(false)

  async function save(form: FormData) {
    const phone = String(form.get('phone')).trim()

    apply(
      await updateProfile({
        firstName: String(form.get('firstName')).trim(),
        lastName: String(form.get('lastName')).trim(),
        phone: phone.length > 0 ? phone : null,
      }),
    )
    setSaved(true)
  }

  async function leave() {
    await signOut()
    apply(null)
    void navigate('/')
  }

  if (!customer) {
    return (
      <section className="account-form">
        <h1>Your account</h1>
        <p>
          <Link to="/account/sign-in">Sign in</Link> to see your orders.
        </p>
      </section>
    )
  }

  const history = orders.status === 'ready' ? orders.data : []

  return (
    <section className="account">
      <h1>Hello, {customer.firstName}</h1>
      <p className="hint">
        Signed in as {customer.email} ·{' '}
        <button type="button" className="link-button" onClick={() => void leave()}>
          Sign out
        </button>
      </p>

      <h2>Your orders</h2>
      {orders.status === 'ready' && history.length === 0 && <p className="hint">No orders yet.</p>}
      {history.length > 0 && (
        <table className="order-lines">
          <tbody>
            {history.map((order) => (
              <tr key={order.number}>
                <td>{order.number}</td>
                <td>{new Date(order.placedAt).toLocaleDateString(store.culture)}</td>
                <td>{status(order.status)}</td>
                <td>{order.items} items</td>
                <td className="order-amount">{formatPrice(order.grandTotal, store)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Your details</h2>
      <form action={save} className="account-form">
        <label>
          First name <input name="firstName" defaultValue={customer.firstName} required />
        </label>
        <label>
          Last name <input name="lastName" defaultValue={customer.lastName} required />
        </label>
        <label>
          Phone <input name="phone" type="tel" defaultValue={customer.phone ?? ''} />
        </label>
        <button type="submit">Save</button>
        {saved && <p className="hint">Saved.</p>}
      </form>
    </section>
  )
}

function status(value: string) {
  switch (value) {
    case 'AwaitingPayment':
      return 'Awaiting payment'
    case 'Paid':
      return 'Paid'
    case 'Shipped':
      return 'Shipped'
    default:
      return 'Cancelled'
  }
}
