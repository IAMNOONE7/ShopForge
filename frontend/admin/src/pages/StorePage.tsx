import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { DiscountsSection } from "../components/DiscountsSection";
import { MethodsSection } from "../components/MethodsSection";
import { PermissionScope } from "../components/PermissionScope";
import { ReviewsSection } from "../components/ReviewsSection";
import { StoreLogoSection } from "../components/StoreLogoSection";
import { StorePublicationSection } from "../components/StorePublicationSection";
import { StoreSettingsSection } from "../components/StoreSettingsSection";
import { canManageCatalog, canManageStore, useSession } from "../session";
import { currencyFormatter } from "../utils/format";

function StoreRoute({
  title,
  allowed,
  children,
}: {
  title: string;
  allowed: boolean;
  children: ReactNode;
}) {
  return (
    <div className="admin-route-section">
      <h1>{title}</h1>
      <PermissionScope allowed={allowed}>{children}</PermissionScope>
    </div>
  );
}

export function StoreSettingsPage() {
  const { t } = useTranslation("stores");
  const { user } = useSession();
  const { store, reloadStores, storeRefreshing, storeRefreshError } = useSelectedStore();
  return (
    <StoreRoute title={t("settings")} allowed={canManageStore(user.role)}>
      <div className="store-settings-workspace">
        <StorePublicationSection
          store={store}
          reloadStores={reloadStores}
          canManage={canManageStore(user.role)}
          storeRefreshing={storeRefreshing}
          storeRefreshError={storeRefreshError}
        />
        <StoreSettingsSection
          store={store}
          reloadStores={reloadStores}
          canManageDomains={canManageStore(user.role)}
        />
        <StoreLogoSection
          key={store.id}
          store={store}
          reloadStores={reloadStores}
        />
      </div>
    </StoreRoute>
  );
}

export function StoreMethodsPage() {
  const { t, i18n } = useTranslation("methods");
  const { user } = useSession();
  const { store, storeRefreshing, storeRefreshError } = useSelectedStore();
  return (
    <StoreRoute title={t("title")} allowed={canManageStore(user.role)}>
      <MethodsSection
        key={`${user.tenantId}:${user.id}:${user.role}:${store.id}:${store.currency}`}
        storeId={store.id}
        canManage={canManageStore(user.role)}
        storeStale={storeRefreshing || storeRefreshError !== null}
        money={currencyFormatter(store.currency, i18n.resolvedLanguage)}
      />
    </StoreRoute>
  );
}

export function StoreDiscountsPage() {
  const { t, i18n } = useTranslation("discounts");
  const { user } = useSession();
  const { store } = useSelectedStore();
  return (
    <StoreRoute title={t("title")} allowed={canManageStore(user.role)}>
      <DiscountsSection
        storeId={store.id}
        money={currencyFormatter(store.currency, i18n.resolvedLanguage)}
      />
    </StoreRoute>
  );
}

export function StoreReviewsPage() {
  const { t } = useTranslation("reviews");
  const { user } = useSession();
  const { store } = useSelectedStore();
  return (
    <StoreRoute title={t("title")} allowed={canManageCatalog(user.role)}>
      <ReviewsSection storeId={store.id} />
    </StoreRoute>
  );
}
