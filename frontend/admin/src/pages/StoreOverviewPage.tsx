import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useSelectedStore } from "../adminContext";

export function StoreOverviewPage() {
  const { t } = useTranslation(["stores", "navigation", "common"]);
  const { store } = useSelectedStore();
  const links = [
    ["settings", t("navigation:settings")],
    ["products", t("navigation:storeProducts")],
    ["categories", t("navigation:categories")],
    ["attributes", t("navigation:attributes")],
    ["import", t("navigation:import")],
    ["methods", t("navigation:methods")],
    ["discounts", t("navigation:discounts")],
    ["orders", t("navigation:orders")],
    ["returns", t("navigation:returns")],
    ["reviews", t("navigation:reviews")],
    ["operations", t("navigation:operations")],
  ];

  return (
    <>
      <h1 lang={store.culture}>
        {store.name}
        {store.status === "draft" && (
          <span className="badge">{t("common:draft")}</span>
        )}
      </h1>
      <p className="hint">
        {store.primaryHostName ?? t("stores:noAddress")} · {store.currency} ·{" "}
        {store.culture}
      </p>
      <p>{t("stores:overviewHint")}</p>
      <nav
        className="store-overview-links"
        aria-label={t("navigation:storeNavigation")}
      >
        {links.map(([path, label]) => (
          <Link key={path} to={path}>
            {label}
          </Link>
        ))}
      </nav>
    </>
  );
}
