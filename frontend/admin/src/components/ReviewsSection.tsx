import { api, type Review } from '../api'
import { useAction } from '../useAction'
import { useRequest } from '../useRequest'

export function ReviewsSection({ storeId, culture }: { storeId: string; culture: string }) {
  const [reviews, reload] = useRequest(`reviews:${storeId}`, () => api.reviews(storeId))
  const [error, run] = useAction(reload)
  const all: Review[] = reviews.status === 'ready' ? reviews.data : []
  const waiting = all.filter((review) => review.status === 'Pending')

  return (
    <section>
      <h2>Reviews</h2>
      <p className="hint">Only a customer who bought the product can write one, and only what you publish is shown.</p>
      {error && <p className="error">{error}</p>}
      {reviews.status === 'ready' && all.length === 0 && <p className="hint">No reviews yet.</p>}

      {all.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Product</th>
              <th>Rating</th>
              <th>Review</th>
              <th>Written</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {all.map((review) => (
              <tr key={review.id}>
                <td>{review.productName}</td>
                <td>{'★'.repeat(review.rating)}</td>
                <td>
                  {review.text}
                  <span className="hint"> — {review.author}</span>
                </td>
                <td>{new Date(review.writtenAt).toLocaleDateString(culture)}</td>
                <td>{review.status}</td>
                <td className="inline-form compact">
                  {review.status !== 'Published' && (
                    <button type="button" onClick={() => run(() => api.publishReview(storeId, review.id))}>
                      Publish
                    </button>
                  )}
                  {review.status !== 'Rejected' && (
                    <button type="button" onClick={() => run(() => api.rejectReview(storeId, review.id))}>
                      Reject
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
