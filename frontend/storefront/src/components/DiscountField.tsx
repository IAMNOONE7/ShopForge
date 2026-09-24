import { useState } from 'react'
import { applyDiscount, removeDiscount, RequestFailed } from '../cart'
import { useCart } from '../cartContext'
import { formatPrice, useStore } from '../storeContext'

export function DiscountField() {
  const store = useStore()
  const { cart, apply } = useCart()
  const [problem, setProblem] = useState<string | null>(null)

  async function submit(form: FormData) {
    setProblem(null)

    try {
      apply(await applyDiscount(String(form.get('code')).trim()))
    } catch (exception) {
      setProblem(exception instanceof RequestFailed ? exception.message : 'The code could not be applied.')
    }
  }

  async function remove() {
    setProblem(null)
    apply(await removeDiscount())
  }

  if (!cart) {
    return null
  }

  if (cart.discount) {
    return (
      <p className="discount applied">
        <span>
          {cart.discount.name} (<strong>{cart.discount.code}</strong>) −{formatPrice(cart.discount.amount, store)}
        </span>
        <button type="button" className="link-button" onClick={() => void remove()}>
          Remove
        </button>
      </p>
    )
  }

  return (
    <form action={submit} className="discount">
      <label>
        <span className="visually-hidden">Discount code</span>
        <input name="code" placeholder="Discount code" autoComplete="off" required />
      </label>
      <button type="submit">Apply</button>
      {(problem ?? cart.discountProblem) && <span className="error">{problem ?? cart.discountProblem}</span>}
      {cart.discountProblem && !problem && (
        <button type="button" className="link-button" onClick={() => void remove()}>
          Remove code
        </button>
      )}
    </form>
  )
}
