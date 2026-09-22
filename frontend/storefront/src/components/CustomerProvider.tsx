import { useEffect, useState, type ReactNode } from 'react'
import { getProfile, type Customer } from '../account'
import { CustomerContext } from '../customerContext'

export function CustomerProvider({ children }: { children: ReactNode }) {
  const [customer, setCustomer] = useState<Customer | null>(null)

  useEffect(() => {
    getProfile()
      .then(setCustomer)
      .catch(() => setCustomer(null))
  }, [])

  return <CustomerContext value={{ customer, apply: setCustomer }}>{children}</CustomerContext>
}
