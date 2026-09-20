import { useNavigate, useOutletContext } from 'react-router'
import { api } from '../api'
import { useAction } from '../useAction'

type LayoutContext = { reloadStores: () => void }

export function NewStorePage() {
  const navigate = useNavigate()
  const { reloadStores } = useOutletContext<LayoutContext>()
  const [error, run] = useAction(reloadStores)

  async function create(form: FormData) {
    await run(async () => {
      const store = await api.createStore({
        name: String(form.get('name')),
        hostName: String(form.get('hostName')),
        currency: String(form.get('currency')).toUpperCase(),
        culture: String(form.get('culture')),
        theme: {
          primaryColor: String(form.get('primaryColor')),
          secondaryColor: String(form.get('secondaryColor')),
          borderRadius: Number(form.get('borderRadius')),
        },
      })
      void navigate(`/stores/${store.id}`)
    })
  }

  return (
    <>
      <h1>New store</h1>
      <p className="hint">The store starts as a draft. It serves its address once you publish it.</p>
      <form action={create} className="stack edit-form">
        <label>
          Name <input name="name" required />
        </label>
        <label>
          Address <input name="hostName" placeholder="shop.example.com" required />
        </label>
        <label>
          Currency <input name="currency" defaultValue="EUR" maxLength={3} required />
        </label>
        <label>
          Language <input name="culture" defaultValue="en-IE" required />
        </label>
        <label>
          Primary color <input name="primaryColor" type="color" defaultValue="#1F6FEB" />
        </label>
        <label>
          Secondary color <input name="secondaryColor" type="color" defaultValue="#EEF4FF" />
        </label>
        <label>
          Corner radius <input name="borderRadius" type="number" min="0" max="32" defaultValue="6" />
        </label>
        <button type="submit">Create store</button>
        {error && <p className="error">{error}</p>}
      </form>
    </>
  )
}
