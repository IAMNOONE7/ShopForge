import { useTranslation } from "react-i18next";
import { useSelectedStore } from "../adminContext";
import { ReviewsSection } from "../components/ReviewsSection";
import { StoreRoute } from "../components/StoreRoute";
import { canManageCatalog, useSession } from "../session";

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
