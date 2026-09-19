import type { Facet } from '../../api'

export function RangeFilter({ facet, onChange }: { facet: Facet; onChange: (value: string | null) => void }) {
  const inputType = facet.type === 'date' ? 'date' : 'number'
  const step = facet.type === 'decimal' ? 'any' : undefined

  function apply(form: FormData) {
    const min = String(form.get('min') ?? '')
    const max = String(form.get('max') ?? '')
    onChange(min || max ? `${min}..${max}` : null)
  }

  if (facet.min === null && facet.selectedMin === null && facet.selectedMax === null) {
    return <p className="filter-count">No values</p>
  }

  return (
    <form action={apply} className="range-filter" key={`${facet.selectedMin}-${facet.selectedMax}`}>
      <input name="min" type={inputType} step={step} placeholder={String(facet.min ?? '')} defaultValue={facet.selectedMin ?? ''} aria-label={`${facet.name} from`} />
      <span>–</span>
      <input name="max" type={inputType} step={step} placeholder={String(facet.max ?? '')} defaultValue={facet.selectedMax ?? ''} aria-label={`${facet.name} to`} />
      <button type="submit">Apply</button>
    </form>
  )
}
