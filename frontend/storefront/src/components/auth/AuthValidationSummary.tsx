import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import type { AuthIssue } from "../../auth";

export function AuthValidationSummary({ issues }: { issues: AuthIssue[] }) {
  const { t } = useTranslation("validation");
  const summary = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (issues.length > 0) summary.current?.focus();
  }, [issues]);

  if (issues.length === 0) return null;
  return (
    <div className="auth-validation request-error" role="alert" tabIndex={-1} ref={summary}>
      <p>{t("requiredFields")}</p>
      <ul>
        {issues.map((issue) => (
          <li key={issue.field}>
            <button
              type="button"
              className="link-button"
              onClick={() => document.querySelector<HTMLElement>(`[name="${issue.field}"]`)?.focus()}
            >
              {t(issue.key)}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
