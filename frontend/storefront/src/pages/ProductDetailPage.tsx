import { Link, useParams } from 'react-router'
import { getProduct } from '../api'
import { Message } from '../components/Message'
import { formatPrice, useStore } from '../storeContext'
import { useRequest } from '../useRequest'

export function ProductDetailPage() {
  const { slug = '' } = useParams()
  const store = useStore()
  const product = useRequest(`product:${slug}`, (signal) => getProduct(slug, signal))

  switch (product.status) {
    case 'loading':
      return null
    case 'not-found':
      return <Message title="Product not found" text="This product is not available." />
    case 'error':
      return <Message title="Something went wrong" text="The product could not be loaded. Please try again." />
    case 'ready': {
      const { name, price, description, images, categories } = product.data

      return (
        <article className="product-detail">
          <div className="product-gallery">
            {images.length === 0 ? (
              <div className="product-image product-image-placeholder" aria-hidden="true" />
            ) : (
              images.map((image) => <img key={image.url} src={image.url} alt={image.altText ?? name} className="product-image" />)
            )}
          </div>
          <div className="product-info">
            <h1>{name}</h1>
            <p className="product-price">{formatPrice(price, store)}</p>
            {description && <p>{description}</p>}
            {categories.length > 0 && (
              <p className="product-categories">
                {categories.map((category) => (
                  <Link key={category.slug} to={`/c/${category.slug}`}>
                    {category.name}
                  </Link>
                ))}
              </p>
            )}
          </div>
        </article>
      )
    }
  }
}
