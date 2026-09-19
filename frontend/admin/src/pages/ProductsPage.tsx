import { api } from '../api'
import { useAction } from '../useAction'
import { useRequest } from '../useRequest'

export function ProductsPage() {
  const [products, reload] = useRequest('products', api.products)
  const [error, run] = useAction(reload)

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
      <p className="hint">Physical products are shared by all stores of your company. List them in a store to sell them there.</p>

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
