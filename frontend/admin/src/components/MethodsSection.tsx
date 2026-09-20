import { api, type PaymentMethod, type ShippingMethod } from '../api'
import { useRequest } from '../useRequest'

type MethodsSectionProps = {
  storeId: string
  money: Intl.NumberFormat
  run: (change: () => Promise<unknown>) => Promise<void>
}

export function MethodsSection({ storeId, money, run }: MethodsSectionProps) {
  const [payment, reloadPayment] = useRequest(`payment-methods:${storeId}`, () => api.paymentMethods(storeId))
  const [shipping, reloadShipping] = useRequest(`shipping-methods:${storeId}`, () => api.shippingMethods(storeId))
  const paymentMethods: PaymentMethod[] = payment.status === 'ready' ? payment.data : []
  const shippingMethods: ShippingMethod[] = shipping.status === 'ready' ? shipping.data : []

  return (
    <section>
      <h2>Checkout methods</h2>
      <p className="hint">The store settles these itself: the order is placed and the customer is told how to pay.</p>

      <h3>Payment</h3>
      {paymentMethods.map((method) => (
        <form
          key={method.code}
          className="inline-form"
          action={(form) =>
            run(async () => {
              await api.updatePaymentMethod(storeId, method.code, { name: String(form.get('name')), isActive: form.get('isActive') === 'on' })
              reloadPayment()
            })
          }
        >
          <input name="name" defaultValue={method.name} required />
          <label>
            <input name="isActive" type="checkbox" defaultChecked={method.isActive} /> Offered at checkout
          </label>
          <button type="submit">Save</button>
        </form>
      ))}
      <form
        className="inline-form"
        action={(form) =>
          run(async () => {
            await api.createPaymentMethod(storeId, String(form.get('name')))
            reloadPayment()
          })
        }
      >
        <input name="name" placeholder="New payment method" required />
        <button type="submit">Add</button>
      </form>

      <h3>Shipping</h3>
      {shippingMethods.map((method) => (
        <form
          key={method.code}
          className="inline-form"
          action={(form) =>
            run(async () => {
              await api.updateShippingMethod(storeId, method.code, {
                name: String(form.get('name')),
                price: Number(form.get('price')),
                vatRate: Number(form.get('vatRate')),
                isActive: form.get('isActive') === 'on',
              })
              reloadShipping()
            })
          }
        >
          <input name="name" defaultValue={method.name} required />
          <label>
            Price <input name="price" type="number" min="0" step="0.01" defaultValue={method.price} required />
          </label>
          <label>
            VAT % <input name="vatRate" type="number" min="0" max="100" step="0.01" defaultValue={method.vatRate} required />
          </label>
          <span className="hint">{money.format(method.price)}</span>
          <label>
            <input name="isActive" type="checkbox" defaultChecked={method.isActive} /> Offered at checkout
          </label>
          <button type="submit">Save</button>
        </form>
      ))}
      <form
        className="inline-form"
        action={(form) =>
          run(async () => {
            await api.createShippingMethod(storeId, {
              name: String(form.get('name')),
              price: Number(form.get('price')),
              vatRate: Number(form.get('vatRate')),
            })
            reloadShipping()
          })
        }
      >
        <input name="name" placeholder="New shipping method" required />
        <input name="price" type="number" min="0" step="0.01" placeholder="Price" required />
        <input name="vatRate" type="number" min="0" max="100" step="0.01" placeholder="VAT %" defaultValue="21" required />
        <button type="submit">Add</button>
      </form>
    </section>
  )
}
