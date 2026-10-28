import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { StoreLogoSection } from "../components/StoreLogoSection";
import { StorePublicationSection } from "../components/StorePublicationSection";
import { StoreSettingsSection } from "../components/StoreSettingsSection";
import { StoreRoute } from "../components/StoreRoute";
import { canManageStore, useSession } from "../session";

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
