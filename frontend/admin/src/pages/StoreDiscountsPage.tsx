import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { DiscountsSection } from "../components/DiscountsSection";
import { StoreRoute } from "../components/StoreRoute";
import { canManageStore, useSession } from "../session";
import { currencyFormatter } from "../utils/format";

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
