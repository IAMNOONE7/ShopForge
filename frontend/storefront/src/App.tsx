import { useEffect, useState } from 'react'
import { BrowserRouter, Route, Routes } from 'react-router'
import { Layout } from './components/Layout'
import { Message } from './components/Message'
import { ProductDetailPage } from './pages/ProductDetailPage'
import { ProductListPage } from './pages/ProductListPage'
import { applyStore, fetchStore, type Store } from './store'
import { StoreContext } from './storeContext'

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
      return (
        <main className="app">
          <Message title="Store not found" text="There is no store at this address." />
        </main>
      )
    case 'unavailable':
      return (
        <main className="app">
          <Message title="Store unavailable" text="Please try again in a moment." />
        </main>
      )
    case 'ready':
      return (
        <StoreContext value={state.store}>
          <BrowserRouter>
            <Routes>
              <Route element={<Layout />}>
                <Route index element={<ProductListPage />} />
                <Route path="c/:slug" element={<ProductListPage />} />
                <Route path="p/:slug" element={<ProductDetailPage />} />
                <Route path="*" element={<Message title="Page not found" text="This page does not exist." />} />
              </Route>
            </Routes>
          </BrowserRouter>
        </StoreContext>
      )
  }
}

export default App
