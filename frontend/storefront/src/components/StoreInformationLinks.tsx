import { useTranslation } from "react-i18next";
import { NavLink } from "react-router";
import { storeInformationPages } from "../publicPages";

export function StoreInformationLinks({ className = "", heading = false, onNavigate }: {
  className?: string;
  heading?: boolean;
  onNavigate?: () => void;
}) {
  const { t } = useTranslation("navigation");
  return <nav className={`store-information-links ${className}`} aria-label={t("storeInformation")}>
    {heading && <h2>{t("storeInformation")}</h2>}
    {storeInformationPages.map(page => <NavLink key={page.slug} to={page.path} end onClick={onNavigate}>
      {t(page.label)}
    </NavLink>)}
  </nav>;
}
