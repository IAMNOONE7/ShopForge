import type { Facet } from '../../api'
import { BooleanFilter } from './BooleanFilter'
import { OptionsFilter } from './OptionsFilter'
import { RangeFilter } from './RangeFilter'

type FilterPanelProps = {
  facets: Facet[]
  onChange: (code: string, value: string | null) => void
  onClear: () => void
}

export function FilterPanel({ facets, onChange, onClear }: FilterPanelProps) {
  if (facets.length === 0) {
    return null
  }

  return (
    <aside className="filter-panel">
      {facets.map((facet) => (
        <fieldset key={facet.code}>
          <legend>
            {facet.name}
            {facet.unit && ` (${facet.unit})`}
          </legend>
          {facet.type === 'select' || facet.type === 'multiSelect' ? (
            <OptionsFilter facet={facet} onChange={(value) => onChange(facet.code, value)} />
          ) : facet.type === 'boolean' ? (
            <BooleanFilter facet={facet} onChange={(value) => onChange(facet.code, value)} />
          ) : (
            <RangeFilter facet={facet} onChange={(value) => onChange(facet.code, value)} />
          )}
        </fieldset>
      ))}
      <button type="button" className="link-button" onClick={onClear}>
        Clear filters
      </button>
    </aside>
  )
}
