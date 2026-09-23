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
        company: {
          legalName: String(form.get('legalName')),
          line1: String(form.get('line1')),
          city: String(form.get('city')),
          postalCode: String(form.get('postalCode')),
          country: String(form.get('country')).toUpperCase(),
          registrationNumber: String(form.get('registrationNumber')),
          vatNumber: String(form.get('vatNumber')) || null,
        },
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
      <form action={save} className="stack edit-form" key={`${store.name}-${store.currency}-${store.culture}-${store.company?.legalName ?? ''}`}>
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
        <fieldset>
          <legend>Company details</legend>
          <p className="hint">These appear on invoices, and a store cannot go live without them.</p>
          <label>
            Legal name <input name="legalName" defaultValue={store.company?.legalName ?? store.name} required />
          </label>
          <label>
            Street and number <input name="line1" defaultValue={store.company?.line1 ?? ''} required />
          </label>
          <label>
            City <input name="city" defaultValue={store.company?.city ?? ''} required />
          </label>
          <label>
            Postal code <input name="postalCode" defaultValue={store.company?.postalCode ?? ''} required />
          </label>
          <label>
            Country <input name="country" defaultValue={store.company?.country ?? ''} maxLength={2} required />
          </label>
          <label>
            Registration number <input name="registrationNumber" defaultValue={store.company?.registrationNumber ?? ''} required />
          </label>
          <label>
            VAT number <input name="vatNumber" defaultValue={store.company?.vatNumber ?? ''} />
          </label>
        </fieldset>
        <button type="submit">Save settings</button>
      </form>
    </section>
  )
}
