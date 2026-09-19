import type { Facet } from '../../api'

export function BooleanFilter({ facet, onChange }: { facet: Facet; onChange: (value: string | null) => void }) {
  return (
    <label className="filter-option">
      <input
        type="checkbox"
        checked={facet.selected === true}
        disabled={facet.trueCount === 0 && facet.selected !== true}
        onChange={(event) => onChange(event.target.checked ? 'true' : null)}
      />
      Yes <span className="filter-count">({facet.trueCount ?? 0})</span>
    </label>
  )
}
