import { useState } from 'react'
import { Link } from 'react-router'
import { register } from '../account'
import { RequestFailed } from '../cart'

export function RegisterPage() {
  const [sent, setSent] = useState(false)
  const [problems, setProblems] = useState<string[]>([])

  async function submit(form: FormData) {
    setProblems([])

    try {
      const phone = String(form.get('phone')).trim()

      await register({
        email: String(form.get('email')).trim(),
        password: String(form.get('password')),
        firstName: String(form.get('firstName')).trim(),
        lastName: String(form.get('lastName')).trim(),
        phone: phone.length > 0 ? phone : null,
      })
      setSent(true)
    } catch (exception) {
      setProblems(exception instanceof RequestFailed ? exception.problems : ['Registration failed. Please try again.'])
    }
  }

  if (sent) {
    return (
      <section className="account-form">
        <h1>Check your e-mail</h1>
        <p>We sent you a link to confirm your address. Open it to finish creating your account.</p>
      </section>
    )
  }

  return (
    <section className="account-form">
      <h1>Create an account</h1>
      <form action={submit}>
        <label>
          First name <input name="firstName" autoComplete="given-name" required />
        </label>
        <label>
          Last name <input name="lastName" autoComplete="family-name" required />
        </label>
        <label>
          E-mail <input name="email" type="email" autoComplete="email" required />
        </label>
        <label>
          Phone (optional) <input name="phone" type="tel" autoComplete="tel" />
        </label>
        <label>
          Password <input name="password" type="password" autoComplete="new-password" minLength={10} required />
        </label>
        <button type="submit">Create account</button>
        {problems.map((problem) => (
          <p key={problem} className="error">
            {problem}
          </p>
        ))}
      </form>
      <p className="hint">
        Already have an account? <Link to="/account/sign-in">Sign in</Link>
      </p>
    </section>
  )
}
