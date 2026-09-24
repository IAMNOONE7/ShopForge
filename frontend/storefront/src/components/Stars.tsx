export function Stars({ rating, count }: { rating: number; count?: number }) {
  const shape = <span aria-hidden="true">{'★'.repeat(Math.round(rating)).padEnd(5, '☆')}</span>

  if (count === undefined) {
    return (
      <span className="stars" aria-label={`${rating} out of 5`}>
        {shape}
      </span>
    )
  }

  if (count === 0) {
    return <span className="stars none">No reviews yet</span>
  }

  return (
    <span className="stars" aria-label={`${rating} out of 5, ${count} ${count === 1 ? 'review' : 'reviews'}`}>
      {shape} {rating.toFixed(1)} ({count})
    </span>
  )
}
