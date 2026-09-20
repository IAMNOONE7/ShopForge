import { useState } from 'react'
import { useOutletContext, useParams } from 'react-router'
import { api, type AdminStore, type AttributeDefinition, type AttributeValues, type Category, type StoreProduct, type StoreProductInput } from '../api'
import { AttributesSection } from '../components/AttributesSection'
import { MethodsSection } from '../components/MethodsSection'
import { OrdersSection } from '../components/OrdersSection'
import { StoreSettingsSection } from '../components/StoreSettingsSection'
import { ImportSection } from '../components/ImportSection'
import { AttributeValueFields } from '../components/AttributeValueFields'
import { readAttributeValues } from '../components/attributeValues'
import { useAction } from '../useAction'
import { useRequest } from '../useRequest'

type LayoutContext = { stores: AdminStore[]; reloadStores: () => void }

export function StorePage() {
  const { storeId = '' } = useParams()
  const { stores, reloadStores } = useOutletContext<LayoutContext>()
  const store = stores.find((candidate) => candidate.id === storeId)

  const [storeProducts, reloadStoreProducts] = useRequest(`store-products:${storeId}`, () => api.storeProducts(storeId))
  const [categories, reloadCategories] = useRequest(`categories:${storeId}`, () => api.categories(storeId))
  const [attributes, reloadAttributes] = useRequest(`attributes:${storeId}`, () => api.attributes(storeId))
  const [products] = useRequest('products', api.products)
  const [error, run] = useAction(() => {
    reloadStoreProducts()
    reloadCategories()
    reloadAttributes()
    reloadStores()
  })
  const [editing, setEditing] = useState<string | null>(null)

  if (!store) {
    return null
  }

  const categoryList = categories.status === 'ready' ? categories.data : []
  const attributeList = attributes.status === 'ready' ? attributes.data : []
  const listed = storeProducts.status === 'ready' ? storeProducts.data : []
  const unlisted = products.status === 'ready' ? products.data.filter((product) => !listed.some((item) => item.productId === product.id)) : []
  const money = new Intl.NumberFormat(store.culture, { style: 'currency', currency: store.currency })

  async function listProduct(form: FormData) {
    await run(() =>
      api.listProduct(storeId, String(form.get('productId')), {
        name: String(form.get('name')),
        price: Number(form.get('price')),
        vatRate: Number(form.get('vatRate')),
        isVisible: form.get('isVisible') === 'on',
        sortOrder: 0,
      }),
    )
  }

  return (
    <>
      <h1>
        {store.name}
        {store.status === 'draft' && <span className="badge">draft</span>}
      </h1>
      <p className="hint">
        {store.primaryHostName ?? 'No address'} · {store.currency} · {store.culture}
      </p>
      <p className="inline-form">
        {store.status === 'draft' ? (
          <button type="button" onClick={() => run(() => api.publishStore(storeId))}>
            Publish store
          </button>
        ) : (
          <button type="button" onClick={() => run(() => api.unpublishStore(storeId))}>
            Unpublish store
          </button>
        )}
        {store.status === 'draft' && <span className="hint">A draft store does not serve its address yet.</span>}
      </p>
      {error && <p className="error">{error}</p>}

      <StoreSettingsSection store={store} theme={store.theme} run={run} />

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

      <MethodsSection storeId={storeId} money={money} run={run} />

      <AttributesSection storeId={storeId} attributes={attributeList} run={run} />

      <ImportSection storeId={storeId} run={run} />

      <section>
        <h2>Categories</h2>
        <p className="hint">Tick the attributes each category offers as filters.</p>
        {categoryList.map((category) => (
          <form
            key={category.id}
            className="inline-form"
            action={(form) => run(() => api.assignCategoryAttributes(storeId, category.id, form.getAll('attributeIds').map(String)))}
          >
            <strong className="chip">{category.name}</strong>
            {attributeList.map((attribute) => (
              <label key={attribute.id}>
                <input name="attributeIds" type="checkbox" value={attribute.id} defaultChecked={category.attributeIds.includes(attribute.id)} />{' '}
                {attribute.name}
              </label>
            ))}
            <button type="submit">Save</button>
          </form>
        ))}
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
          <input name="vatRate" placeholder="VAT %" type="number" min="0" max="100" step="0.01" defaultValue="21" required />
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
              <th>VAT</th>
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
                  storeId={storeId}
                  item={item}
                  categories={categoryList}
                  attributes={attributeList}
                  onCancel={() => setEditing(null)}
                  onSave={(input, categoryIds, values) =>
                    run(async () => {
                      await api.updateStoreProduct(storeId, item.id, input)
                      await api.assignCategories(storeId, item.id, categoryIds)
                      await api.setProductAttributes(storeId, item.id, values)
                      setEditing(null)
                    })
                  }
                />
              ) : (
                <tr key={item.id}>
                  <td>{item.name}</td>
                  <td>{item.sku}</td>
                  <td>{money.format(item.price)}</td>
                  <td>{item.vatRate}%</td>
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

      <OrdersSection storeId={storeId} money={money} culture={store.culture} />
    </>
  )
}

type EditRowProps = {
  storeId: string
  item: StoreProduct
  categories: Category[]
  attributes: AttributeDefinition[]
  onCancel: () => void
  onSave: (input: StoreProductInput, categoryIds: string[], values: AttributeValues) => void
}

function EditRow({ storeId, item, categories, attributes, onCancel, onSave }: EditRowProps) {
  const [current] = useRequest(`product-attributes:${item.id}`, () => api.productAttributes(storeId, item.id))

  function save(form: FormData) {
    onSave(
      {
        name: String(form.get('name')),
        slug: String(form.get('slug')),
        description: String(form.get('description')) || null,
        price: Number(form.get('price')),
        vatRate: Number(form.get('vatRate')),
        isVisible: form.get('isVisible') === 'on',
        sortOrder: item.sortOrder,
      },
      form.getAll('categoryIds').map(String),
      readAttributeValues(form, attributes),
    )
  }

  if (current.status !== 'ready') {
    return (
      <tr>
        <td colSpan={7}>{current.status === 'error' ? current.message : 'Loading…'}</td>
      </tr>
    )
  }

  return (
    <tr>
      <td colSpan={7}>
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
            VAT rate % <input name="vatRate" type="number" min="0" max="100" step="0.01" defaultValue={item.vatRate} required />
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
          {attributes.length > 0 && <AttributeValueFields attributes={attributes} values={current.data.values} />}
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
