import { useId, type ReactNode } from "react";

export function AuthLayout({
  title,
  introduction,
  children,
  footer,
}: {
  title: ReactNode;
  introduction?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}) {
  const heading = useId();
  return (
    <section className="account-form" aria-labelledby={heading}>
      <header className="auth-header">
        <h1 id={heading}>{title}</h1>
        {introduction && <div className="auth-introduction">{introduction}</div>}
      </header>
      {children}
      {footer && <footer className="auth-footer">{footer}</footer>}
    </section>
  );
}
