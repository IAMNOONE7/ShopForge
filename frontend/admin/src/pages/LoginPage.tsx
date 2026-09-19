import { useState } from 'react'
import { ApiError, api, type CurrentUser } from '../api'

export function LoginPage({ onLogin }: { onLogin: (user: CurrentUser) => void }) {
  const [error, setError] = useState<string | null>(null)

  async function login(form: FormData) {
    try {
      onLogin(await api.login(String(form.get('email')), String(form.get('password'))))
    } catch (exception) {
      setError(exception instanceof ApiError && exception.status === 401 ? 'Invalid e-mail or password.' : String(exception))
    }
  }

  return (
    <main className="login">
      <h1>ShopForge Admin</h1>
      <form action={login} className="stack">
        <label>
          E-mail
          <input name="email" type="email" autoComplete="username" required />
        </label>
        <label>
          Password
          <input name="password" type="password" autoComplete="current-password" required />
        </label>
        <button type="submit">Sign in</button>
        {error && <p className="error">{error}</p>}
      </form>
    </main>
  )
}
