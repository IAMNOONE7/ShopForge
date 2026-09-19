import { useEffect, useState } from 'react'
import { NotFoundError } from './api'

export type RequestState<T> =
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'not-found' }
  | { status: 'error' }

// `key` identifies the request: when it changes, the previous result is discarded and `load` runs again.
export function useRequest<T>(key: string, load: (signal: AbortSignal) => Promise<T>): RequestState<T> {
  const [result, setResult] = useState<{ key: string; state: RequestState<T> } | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    load(controller.signal)
      .then((data) => setResult({ key, state: { status: 'ready', data } }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setResult({ key, state: error instanceof NotFoundError ? { status: 'not-found' } : { status: 'error' } })
        }
      })

    return () => controller.abort()
  }, [key]) // eslint-disable-line react-hooks/exhaustive-deps

  return result?.key === key ? result.state : { status: 'loading' }
}
