import { Link } from 'react-router'
import type { ProductSummary } from '../api'
import { formatPrice, useStore } from '../storeContext'

export function ProductCard({ product }: { product: ProductSummary }) {
  const store = useStore()

  return (
    <Link to={`/p/${product.slug}`} className="product-card">
      {product.imageUrl ? (
        <img src={product.imageUrl} alt={product.name} className="product-image" loading="lazy" />
      ) : (
        <div className="product-image product-image-placeholder" aria-hidden="true" />
      )}
      <span className="product-name">{product.name}</span>
      <span className="product-price">{formatPrice(product.price, store)}</span>
      {product.available === 0 && <span className="hint">Out of stock</span>}
    </Link>
  )
}
