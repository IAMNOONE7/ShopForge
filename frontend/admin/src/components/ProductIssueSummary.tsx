import { useTranslation } from "react-i18next";
import type { ProductIssue, ProductField } from "./productValidation";

export function ProductIssueSummary({
  issues,
  id,
}: {
  issues: ProductIssue[];
  id: string;
}) {
  const { t } = useTranslation("products");
  if (!issues.length) return null;

  function focus(field: ProductField) {
    const element = document.getElementsByName(field)[0];
    if (element instanceof HTMLElement) {
      element.focus();
      element.scrollIntoView?.({ block: "center" });
    }
  }

  return (
    <div id={id} className="validation-summary" role="alert" tabIndex={-1}>
      <strong>{t("validationTitle")}</strong>
      <ul>
        {issues.map((issue) => (
          <li key={issue.field}>
            <button
              type="button"
              className="link-button"
              onClick={() => focus(issue.field)}
            >
              {t(`fields.${issue.field}`)}: {t(`validation.${issue.key}`)}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
