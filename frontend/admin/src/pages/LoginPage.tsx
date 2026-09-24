import { useState } from 'react'
import { ApiError, api, type CurrentUser } from '../api'
import { Button } from '../components/ui/Button'
import { Field } from '../components/ui/Field'
import { InlineMessage } from '../components/ui/InlineMessage'

export function LoginPage({ onLogin }: { onLogin: (user: CurrentUser) => void }) {
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function login(form: FormData) {
    setError(null)
    setPending(true)

    try {
      onLogin(await api.login(String(form.get('email')), String(form.get('password'))))
    } catch (exception) {
      setError(exception instanceof ApiError && exception.status === 401 ? 'Invalid e-mail or password.' : String(exception))
    } finally {
      setPending(false)
    }
  }

  return (
    <main className="login">
      <h1>ShopForge Admin</h1>
      <form action={login} className="stack">
        <Field label="E-mail" name="email" type="email" autoComplete="username" required />
        <Field label="Password" name="password" type="password" autoComplete="current-password" required />
        <Button type="submit" busy={pending} busyLabel="Signing in…">
          Sign in
        </Button>
        {error && <InlineMessage tone="error">{error}</InlineMessage>}
      </form>
    </main>
  )
}
