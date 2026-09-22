import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { verifyEmail } from '../account'
import { Message } from '../components/Message'
import { useCustomer } from '../customerContext'

export function VerifyEmailPage() {
  const [parameters] = useSearchParams()
  const navigate = useNavigate()
  const { apply } = useCustomer()
  const [failed, setFailed] = useState(false)
  const token = parameters.get('token') ?? ''

  useEffect(() => {
    verifyEmail(token)
      .then((customer) => {
        apply(customer)
        void navigate('/account', { replace: true })
      })
      .catch(() => setFailed(true))
  }, [token]) // eslint-disable-line react-hooks/exhaustive-deps

  if (!failed) {
    return null
  }

  return (
    <>
      <Message title="This link no longer works" text="Confirmation links expire after a day and can only be used once." />
      <p className="hint">
        <Link to="/account/register">Register again</Link> to get a new one.
      </p>
    </>
  )
}
