import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { resetPassword } from '../account'
import { RequestFailed } from '../cart'

export function ResetPasswordPage() {
  const [parameters] = useSearchParams()
  const navigate = useNavigate()
  const [problem, setProblem] = useState<string | null>(null)

  async function submit(form: FormData) {
    setProblem(null)

    try {
      await resetPassword(parameters.get('token') ?? '', String(form.get('password')))
      void navigate('/account/sign-in', { replace: true })
    } catch (exception) {
      setProblem(exception instanceof RequestFailed ? exception.message : 'The password could not be changed.')
    }
  }

  return (
    <section className="account-form">
      <h1>Choose a new password</h1>
      <form action={submit}>
        <label>
          New password <input name="password" type="password" autoComplete="new-password" minLength={10} required />
        </label>
        <button type="submit">Save password</button>
        {problem && <p className="error">{problem}</p>}
      </form>
    </section>
  )
}
