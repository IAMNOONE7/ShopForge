import { useEffect, useState } from 'react'
import { applyStore, fetchStore, type Store } from './store'

type StoreState =
  | { status: 'loading' }
  | { status: 'ready'; store: Store }
  | { status: 'not-found' }
  | { status: 'unavailable' }

function App() {
  const [state, setState] = useState<StoreState>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()

    fetchStore(controller.signal)
      .then((store) => {
        if (store) {
          applyStore(store)
          setState({ status: 'ready', store })
        } else {
          setState({ status: 'not-found' })
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setState({ status: 'unavailable' })
        }
      })

    return () => controller.abort()
  }, [])

  switch (state.status) {
    case 'loading':
      return null
    case 'not-found':
      return <Message title="Store not found" text="There is no store at this address." />
    case 'unavailable':
      return <Message title="Store unavailable" text="Please try again in a moment." />
    case 'ready':
      return (
        <>
          <header className="store-header">
            <h1>{state.store.name}</h1>
          </header>
          <main className="app">
            <section className="panel">
              <p>Prices are shown in {state.store.currency}.</p>
            </section>
          </main>
        </>
      )
  }
}

function Message({ title, text }: { title: string; text: string }) {
  return (
    <main className="app">
      <h1>{title}</h1>
      <p>{text}</p>
    </main>
  )
}

export default App
