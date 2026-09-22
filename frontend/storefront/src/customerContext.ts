import { createContext, useContext } from 'react'
import type { Customer } from './account'

export type CustomerState = {
  customer: Customer | null
  apply: (customer: Customer | null) => void
}

export const CustomerContext = createContext<CustomerState | null>(null)

export function useCustomer(): CustomerState {
  const state = useContext(CustomerContext)

  if (!state) {
    throw new Error('useCustomer must be used inside CustomerContext.')
  }

  return state
}
