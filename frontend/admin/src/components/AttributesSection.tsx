import { api, type AttributeDefinition, type AttributeType } from '../api'

const types: [AttributeType, string][] = [
  ['select', 'Select (one option)'],
  ['multiSelect', 'Multi-select'],
  ['decimal', 'Decimal number'],
  ['integer', 'Whole number'],
  ['boolean', 'Yes / no'],
  ['date', 'Date'],
  ['text', 'Text'],
]

type AttributesSectionProps = {
  storeId: string
  attributes: AttributeDefinition[]
  run: (change: () => Promise<unknown>) => Promise<void>
}

export function AttributesSection({ storeId, attributes, run }: AttributesSectionProps) {
  async function create(form: FormData) {
    const type = String(form.get('type')) as AttributeType
    await run(() =>
      api.createAttribute(storeId, {
        name: String(form.get('name')),
        type,
        unit: String(form.get('unit')) || null,
        isFilterable: type !== 'text' && form.get('isFilterable') === 'on',
        isVisibleOnProductPage: form.get('isVisibleOnProductPage') === 'on',
        options: String(form.get('options') ?? '')
          .split(',')
          .map((option) => option.trim())
          .filter(Boolean),
      }),
    )
  }

  return (
    <section>
      <h2>Attributes</h2>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Code</th>
            <th>Type</th>
            <th>Filter</th>
            <th>Shown</th>
            <th>Options</th>
          </tr>
        </thead>
        <tbody>
          {attributes.map((attribute) => (
            <tr key={attribute.id}>
              <td>
                {attribute.name}
                {attribute.unit && ` (${attribute.unit})`}
              </td>
              <td>{attribute.code}</td>
              <td>{types.find(([type]) => type === attribute.type)?.[1]}</td>
              <td>{attribute.isFilterable ? 'Yes' : '—'}</td>
              <td>{attribute.isVisibleOnProductPage ? 'Yes' : '—'}</td>
              <td>
                {attribute.options.map((option) => option.name).join(', ')}
                {(attribute.type === 'select' || attribute.type === 'multiSelect') && (
                  <form action={(form) => run(() => api.addOption(storeId, attribute.id, String(form.get('name'))))} className="inline-form compact">
                    <input name="name" placeholder="New option" required />
                    <button type="submit">Add</button>
                  </form>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <form action={create} className="inline-form">
        <input name="name" placeholder="Attribute name" required />
        <select name="type" defaultValue="select">
          {types.map(([type, label]) => (
            <option key={type} value={type}>
              {label}
            </option>
          ))}
        </select>
        <input name="unit" placeholder="Unit (optional)" size={8} />
        <input name="options" placeholder="Options, comma separated" />
        <label>
          <input name="isFilterable" type="checkbox" defaultChecked /> Filter
        </label>
        <label>
          <input name="isVisibleOnProductPage" type="checkbox" defaultChecked /> Show on product page
        </label>
        <button type="submit">Add attribute</button>
      </form>
    </section>
  )
}
