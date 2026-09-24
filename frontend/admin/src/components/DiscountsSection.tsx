import { api, type Discount } from '../api'
import { useAction } from '../useAction'
import { useRequest } from '../useRequest'

export function DiscountsSection({ storeId, money }: { storeId: string; money: Intl.NumberFormat }) {
  const [discounts, reload] = useRequest(`discounts:${storeId}`, () => api.discounts(storeId))
  const [error, run] = useAction(reload)
  const codes: Discount[] = discounts.status === 'ready' ? discounts.data : []

  return (
    <section>
      <h2>Discount codes</h2>
      <p className="hint">A code is taken off the products in the cart, so every VAT rate keeps its share of it.</p>
      {error && <p className="error">{error}</p>}

      {codes.map((discount) => (
        <form
          key={discount.code}
          className="inline-form"
          action={(form) =>
            run(async () => {
              await api.updateDiscount(storeId, discount.code, {
                code: discount.code,
                name: String(form.get('name')),
                kind: discount.kind,
                value: Number(form.get('value')),
                minimumOrderAmount: optionalNumber(form.get('minimumOrderAmount')),
                startsAt: null,
                endsAt: optionalDate(form.get('endsAt')),
                maxRedemptions: optionalNumber(form.get('maxRedemptions')),
                maxRedemptionsPerCustomer: optionalNumber(form.get('maxRedemptionsPerCustomer')),
                isActive: form.get('isActive') === 'on',
              })
              reload()
            })
          }
        >
          <strong className="chip">{discount.code}</strong>
          <input name="name" defaultValue={discount.name} required />
          <span className="chip">{label(discount)}</span>
          {discount.kind !== 'FreeShipping' && (
            <label>
              Value <input name="value" type="number" min="0" step="0.01" defaultValue={discount.value} required />
            </label>
          )}
          <label>
            Minimum <input name="minimumOrderAmount" type="number" min="0" step="0.01" defaultValue={discount.minimumOrderAmount ?? ''} />
          </label>
          <label>
            Ends <input name="endsAt" type="date" defaultValue={discount.endsAt?.slice(0, 10) ?? ''} />
          </label>
          <label>
            Uses <input name="maxRedemptions" type="number" min="1" defaultValue={discount.maxRedemptions ?? ''} />
          </label>
          <label>
            Per customer <input name="maxRedemptionsPerCustomer" type="number" min="1" defaultValue={discount.maxRedemptionsPerCustomer ?? ''} />
          </label>
          <span className="hint">
            used {discount.redemptions}
            {discount.maxRedemptions === null ? '' : ` of ${discount.maxRedemptions}`}
          </span>
          <label>
            <input name="isActive" type="checkbox" defaultChecked={discount.isActive} /> Usable
          </label>
          <button type="submit">Save</button>
        </form>
      ))}

      <form
        className="inline-form"
        action={(form) =>
          run(async () => {
            await api.createDiscount(storeId, {
              code: String(form.get('code')),
              name: String(form.get('name')),
              kind: String(form.get('kind')),
              value: Number(form.get('value')),
              minimumOrderAmount: optionalNumber(form.get('minimumOrderAmount')),
              startsAt: null,
              endsAt: optionalDate(form.get('endsAt')),
              maxRedemptions: optionalNumber(form.get('maxRedemptions')),
              maxRedemptionsPerCustomer: optionalNumber(form.get('maxRedemptionsPerCustomer')),
              isActive: true,
            })
            reload()
          })
        }
      >
        <input name="code" placeholder="NEWCODE" maxLength={40} required />
        <input name="name" placeholder="What it is called" required />
        <select name="kind" defaultValue="Percentage">
          <option value="Percentage">Percentage off</option>
          <option value="Amount">Amount off</option>
          <option value="FreeShipping">Free shipping</option>
        </select>
        <input name="value" type="number" min="0" step="0.01" placeholder="Value" defaultValue="10" />
        <input name="minimumOrderAmount" type="number" min="0" step="0.01" placeholder="Minimum order" />
        <input name="endsAt" type="date" />
        <input name="maxRedemptions" type="number" min="1" placeholder="Total uses" />
        <input name="maxRedemptionsPerCustomer" type="number" min="1" placeholder="Per customer" />
        <button type="submit">Add code</button>
      </form>
      <p className="hint">Example: 10 % off with a minimum of {money.format(50)}.</p>
    </section>
  )
}

function label(discount: Discount) {
  switch (discount.kind) {
    case 'Percentage':
      return `${discount.value} % off`
    case 'Amount':
      return `${discount.value} off`
    default:
      return 'Free shipping'
  }
}

function optionalNumber(value: FormDataEntryValue | null) {
  const text = String(value ?? '').trim()

  return text.length === 0 ? null : Number(text)
}

function optionalDate(value: FormDataEntryValue | null) {
  const text = String(value ?? '').trim()

  return text.length === 0 ? null : new Date(`${text}T23:59:59Z`).toISOString()
}
