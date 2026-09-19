export const filterKey = (code: string) => `f.${code}`

// Returns a copy of the search parameters with one value changed; any change of filters or sort starts again at page 1.
export function withParam(params: URLSearchParams, key: string, value: string | null) {
  const next = new URLSearchParams(params)
  if (value === null || value === '') {
    next.delete(key)
  } else {
    next.set(key, value)
  }
  if (key !== 'page') {
    next.delete('page')
  }
  return next
}
