import type { ButtonHTMLAttributes, ReactNode } from 'react'
import styles from './ui.module.css'

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  busy?: boolean
  busyLabel?: ReactNode
  variant?: 'primary' | 'secondary' | 'quiet' | 'danger'
}

export function Button({ busy = false, busyLabel, children, className, disabled, variant = 'primary', ...props }: ButtonProps) {
  const classes = [styles.button, styles[variant], className].filter(Boolean).join(' ')

  return (
    <button {...props} className={classes} disabled={disabled || busy} aria-busy={busy || undefined}>
      {busy ? (busyLabel ?? children) : children}
    </button>
  )
}
