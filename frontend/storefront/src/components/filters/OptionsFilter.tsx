import type { Facet } from '../../api'

export function OptionsFilter({ facet, onChange }: { facet: Facet; onChange: (value: string | null) => void }) {
  const selected = (facet.options ?? []).filter((option) => option.selected).map((option) => option.code)

  function toggle(code: string, checked: boolean) {
    const next = checked ? [...selected, code] : selected.filter((item) => item !== code)
    onChange(next.length > 0 ? next.join(',') : null)
  }

  return (
    <>
      {(facet.options ?? []).map((option) => (
        <label key={option.code} className="filter-option">
          <input
            type="checkbox"
            checked={option.selected}
            disabled={option.count === 0 && !option.selected}
            onChange={(event) => toggle(option.code, event.target.checked)}
          />
          {option.name} <span className="filter-count">({option.count})</span>
        </label>
      ))}
    </>
  )
}
