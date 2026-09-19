import { useState } from 'react'

// Runs a change against the API, shows its error message if it fails and calls `onSuccess` (usually a reload) otherwise.
export function useAction(onSuccess: () => void): [string | null, (change: () => Promise<unknown>) => Promise<void>] {
  const [error, setError] = useState<string | null>(null)

  async function run(change: () => Promise<unknown>) {
    setError(null)

    try {
      await change()
      onSuccess()
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : String(exception))
    }
  }

  return [error, run]
}
