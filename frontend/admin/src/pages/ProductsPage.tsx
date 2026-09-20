import { api, type Stock } from '../api'
import { useAction } from '../useAction'
import { useRequest } from '../useRequest'

export function ProductsPage() {
  const [products, reloadProducts] = useRequest('products', api.products)
  const [stock, reloadStock] = useRequest('stock', api.stock)
  const [error, run] = useAction(() => {
    reloadProducts()
    reloadStock()
  })
  const stockLevels: Stock[] = stock.status === 'ready' ? stock.data : []

  async function createProduct(form: FormData) {
    const weight = String(form.get('weightGrams'))
    await run(() =>
      api.createProduct({
        sku: String(form.get('sku')),
        ean: String(form.get('ean')) || null,
        weightGrams: weight ? Number(weight) : null,
      }),
    )
  }

  return (
    <>
      <h1>Products</h1>
      <p className="hint">
        Physical products are shared by all stores of your company. List them in a store to sell them there; stock is counted once, for all
        of them.
      </p>

      <form action={createProduct} className="inline-form">
        <input name="sku" placeholder="SKU" required />
        <input name="ean" placeholder="EAN (optional)" inputMode="numeric" />
        <input name="weightGrams" placeholder="Weight in grams" type="number" min="0" />
        <button type="submit">Add product</button>
      </form>
      {error && <p className="error">{error}</p>}

      {products.status === 'error' && <p className="error">{products.message}</p>}
      {products.status === 'ready' && (
        <table>
          <thead>
            <tr>
              <th>SKU</th>
              <th>EAN</th>
              <th>Weight</th>
              <th>Stock</th>
              <th>Images</th>
            </tr>
          </thead>
          <tbody>
            {products.data.map((product) => (
              <tr key={product.id}>
                <td>{product.sku}</td>
                <td>{product.ean ?? '—'}</td>
                <td>{product.weightGrams === null ? '—' : `${product.weightGrams} g`}</td>
                <td>
                  <form
                    className="inline-form compact"
                    action={(form) => run(() => api.setStock(product.id, Number(form.get('quantity'))))}
                    key={stockOf(stockLevels, product.id).onHand}
                  >
                    <input name="quantity" type="number" min="0" defaultValue={stockOf(stockLevels, product.id).onHand} aria-label="On hand" />
                    <button type="submit">Save</button>
                    {stockOf(stockLevels, product.id).reserved > 0 && (
                      <span className="hint">{stockOf(stockLevels, product.id).reserved} reserved</span>
                    )}
                  </form>
                </td>
                <td>
                  <div className="thumbnails">
                    {product.images.map((image) => (
                      <span key={image.id} className="thumbnail">
                        <img src={image.url} alt={image.altText ?? product.sku} />
                        <button type="button" aria-label="Remove image" onClick={() => run(() => api.deleteProductImage(product.id, image.id))}>
                          ×
                        </button>
                      </span>
                    ))}
                    <label className="upload">
                      Add image
                      <input
                        type="file"
                        accept="image/jpeg,image/png,image/webp"
                        onChange={(event) => {
                          const file = event.target.files?.[0]
                          event.target.value = ''
                          if (file) {
                            void run(() => api.uploadProductImage(product.id, file, product.sku))
                          }
                        }}
                      />
                    </label>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}

function stockOf(levels: Stock[], productId: string): Stock {
  return levels.find((level) => level.productId === productId) ?? { productId, onHand: 0, reserved: 0, available: 0 }
}
