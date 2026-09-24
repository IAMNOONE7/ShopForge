import { useOutletContext, useParams, useSearchParams } from 'react-router'
import { getProducts, type Category, type Facet } from '../api'
import { FilterPanel } from '../components/filters/FilterPanel'
import { filterKey, withParam } from '../components/filters/filterParams'
import { Message } from '../components/Message'
import { ProductCard } from '../components/ProductCard'
import { LoadingState } from '../components/ui/LoadingState'
import { useRequest } from '../useRequest'

export function ProductListPage() {
  const { slug } = useParams()
  const categories = useOutletContext<Category[]>()
  const [searchParams, setSearchParams] = useSearchParams()
  const products = useRequest(`products:${slug ?? ''}?${searchParams}`, (signal) => getProducts(slug, searchParams, signal))
  const title = slug ? (categories.find((category) => category.slug === slug)?.name ?? '') : 'All products'

  function clearFilters() {
    const next = new URLSearchParams(searchParams)
    for (const key of [...next.keys()].filter((key) => key.startsWith('f.'))) {
      next.delete(key)
    }
    next.delete('page')
    setSearchParams(next)
  }

  switch (products.status) {
    case 'loading':
      return <LoadingState label="Loading products…" lines={6} />
    case 'not-found':
      return <Message title="Category not found" text="This category does not exist." />
    case 'error':
      return <Message title="Something went wrong" text="Products could not be loaded. Please try again." />
    case 'ready': {
      const { items, filters, totalCount, page, pageSize } = products.data
      const pageCount = Math.max(Math.ceil(totalCount / pageSize), 1)

      return (
        <>
          <h1>{title}</h1>
          <div className="catalog">
            <FilterPanel
              facets={filters}
              onChange={(code, value) => setSearchParams(withParam(searchParams, filterKey(code), value))}
              onClear={clearFilters}
            />
            <div className="catalog-results">
              <div className="catalog-toolbar">
                <span>
                  {totalCount} {totalCount === 1 ? 'product' : 'products'}
                </span>
                <select
                  aria-label="Sort by"
                  value={searchParams.get('sort') ?? ''}
                  onChange={(event) => setSearchParams(withParam(searchParams, 'sort', event.target.value))}
                >
                  {sortOptions(filters).map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
              </div>
              {items.length === 0 ? (
                <p>No products match these filters.</p>
              ) : (
                <div className="product-grid">
                  {items.map((product) => (
                    <ProductCard key={product.id} product={product} />
                  ))}
                </div>
              )}
              {pageCount > 1 && (
                <nav className="pagination" aria-label="Pages">
                  <button type="button" disabled={page <= 1} onClick={() => setSearchParams(withParam(searchParams, 'page', String(page - 1)))}>
                    Previous
                  </button>
                  <span>
                    Page {page} of {pageCount}
                  </span>
                  <button
                    type="button"
                    disabled={page >= pageCount}
                    onClick={() => setSearchParams(withParam(searchParams, 'page', String(page + 1)))}
                  >
                    Next
                  </button>
                </nav>
              )}
            </div>
          </div>
        </>
      )
    }
  }
}

function sortOptions(facets: Facet[]): [string, string][] {
  const attributeSorts = facets
    .filter((facet) => facet.type === 'integer' || facet.type === 'decimal' || facet.type === 'date')
    .flatMap((facet): [string, string][] => [
      [`attr.${facet.code}`, `${facet.name}: low to high`],
      [`-attr.${facet.code}`, `${facet.name}: high to low`],
    ])

  return [['', 'Recommended'], ['price', 'Price: low to high'], ['-price', 'Price: high to low'], ['name', 'Name'], ...attributeSorts]
}
