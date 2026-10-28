import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { MethodsSection } from "../components/MethodsSection";
import { StoreRoute } from "../components/StoreRoute";
import { canManageStore, useSession } from "../session";
import { currencyFormatter } from "../utils/format";

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
