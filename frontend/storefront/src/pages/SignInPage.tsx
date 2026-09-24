import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { signIn } from '../account'
import { RequestFailed } from '../cart'
import { Button } from '../components/ui/Button'
import { Field } from '../components/ui/Field'
import { InlineMessage } from '../components/ui/InlineMessage'
import { useCustomer } from '../customerContext'

export function SignInPage() {
  const navigate = useNavigate()
  const { apply } = useCustomer()
  const [problem, setProblem] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit(form: FormData) {
    setProblem(null)
    setPending(true)

    try {
      apply(await signIn(String(form.get('email')).trim(), String(form.get('password'))))
      void navigate('/account')
    } catch (exception) {
      setProblem(exception instanceof RequestFailed ? exception.message : 'Signing in failed. Please try again.')
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="account-form">
      <h1>Sign in</h1>
      <form action={submit}>
        <Field label="E-mail" name="email" type="email" autoComplete="email" required />
        <Field label="Password" name="password" type="password" autoComplete="current-password" required />
        <Button type="submit" busy={pending} busyLabel="Signing in…">
          Sign in
        </Button>
        {problem && <InlineMessage tone="error">{problem}</InlineMessage>}
      </form>
      <p className="hint">
        <Link to="/account/register">Create an account</Link> · <Link to="/account/forgot-password">Forgotten password</Link>
      </p>
    </section>
  )
}
