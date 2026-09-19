import { useCallback, useEffect, useState } from 'react'

export type RequestState<T> = { status: 'loading' } | { status: 'ready'; data: T } | { status: 'error'; message: string }

// `key` identifies the request: when it changes, the previous result is discarded and `load` runs again.
// `reload` fetches the same key again, e.g. after a successful change.
export function useRequest<T>(key: string, load: () => Promise<T>): [RequestState<T>, () => void] {
  const [version, setVersion] = useState(0)
  const [result, setResult] = useState<{ key: string; state: RequestState<T> } | null>(null)

  useEffect(() => {
    let active = true

    load()
      .then((data) => active && setResult({ key, state: { status: 'ready', data } }))
      .catch((error: unknown) => active && setResult({ key, state: { status: 'error', message: String(error) } }))

    return () => {
      active = false
    }
  }, [key, version]) // eslint-disable-line react-hooks/exhaustive-deps

  const reload = useCallback(() => setVersion((current) => current + 1), [])

  return [result?.key === key ? result.state : { status: 'loading' }, reload]
}
