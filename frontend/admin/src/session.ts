import { createContext, useContext } from 'react'
import type { CurrentUser } from './api'

export type Session = { user: CurrentUser; logout: () => void }

export const SessionContext = createContext<Session | null>(null)

export function useSession(): Session {
  const session = useContext(SessionContext)

  if (!session) {
    throw new Error('useSession must be used inside SessionContext.')
  }

  return session
}
