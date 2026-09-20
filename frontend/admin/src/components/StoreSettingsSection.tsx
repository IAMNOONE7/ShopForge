import { api, type AdminStore, type StoreTheme } from '../api'

type StoreSettingsSectionProps = {
  store: AdminStore
  theme: StoreTheme
  run: (change: () => Promise<unknown>) => Promise<void>
}

export function StoreSettingsSection({ store, theme, run }: StoreSettingsSectionProps) {
  async function save(form: FormData) {
    await run(() =>
      api.updateStore(store.id, {
        name: String(form.get('name')),
        currency: String(form.get('currency')).toUpperCase(),
        culture: String(form.get('culture')),
        theme: {
          primaryColor: String(form.get('primaryColor')),
          secondaryColor: String(form.get('secondaryColor')),
          borderRadius: Number(form.get('borderRadius')),
        },
      }),
    )
  }

  return (
    <section>
      <h2>Settings</h2>
      <form action={save} className="stack edit-form" key={`${store.name}-${store.currency}-${store.culture}`}>
        <label>
          Name <input name="name" defaultValue={store.name} required />
        </label>
        <label>
          Currency <input name="currency" defaultValue={store.currency} maxLength={3} required disabled={store.status === 'published'} />
        </label>
        {store.status === 'published' && <p className="hint">A published store keeps its currency, because prices are stored in it.</p>}
        <label>
          Language <input name="culture" defaultValue={store.culture} required />
        </label>
        <label>
          Primary color <input name="primaryColor" type="color" defaultValue={theme.primaryColor} />
        </label>
        <label>
          Secondary color <input name="secondaryColor" type="color" defaultValue={theme.secondaryColor} />
        </label>
        <label>
          Corner radius <input name="borderRadius" type="number" min="0" max="32" defaultValue={theme.borderRadius} />
        </label>
        <button type="submit">Save settings</button>
      </form>
    </section>
  )
}
