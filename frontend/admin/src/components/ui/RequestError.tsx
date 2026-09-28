import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  errorMessageKey,
  fieldIssues,
  traceId,
  type ErrorOperation,
} from "../../api/errors";

export function RequestError({
  error,
  operation,
  onRetry,
}: {
  error: unknown;
  operation: ErrorOperation;
  onRetry?: () => void;
}) {
  const { t } = useTranslation(["errors", "validation", "common"]);
  const [copied, setCopied] = useState(false);
  const issues = fieldIssues(error);
  const reference = traceId(error);

  useEffect(() => {
    for (const issue of issues) {
      const field = findField(issue.target);
      field?.setAttribute("aria-invalid", "true");
    }
    return () => {
      for (const issue of issues)
        findField(issue.target)?.removeAttribute("aria-invalid");
    };
  }, [error]); // eslint-disable-line react-hooks/exhaustive-deps

  function focusField(name: string) {
    const field = findField(name);
    field?.focus();
    field?.scrollIntoView({ block: "center", behavior: "smooth" });
  }

  async function copyReference() {
    if (!reference) return;
    try {
      await navigator.clipboard.writeText(reference);
      setCopied(true);
    } catch {
      return;
    }
  }

  return (
    <div className="request-error" role="alert">
      <p>{t(`errors:${errorMessageKey(error, operation)}`)}</p>
      {issues.length > 0 && (
        <ul>
          {issues.map((issue) => (
            <li key={issue.field}>
              <button
                type="button"
                className="link-button"
                onClick={() => focusField(issue.target)}
              >
                {t(`validation:${issue.key}`)}
              </button>
            </li>
          ))}
        </ul>
      )}
      {reference && (
        <p className="trace-reference">
          {t("errors:traceReference", { traceId: reference })}{" "}
          <button
            type="button"
            className="link-button"
            onClick={() => void copyReference()}
          >
            {t(copied ? "common:copied" : "common:copy")}
          </button>
        </p>
      )}
      {onRetry && (
        <button type="button" onClick={onRetry}>
          {t("common:retry")}
        </button>
      )}
    </div>
  );
}

function findField(name: string): HTMLElement | null {
  const escaped = CSS.escape(name);
  return (
    document.querySelector<HTMLElement>(`[name="${escaped}"]`) ??
    document.getElementById(name)
  );
}
