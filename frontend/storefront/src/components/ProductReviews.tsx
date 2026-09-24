import { useState } from 'react'
import { getReviews } from '../api'
import { writeReview } from '../account'
import { RequestFailed } from '../cart'
import { useCustomer } from '../customerContext'
import { useStore } from '../storeContext'
import { useRequest } from '../useRequest'
import { Stars } from './Stars'

export function ProductReviews({ slug }: { slug: string }) {
  const store = useStore()
  const { customer } = useCustomer()
  const [attempt, setAttempt] = useState(0)
  const [sent, setSent] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)
  const answer = useRequest(`reviews:${slug}:${attempt}`, (signal) => getReviews(slug, signal))

  async function submit(form: FormData) {
    setProblem(null)

    try {
      await writeReview(slug, {
        rating: Number(form.get('rating')),
        text: String(form.get('text')).trim(),
        author: customer ? `${customer.firstName} ${customer.lastName.slice(0, 1)}.` : 'A customer',
      })
      setSent(true)
      setAttempt((current) => current + 1)
    } catch (exception) {
      setProblem(exception instanceof RequestFailed ? exception.message : 'The review could not be sent.')
    }
  }

  if (answer.status !== 'ready') {
    return null
  }

  const { canWrite, reviews } = answer.data

  if (reviews.length === 0 && !canWrite && !sent) {
    return null
  }

  return (
    <section className="reviews">
      <h2>What customers say</h2>

      {reviews.map((review) => (
        <article key={`${review.author}-${review.writtenAt}`} className="review">
          <p className="review-head">
            <Stars rating={review.rating} />
            <span className="hint">
              {review.author} · {new Date(review.writtenAt).toLocaleDateString(store.culture)}
            </span>
          </p>
          <p>{review.text}</p>
        </article>
      ))}

      {canWrite && !sent && (
        <form action={submit} className="review-form">
          <h3>Write a review</h3>
          <p className="hint">You bought this, so you can say what it is like.</p>
          <label>
            Rating
            <select name="rating" defaultValue="5">
              {[5, 4, 3, 2, 1].map((value) => (
                <option key={value} value={value}>
                  {value} {value === 1 ? 'star' : 'stars'}
                </option>
              ))}
            </select>
          </label>
          <label>
            Your review <textarea name="text" rows={3} maxLength={2000} required />
          </label>
          <button type="submit">Send review</button>
          {problem && <p className="error">{problem}</p>}
        </form>
      )}

      {sent && <p className="notice">Thank you — your review will appear once the store has read it.</p>}
    </section>
  )
}
