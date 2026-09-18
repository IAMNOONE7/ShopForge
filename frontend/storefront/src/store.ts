export type StoreTheme = {
  primaryColor: string
  secondaryColor: string
  borderRadius: number
}

export type Store = {
  id: string
  name: string
  currency: string
  culture: string
  theme: StoreTheme
}

export async function fetchStore(signal: AbortSignal): Promise<Store | null> {
  const response = await fetch('/api/storefront/store', { signal })

  if (response.status === 404) {
    return null
  }

  if (!response.ok) {
    throw new Error(`Loading the store failed with status ${response.status}.`)
  }

  return (await response.json()) as Store
}

export function applyStore(store: Store) {
  const root = document.documentElement

  root.lang = store.culture
  root.style.setProperty('--color-primary', store.theme.primaryColor)
  root.style.setProperty('--color-secondary', store.theme.secondaryColor)
  root.style.setProperty('--radius', `${store.theme.borderRadius}px`)
  document.title = store.name
}
