import { api, type OrderReturn } from '../api'
import { useAction } from '../useAction'
import { useRequest } from '../useRequest'

export function ReturnsSection({ storeId, culture, money }: { storeId: string; culture: string; money: Intl.NumberFormat }) {
  const [returns, reload] = useRequest(`returns:${storeId}`, () => api.returns(storeId))
  const [error, run] = useAction(reload)
  const all: OrderReturn[] = returns.status === 'ready' ? returns.data : []
  const waiting = all.filter((sent) => sent.status === 'Requested' || sent.status === 'Accepted')

  return (
    <section>
      <h2>Returns</h2>
      <p className="hint">Money and stock move when you mark the parcel as received, not before.</p>
      {error && <p className="error">{error}</p>}
      {returns.status === 'ready' && all.length === 0 && <p className="hint">No returns yet.</p>}

      {all.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Return</th>
              <th>Order</th>
              <th>Items</th>
              <th>Asked for</th>
              <th>Status</th>
              <th>Refunded</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {all.map((sent) => (
              <tr key={sent.id}>
                <td>{sent.number}</td>
                <td>{sent.orderNumber}</td>
                <td>
                  {sent.lines.map((line) => `${line.quantity} × ${line.productName}`).join(', ')}
                  {sent.reason && <span className="hint"> — {sent.reason}</span>}
                </td>
                <td>{new Date(sent.requestedAt).toLocaleDateString(culture)}</td>
                <td>{sent.status}</td>
                <td>{sent.status === 'Received' ? money.format(sent.refundedAmount) : ''}</td>
                <td className="inline-form compact">
                  {sent.status === 'Requested' && (
                    <button type="button" onClick={() => run(() => api.decideReturn(storeId, sent.id, 'accept'))}>
                      Accept
                    </button>
                  )}
                  {sent.status === 'Accepted' && (
                    <button type="button" onClick={() => run(() => api.decideReturn(storeId, sent.id, 'receive'))}>
                      Mark as received
                    </button>
                  )}
                  {(sent.status === 'Requested' || sent.status === 'Accepted') && (
                    <button type="button" onClick={() => run(() => api.decideReturn(storeId, sent.id, 'refuse'))}>
                      Refuse
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {waiting.length > 0 && <p className="hint">{waiting.length} waiting for you.</p>}
    </section>
  )
}
