import { useId, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";

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
  const { t } = useTranslation(["account", "cart"]);
  return (
    <section className="customer-auth" aria-labelledby={heading}>
      <div className="account-form">
        <header className="auth-header">
          <p className="customer-eyebrow">{t("account:title")}</p>
          <h1 id={heading}>{title}</h1>
          {introduction && <div className="auth-introduction">{introduction}</div>}
          <Link className="customer-shopping-link" to="/">{t("cart:continueShopping")}</Link>
        </header>
        <div className="auth-content">
          {children}
          {footer && <footer className="auth-footer">{footer}</footer>}
        </div>
      </div>
    </section>
  );
}
