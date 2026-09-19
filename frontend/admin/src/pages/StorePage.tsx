import { useState } from 'react'
import { useOutletContext, useParams } from 'react-router'
import { api, type AdminStore, type Category, type StoreProduct } from '../api'
import { useAction } from '../useAction'
import { useRequest } from '../useRequest'

type LayoutContext = { stores: AdminStore[]; reloadStores: () => void }

export function StorePage() {
  const { storeId = '' } = useParams()
  const { stores, reloadStores } = useOutletContext<LayoutContext>()
  const store = stores.find((candidate) => candidate.id === storeId)

  const [storeProducts, reloadStoreProducts] = useRequest(`store-products:${storeId}`, () => api.storeProducts(storeId))
  const [categories, reloadCategories] = useRequest(`categories:${storeId}`, () => api.categories(storeId))
  const [products] = useRequest('products', api.products)
  const [error, run] = useAction(() => {
    reloadStoreProducts()
    reloadCategories()
    reloadStores()
  })
  const [editing, setEditing] = useState<string | null>(null)

  if (!store) {
    return null
  }

  const categoryList = categories.status === 'ready' ? categories.data : []
  const listed = storeProducts.status === 'ready' ? storeProducts.data : []
  const unlisted = products.status === 'ready' ? products.data.filter((product) => !listed.some((item) => item.productId === product.id)) : []
  const money = new Intl.NumberFormat(store.culture, { style: 'currency', currency: store.currency })

  async function listProduct(form: FormData) {
    await run(() =>
      api.listProduct(storeId, String(form.get('productId')), {
        name: String(form.get('name')),
        price: Number(form.get('price')),
        isVisible: form.get('isVisible') === 'on',
        sortOrder: 0,
      }),
    )
  }

  return (
    <>
      <h1>{store.name}</h1>
      <p className="hint">
        {store.primaryHostName ?? 'No domain'} · {store.currency} · {store.culture}
      </p>
      {error && <p className="error">{error}</p>}

      <section>
        <h2>Branding</h2>
        <div className="inline-form">
          {store.logoUrl && <img src={store.logoUrl} alt={`${store.name} logo`} className="logo-preview" />}
          <label className="upload">
            {store.logoUrl ? 'Replace logo' : 'Upload logo'}
            <input
              type="file"
              accept="image/jpeg,image/png,image/webp"
              onChange={(event) => {
                const file = event.target.files?.[0]
                event.target.value = ''
                if (file) {
                  void run(() => api.uploadLogo(storeId, file))
                }
              }}
            />
          </label>
        </div>
      </section>

      <section>
        <h2>Categories</h2>
        <p className="chips">
          {categoryList.map((category) => (
            <span key={category.id} className="chip">
              {category.name}
            </span>
          ))}
        </p>
        <form action={(form) => run(() => api.createCategory(storeId, String(form.get('name'))))} className="inline-form">
          <input name="name" placeholder="New category" required />
          <button type="submit">Add category</button>
        </form>
      </section>

      <section>
        <h2>Products in this store</h2>
        <form action={listProduct} className="inline-form">
          <select name="productId" required defaultValue="">
            <option value="" disabled>
              Product to list…
            </option>
            {unlisted.map((product) => (
              <option key={product.id} value={product.id}>
                {product.sku}
              </option>
            ))}
          </select>
          <input name="name" placeholder="Name in this store" required />
          <input name="price" placeholder="Price" type="number" min="0" step="0.01" required />
          <label>
            <input name="isVisible" type="checkbox" defaultChecked /> Visible
          </label>
          <button type="submit">List product</button>
        </form>

        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>SKU</th>
              <th>Price</th>
              <th>Visible</th>
              <th>Categories</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {listed.map((item) =>
              editing === item.id ? (
                <EditRow
                  key={item.id}
                  item={item}
                  categories={categoryList}
                  onCancel={() => setEditing(null)}
                  onSave={(input, categoryIds) =>
                    run(async () => {
                      await api.updateStoreProduct(storeId, item.id, input)
                      await api.assignCategories(storeId, item.id, categoryIds)
                      setEditing(null)
                    })
                  }
                />
              ) : (
                <tr key={item.id}>
                  <td>{item.name}</td>
                  <td>{item.sku}</td>
                  <td>{money.format(item.price)}</td>
                  <td>{item.isVisible ? 'Yes' : 'Hidden'}</td>
                  <td>{categoryList.filter((category) => item.categoryIds.includes(category.id)).map((category) => category.name).join(', ')}</td>
                  <td>
                    <button type="button" onClick={() => setEditing(item.id)}>
                      Edit
                    </button>
                  </td>
                </tr>
              ),
            )}
          </tbody>
        </table>
      </section>
    </>
  )
}

type EditRowProps = {
  item: StoreProduct
  categories: Category[]
  onCancel: () => void
  onSave: (input: { name: string; slug: string; description: string | null; price: number; isVisible: boolean; sortOrder: number }, categoryIds: string[]) => void
}

function EditRow({ item, categories, onCancel, onSave }: EditRowProps) {
  function save(form: FormData) {
    onSave(
      {
        name: String(form.get('name')),
        slug: String(form.get('slug')),
        description: String(form.get('description')) || null,
        price: Number(form.get('price')),
        isVisible: form.get('isVisible') === 'on',
        sortOrder: item.sortOrder,
      },
      form.getAll('categoryIds').map(String),
    )
  }

  return (
    <tr>
      <td colSpan={6}>
        <form action={save} className="stack edit-form">
          <label>
            Name <input name="name" defaultValue={item.name} required />
          </label>
          <label>
            Slug <input name="slug" defaultValue={item.slug} required />
          </label>
          <label>
            Description <textarea name="description" defaultValue={item.description ?? ''} rows={3} />
          </label>
          <label>
            Price <input name="price" type="number" min="0" step="0.01" defaultValue={item.price} required />
          </label>
          <label>
            <input name="isVisible" type="checkbox" defaultChecked={item.isVisible} /> Visible in the storefront
          </label>
          <fieldset>
            <legend>Categories</legend>
            {categories.map((category) => (
              <label key={category.id}>
                <input name="categoryIds" type="checkbox" value={category.id} defaultChecked={item.categoryIds.includes(category.id)} /> {category.name}
              </label>
            ))}
          </fieldset>
          <p className="inline-form">
            <button type="submit">Save</button>
            <button type="button" onClick={onCancel}>
              Cancel
            </button>
          </p>
        </form>
      </td>
    </tr>
  )
}
