import { useState } from 'react'
import { getReturns, requestReturn } from '../account'
import { RequestFailed } from '../cart'
import type { Store } from '../store'
import { formatPrice, useStore } from '../storeContext'
import { useRequest } from '../useRequest'

export function OrderReturns({ number, onReturned }: { number: string; onReturned: () => void }) {
  const store = useStore()
  const [attempt, setAttempt] = useState(0)
  const [problem, setProblem] = useState<string | null>(null)
  const answer = useRequest(`returns:${number}:${attempt}`, () => getReturns(number))

  async function send(form: FormData) {
    setProblem(null)

    const lines = (answer.status === 'ready' ? answer.data.returnable : [])
      .map((line) => ({ storeProductId: line.storeProductId, quantity: Number(form.get(line.storeProductId) ?? 0) }))
      .filter((line) => line.quantity > 0)

    try {
      await requestReturn(number, lines, String(form.get('reason')).trim() || null)
      setAttempt((current) => current + 1)
      onReturned()
    } catch (exception) {
      setProblem(exception instanceof RequestFailed ? exception.message : 'The return could not be asked for.')
    }
  }

  if (answer.status !== 'ready') {
    return null
  }

  const { closesAt, returnable, returns } = answer.data

  return (
    <section className="returns">
      <h2>Sending something back</h2>

      {returns.length > 0 && (
        <ul className="return-list">
          {returns.map((sent) => (
            <li key={sent.number}>
              <span>
                {sent.number} · {sent.lines.map((line) => `${line.quantity} × ${line.productName}`).join(', ')}
              </span>
              <span className="hint">{state(sent.status, sent.refundedAmount, store)}</span>
            </li>
          ))}
        </ul>
      )}

      {returnable.length === 0 ? (
        <p className="hint">Nothing on this order can be sent back.</p>
      ) : (
        <form action={send} className="return-form">
          {closesAt && <p className="hint">You can send these back until {new Date(closesAt).toLocaleDateString(store.culture)}.</p>}
          {returnable.map((line) => (
            <label key={line.storeProductId}>
              {line.productName}
              <select name={line.storeProductId} defaultValue="0">
                {Array.from({ length: line.quantity + 1 }, (_, quantity) => (
                  <option key={quantity} value={quantity}>
                    {quantity}
                  </option>
                ))}
              </select>
            </label>
          ))}
          <label>
            Why are you sending it back? <textarea name="reason" rows={2} maxLength={1000} />
          </label>
          <button type="submit">Ask to send back</button>
          {problem && <p className="error">{problem}</p>}
        </form>
      )}
    </section>
  )
}

function state(status: string, refunded: number, store: Store) {
  switch (status) {
    case 'Requested':
      return 'Waiting for the store to agree'
    case 'Accepted':
      return 'Accepted — send it to us'
    case 'Refused':
      return 'The store cannot take it back'
    default:
      return `Refunded ${formatPrice(refunded, store)}`
  }
}
