import { useOutletContext, useParams } from 'react-router'
import { getProducts, type Category } from '../api'
import { Message } from '../components/Message'
import { ProductCard } from '../components/ProductCard'
import { useRequest } from '../useRequest'

export function ProductListPage() {
  const { slug } = useParams()
  const categories = useOutletContext<Category[]>()
  const products = useRequest(`products:${slug ?? ''}`, (signal) => getProducts(slug, signal))
  const title = slug ? (categories.find((category) => category.slug === slug)?.name ?? '') : 'All products'

  switch (products.status) {
    case 'loading':
      return null
    case 'not-found':
      return <Message title="Category not found" text="This category does not exist." />
    case 'error':
      return <Message title="Something went wrong" text="Products could not be loaded. Please try again." />
    case 'ready':
      return (
        <>
          <h1>{title}</h1>
          {products.data.items.length === 0 ? (
            <p>No products yet.</p>
          ) : (
            <div className="product-grid">
              {products.data.items.map((product) => (
                <ProductCard key={product.id} product={product} />
              ))}
            </div>
          )}
        </>
      )
  }
}
