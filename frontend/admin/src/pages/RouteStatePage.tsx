import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { EmptyState } from "../components/ui/EmptyState";

export function NotFoundPage() {
  const { t } = useTranslation(["errors", "navigation"]);
  return (
    <EmptyState
      title={t("errors:pageNotFoundTitle")}
      action={<Link to="/products">{t("navigation:products")}</Link>}
    >
      {t("errors:pageNotFoundBody")}
    </EmptyState>
  );
}

export function ForbiddenPage() {
  const { t } = useTranslation(["errors", "navigation"]);
  return (
    <EmptyState
      title={t("errors:permissionTitle")}
      action={<Link to="/products">{t("navigation:products")}</Link>}
    >
      {t("errors:permissionBody")}
    </EmptyState>
  );
}
