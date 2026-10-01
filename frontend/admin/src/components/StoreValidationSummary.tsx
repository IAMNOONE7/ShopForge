import { useTranslation } from "react-i18next";
import type { StoreIssue } from "./storeValidation";

export function StoreValidationSummary({
  issues,
  id,
}: {
  issues: StoreIssue[];
  id: string;
}) {
  const { t } = useTranslation("stores");
  if (issues.length === 0) return null;

  function focusField(name: string) {
    const field = document.getElementsByName(name)[0];
    if (!(field instanceof HTMLElement)) return;
    field.focus();
    field.scrollIntoView({ block: "center", behavior: "smooth" });
  }

  return (
    <div id={id} className="validation-summary" role="alert" tabIndex={-1}>
      <strong>{t("validationTitle")}</strong>
      <ul>
        {issues.map((issue, index) => (
          <li key={`${issue.field}-${issue.key}-${index}`}>
            <button
              type="button"
              className="link-button"
              onClick={() => focusField(issue.field)}
            >
              {t(`fields.${issue.field}`)}: {t(`validation.${issue.key}`)}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

