import { useState } from 'react'
import { requestPasswordReset } from '../account'

export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false)

  async function submit(form: FormData) {
    await requestPasswordReset(String(form.get('email')).trim()).catch(() => undefined)
    setSent(true)
  }

  if (sent) {
    return (
      <section className="account-form">
        <h1>Check your e-mail</h1>
        <p>If an account exists for that address, a link to choose a new password is on its way.</p>
      </section>
    )
  }

  return (
    <section className="account-form">
      <h1>Forgotten password</h1>
      <form action={submit}>
        <label>
          E-mail <input name="email" type="email" autoComplete="email" required />
        </label>
        <button type="submit">Send a reset link</button>
      </form>
    </section>
  )
}
