import { api, type PaymentMethod, type ShippingMethod } from '../api'
import { useRequest } from '../useRequest'

type MethodsSectionProps = {
  storeId: string
  money: Intl.NumberFormat
  run: (change: () => Promise<unknown>) => Promise<void>
}

export function MethodsSection({ storeId, money, run }: MethodsSectionProps) {
  const [providers] = useRequest(`payment-providers:${storeId}`, () => api.paymentProviders(storeId))
  const [payment, reloadPayment] = useRequest(`payment-methods:${storeId}`, () => api.paymentMethods(storeId))
  const [shippingProviders] = useRequest(`shipping-providers:${storeId}`, () => api.shippingProviders(storeId))
  const [shipping, reloadShipping] = useRequest(`shipping-methods:${storeId}`, () => api.shippingMethods(storeId))
  const [pickupPoints, reloadPickupPoints] = useRequest(`pickup-points:${storeId}`, () => api.pickupPoints(storeId))
  const paymentMethods: PaymentMethod[] = payment.status === 'ready' ? payment.data : []
  const providerKeys = providers.status === 'ready' ? providers.data : ['manual']
  const shippingProviderKeys = shippingProviders.status === 'ready' ? shippingProviders.data : ['manual']
  const points = pickupPoints.status === 'ready' ? pickupPoints.data : []
  const shippingMethods: ShippingMethod[] = shipping.status === 'ready' ? shipping.data : []

  return (
    <section>
      <h2>Checkout methods</h2>
      <p className="hint">The store settles these itself: the order is placed and the customer is told how to pay.</p>

      <h3>Payment</h3>
      <p className="hint">
        A manual method is settled by the store itself; a provider method sends the shopper to the provider's payment page.
      </p>
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
          <span className="chip">{method.providerKey}</span>
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
            await api.createPaymentMethod(storeId, String(form.get('name')), String(form.get('providerKey')))
            reloadPayment()
          })
        }
      >
        <input name="name" placeholder="New payment method" required />
        <select name="providerKey" defaultValue="manual">
          {providerKeys.map((key) => (
            <option key={key} value={key}>
              {key}
            </option>
          ))}
        </select>
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
                requiresPickupPoint: form.get('requiresPickupPoint') === 'on',
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
          <span className="chip">{method.providerKey}</span>
          <label>
            <input name="requiresPickupPoint" type="checkbox" defaultChecked={method.requiresPickupPoint} /> Needs a pickup point
          </label>
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
              providerKey: String(form.get('providerKey')),
              price: Number(form.get('price')),
              vatRate: Number(form.get('vatRate')),
              requiresPickupPoint: form.get('requiresPickupPoint') === 'on',
            })
            reloadShipping()
          })
        }
      >
        <input name="name" placeholder="New shipping method" required />
        <input name="price" type="number" min="0" step="0.01" placeholder="Price" required />
        <input name="vatRate" type="number" min="0" max="100" step="0.01" placeholder="VAT %" defaultValue="21" required />
        <select name="providerKey" defaultValue="manual">
          {shippingProviderKeys.map((key) => (
            <option key={key} value={key}>
              {key}
            </option>
          ))}
        </select>
        <label>
          <input name="requiresPickupPoint" type="checkbox" /> Needs a pickup point
        </label>
        <button type="submit">Add</button>
      </form>

      <h3>Pickup points</h3>
      <p className="hint">Places this store hands parcels over itself. A shipping method that needs one offers these at checkout.</p>
      {points.map((point) => (
        <form
          key={point.code}
          className="inline-form"
          action={(form) =>
            run(async () => {
              await api.updatePickupPoint(storeId, point.code, {
                name: String(form.get('name')),
                line1: String(form.get('line1')),
                city: String(form.get('city')),
                postalCode: String(form.get('postalCode')),
                country: String(form.get('country')).toUpperCase(),
                isActive: form.get('isActive') === 'on',
              })
              reloadPickupPoints()
            })
          }
        >
          <input name="name" defaultValue={point.name} required />
          <input name="line1" defaultValue={point.line1} required />
          <input name="city" defaultValue={point.city} required />
          <input name="postalCode" defaultValue={point.postalCode} required />
          <input name="country" defaultValue={point.country} maxLength={2} required />
          <label>
            <input name="isActive" type="checkbox" defaultChecked={point.isActive} /> Open
          </label>
          <button type="submit">Save</button>
        </form>
      ))}
      <form
        className="inline-form"
        action={(form) =>
          run(async () => {
            await api.createPickupPoint(storeId, {
              name: String(form.get('name')),
              line1: String(form.get('line1')),
              city: String(form.get('city')),
              postalCode: String(form.get('postalCode')),
              country: String(form.get('country')).toUpperCase(),
              isActive: true,
            })
            reloadPickupPoints()
          })
        }
      >
        <input name="name" placeholder="New pickup point" required />
        <input name="line1" placeholder="Street and number" required />
        <input name="city" placeholder="City" required />
        <input name="postalCode" placeholder="Postal code" required />
        <input name="country" placeholder="IE" maxLength={2} required />
        <button type="submit">Add</button>
      </form>
    </section>
  )
}
