import { useId, type ReactNode } from 'react'
import styles from './ui.module.css'

type EmptyStateProps = {
  title: ReactNode
  children: ReactNode
  action?: ReactNode
  headingLevel?: 1 | 2
}

export function EmptyState({ title, children, action, headingLevel = 1 }: EmptyStateProps) {
  const titleId = useId()
  const Heading = headingLevel === 1 ? 'h1' : 'h2'

  return (
    <section className={styles.emptyState} aria-labelledby={titleId}>
      <Heading id={titleId}>{title}</Heading>
      <div className={styles.emptyText}>{children}</div>
      {action && <div className={styles.emptyAction}>{action}</div>}
    </section>
  )
}
