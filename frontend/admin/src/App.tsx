import { useEffect, useState } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { ApiError, api, type CurrentUser } from './api'
import { Layout } from './components/Layout'
import { LoginPage } from './pages/LoginPage'
import { NewStorePage } from './pages/NewStorePage'
import { ProductsPage } from './pages/ProductsPage'
import { StorePage } from './pages/StorePage'
import { SessionContext } from './session'

type SessionState = { status: 'checking' } | { status: 'signed-out' } | { status: 'signed-in'; user: CurrentUser } | { status: 'error'; message: string }

function App() {
  const [session, setSession] = useState<SessionState>({ status: 'checking' })

  useEffect(() => {
    api
      .me()
      .then((user) => setSession({ status: 'signed-in', user }))
      .catch((error: unknown) =>
        setSession(error instanceof ApiError && error.status === 401 ? { status: 'signed-out' } : { status: 'error', message: String(error) }),
      )
  }, [])

  async function logout() {
    await api.logout()
    setSession({ status: 'signed-out' })
  }

  switch (session.status) {
    case 'checking':
      return null
    case 'error':
      return <p className="error">{session.message}</p>
    case 'signed-out':
      return <LoginPage onLogin={(user) => setSession({ status: 'signed-in', user })} />
    case 'signed-in':
      return (
        <SessionContext value={{ user: session.user, logout }}>
          <BrowserRouter>
            <Routes>
              <Route element={<Layout />}>
                <Route path="products" element={<ProductsPage />} />
                <Route path="stores/new" element={<NewStorePage />} />
                <Route path="stores/:storeId" element={<StorePage />} />
                <Route path="*" element={<Navigate to="/products" replace />} />
              </Route>
            </Routes>
          </BrowserRouter>
        </SessionContext>
      )
  }
}

export default App
