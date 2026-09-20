import { Link, useParams } from 'react-router'
import { getProduct, type ProductAttribute } from '../api'
import { AddToCart } from '../components/AddToCart'
import { Message } from '../components/Message'
import type { Store } from '../store'
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
      const { id, name, price, description, images, categories, attributes } = product.data

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
            <AddToCart storeProductId={id} />
            {description && <p>{description}</p>}
            {attributes.length > 0 && (
              <table className="product-attributes">
                <tbody>
                  {attributes.map((attribute) => (
                    <tr key={attribute.code}>
                      <th scope="row">{attribute.name}</th>
                      <td>{formatAttribute(attribute, store)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
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

function formatAttribute(attribute: ProductAttribute, store: Store) {
  const { value, unit } = attribute

  if (Array.isArray(value)) {
    return value.join(', ')
  }

  switch (attribute.type) {
    case 'boolean':
      return value ? 'Yes' : 'No'
    case 'date':
      return new Date(`${value}T00:00:00`).toLocaleDateString(store.culture)
    case 'integer':
    case 'decimal':
      return `${Number(value).toLocaleString(store.culture)}${unit ? ` ${unit}` : ''}`
    default:
      return String(value)
  }
}
