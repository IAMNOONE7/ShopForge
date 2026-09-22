import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { signIn } from '../account'
import { RequestFailed } from '../cart'
import { useCustomer } from '../customerContext'

export function SignInPage() {
  const navigate = useNavigate()
  const { apply } = useCustomer()
  const [problem, setProblem] = useState<string | null>(null)

  async function submit(form: FormData) {
    setProblem(null)

    try {
      apply(await signIn(String(form.get('email')).trim(), String(form.get('password'))))
      void navigate('/account')
    } catch (exception) {
      setProblem(exception instanceof RequestFailed ? exception.message : 'Signing in failed. Please try again.')
    }
  }

  return (
    <section className="account-form">
      <h1>Sign in</h1>
      <form action={submit}>
        <label>
          E-mail <input name="email" type="email" autoComplete="email" required />
        </label>
        <label>
          Password <input name="password" type="password" autoComplete="current-password" required />
        </label>
        <button type="submit">Sign in</button>
        {problem && <p className="error">{problem}</p>}
      </form>
      <p className="hint">
        <Link to="/account/register">Create an account</Link> · <Link to="/account/forgot-password">Forgotten password</Link>
      </p>
    </section>
  )
}
